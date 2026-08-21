using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Detects on-disk asset casing that has drifted from what git records, and enables
    /// the repository's git hooks so that it self-heals on future pulls.
    ///
    /// macOS and Windows use case-insensitive filesystems, so git cannot deliver a
    /// case-only rename: it writes the file into the directory that already exists under
    /// the old casing and then reports a clean tree. `git status` shows nothing.
    ///
    /// That silently breaks multiplayer. Unity reports whatever casing the DISK has, and
    /// Netcode identifies scenes across the network as XXHash32(full scene path), which is
    /// case-sensitive. Two machines whose casing differs compute different hashes for the
    /// same scene, so joining dies with:
    ///     Scene Hash &lt;n&gt; does not exist in the HashToBuildIndex table!
    ///
    /// The authoritative repair lives in Tools/fix-asset-casing.sh. This guard only
    /// reports, because renaming folders underneath a live Editor forces a reimport
    /// mid-session.
    /// </summary>
    [InitializeOnLoad]
    internal static class AssetCasingGuard
    {
        private const string HooksDir = ".githooks";
        private const string SessionKey = "SpaceGame.AssetCasingGuard.Ran";

        static AssetCasingGuard()
        {
            // The AssetDatabase is not usable from a static constructor.
            EditorApplication.delayCall += RunOncePerSession;
        }

        [MenuItem("Tools/Assets/Check Folder Casing")]
        private static void CheckFromMenu()
        {
            Check(interactive: true);
        }

        private static void RunOncePerSession()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EnsureHooksEnabled();
            Check(interactive: false);
        }

        private static string RepoRoot()
        {
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        }

        /// <summary>
        /// Points core.hooksPath at the tracked hooks directory, so that post-merge and
        /// post-checkout repair casing automatically from here on. Hooks live outside the
        /// repository by default, which is why a fresh clone cannot fix its own first pull.
        /// </summary>
        private static void EnsureHooksEnabled()
        {
            string root = RepoRoot();
            if (!Directory.Exists(Path.Combine(root, HooksDir)))
            {
                return;
            }

            string configured;
            if (!TryGit(root, "config --get core.hooksPath", out configured))
            {
                return;
            }

            configured = (configured ?? string.Empty).Trim().Replace('\\', '/');
            if (configured == HooksDir)
            {
                return;
            }

            // Never stomp a hooks directory somebody deliberately chose.
            bool isDefault = configured.Length == 0 || configured.EndsWith("/.git/hooks", StringComparison.Ordinal);
            if (!isDefault)
            {
                return;
            }

            string ignored;
            if (TryGit(root, "config core.hooksPath " + HooksDir, out ignored))
            {
                Debug.Log("[AssetCasingGuard] Enabled repository git hooks (core.hooksPath=" + HooksDir +
                          "). Asset casing will now be repaired automatically on pull.");
            }
        }

        private static void Check(bool interactive)
        {
            string root = RepoRoot();
            if (!Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git")))
            {
                return;
            }

            string trackedRaw;
            if (!TryGit(root, "ls-files -z", out trackedRaw))
            {
                if (interactive)
                {
                    Debug.LogWarning("[AssetCasingGuard] Could not run git, so casing was not verified.");
                }

                return;
            }

            string[] tracked = trackedRaw.Split('\0').Where(p => p.Length > 0).ToArray();
            if (tracked.Length == 0)
            {
                return;
            }

            // Map lower-cased path -> the casing the filesystem actually uses.
            var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string top in tracked.Select(p => p.Split('/')[0]).Distinct())
            {
                string abs = Path.Combine(root, top);
                if (Directory.Exists(abs))
                {
                    foreach (string file in Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(root.Length + 1).Replace('\\', '/');
                        onDisk[rel] = rel;
                    }
                }
                else if (File.Exists(abs))
                {
                    onDisk[top] = top;
                }
            }

            // Report the shallowest differing segment, so one wrong folder is one line
            // rather than several hundred.
            var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string want in tracked)
            {
                string real;
                if (!onDisk.TryGetValue(want, out real) || string.Equals(real, want, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] a = real.Split('/');
                string[] b = want.Split('/');
                var prefixReal = new StringBuilder();
                var prefixWant = new StringBuilder();
                for (int i = 0; i < a.Length && i < b.Length; i++)
                {
                    if (i > 0)
                    {
                        prefixReal.Append('/');
                        prefixWant.Append('/');
                    }

                    prefixReal.Append(a[i]);
                    prefixWant.Append(b[i]);
                    if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    {
                        offenders[prefixReal.ToString()] = prefixWant.ToString();
                        break;
                    }
                }
            }

            if (offenders.Count == 0)
            {
                if (interactive)
                {
                    Debug.Log("[AssetCasingGuard] Asset casing matches git.");
                }

                return;
            }

            var message = new StringBuilder();
            message.AppendLine("[AssetCasingGuard] On-disk asset casing does not match git. Multiplayer clients " +
                               "will fail to join with \"Scene Hash ... does not exist in the HashToBuildIndex table\", " +
                               "because Netcode hashes scene paths case-sensitively.");
            message.AppendLine();
            foreach (var pair in offenders)
            {
                message.AppendLine("    " + pair.Key + "  ->  " + pair.Value);
            }

            message.AppendLine();
            message.AppendLine("Fix: close Unity, then run  Tools/fix-asset-casing.sh");
            Debug.LogError(message.ToString());

            EditorUtility.DisplayDialog(
                "Asset folder casing is wrong",
                offenders.Count + " path(s) on disk do not match the casing git records.\n\n" +
                "Multiplayer joins will fail until this is fixed.\n\n" +
                "Close Unity and run:\n    Tools/fix-asset-casing.sh\n\n" +
                "See the Console for the full list.",
                "OK");
        }

        private static bool TryGit(string workingDirectory, string arguments, out string stdout)
        {
            stdout = null;
            try
            {
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    stdout = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(15000);
                    return process.HasExited && process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                // git missing or not permitted: stay silent rather than nagging.
                return false;
            }
        }
    }
}
