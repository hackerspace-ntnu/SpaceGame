// Drives the installed pastel quantize filter from a file on disk, so a look can be
// retuned without a recompile, an asset edit or a play-mode restart. Carries the blend
// and the whole palette shape: the shape is what most tuning actually moves, and it only
// reaches the renderer because PastelQuantizePass.EnsurePalette rebuilds on change.
//
// Nothing connects to the Editor. The browser lab POSTs to the local server that served
// it, that server writes LookLab/live/look.json, and this polls the file's timestamp —
// two halves that never meet, so there is no socket in the Editor to leak across a
// domain reload.
//
// Deliberately does not SetDirty or SaveAssets: the feature instance in memory is the
// one URP renders from, so persisting it is unnecessary for a preview and would rewrite
// the renderer assets on every slider drag. Live values are therefore ephemeral — a
// domain reload returns the committed ones, which is the intent.
using System;
using System.IO;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    [InitializeOnLoad]
    public static class LookLabLive
    {
        private const string ToggleMenuPath = "SpaceGame/Look Lab/Live Bridge";
        private const string EnabledPrefKey = "SpaceGame.LookLab.LiveBridge";

        /// <summary>Repo-root relative. Outside Assets/ on purpose: a write inside the
        /// project would trigger an asset import, and potentially a domain reload, on
        /// every parameter change.</summary>
        private const string LookFileRelativePath = "LookLab/live/look.json";

        // Polled rather than watched: FileSystemWatcher raises on a background thread and
        // does not survive domain reloads cleanly, while a timestamp read at 20 Hz is free.
        private const double PollIntervalSeconds = 0.05;

        private static double nextPollTime;
        private static DateTime lastWriteUtc = DateTime.MinValue;

        // What the renderer assets said before the bridge started overriding them, so
        // switching the bridge off leaves the project as it was found.
        private static bool restoreCaptured;
        private static bool restoreActive;
        private static float restoreBlend;
        private static PaletteShape restoreShape;

        static LookLabLive()
        {
            EditorApplication.update += Poll;
        }

        [MenuItem(ToggleMenuPath)]
        public static void Toggle()
        {
            bool enable = !IsEnabled;

            if (enable)
            {
                CaptureRestoreState();
            }
            else
            {
                RestoreState();
            }

            IsEnabled = enable;
            lastWriteUtc = DateTime.MinValue;
            Menu.SetChecked(ToggleMenuPath, enable);
            Debug.Log($"[LookLab] Live bridge {(enable ? "on" : "off")}, watching {LookFilePath}");
        }

        [MenuItem(ToggleMenuPath, true)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenuPath, IsEnabled);
            return true;
        }

        private static bool IsEnabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, false);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        private static string LookFilePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", LookFileRelativePath));

        private static void Poll()
        {
            if (!IsEnabled || EditorApplication.timeSinceStartup < nextPollTime)
            {
                return;
            }

            nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            string path = LookFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            DateTime writeUtc = File.GetLastWriteTimeUtc(path);
            if (writeUtc == lastWriteUtc)
            {
                return;
            }

            lastWriteUtc = writeUtc;
            Apply(path);
        }

        private static void Apply(string path)
        {
            LookState state;
            try
            {
                state = JsonUtility.FromJson<LookState>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                // Loudly: a look that silently fails to apply reads as "the bridge is broken".
                Debug.LogError($"[LookLab] Could not read {path}: {exception.Message}");
                return;
            }

            if (state == null)
            {
                Debug.LogError($"[LookLab] {path} parsed to nothing; expected an object with " +
                               "\"enabled\" and \"blend\".");
                return;
            }

            // Resolved once rather than per feature, so a bad shape is reported once with
            // the file that carried it rather than once per installed renderer.
            bool hasPalette = !state.palette.IsUnset;
            if (hasPalette && !state.palette.Validate(
                    PastelQuantizeRenderFeature.MaxPaletteSize, out string paletteError))
            {
                Debug.LogError($"[LookLab] {path} carries an unbuildable palette: {paletteError} " +
                               "Nothing was applied.");
                return;
            }

            int touched = ForEachFeature(pastel =>
            {
                pastel.SetActive(state.enabled);
                pastel.settings.blend = state.blend;
                if (hasPalette)
                {
                    // Both features share the one shape instance, and its arrays are
                    // read-only downstream — the next read of look.json builds fresh
                    // ones rather than writing into these.
                    pastel.settings.paletteShape = state.palette;
                }
            });

            if (touched == 0)
            {
                Debug.LogWarning("[LookLab] Pastel quantize filter is not installed; run " +
                                 "SpaceGame ▸ Environment ▸ Install Pastel Quantize Filter first.");
                return;
            }

            // In play mode the loop repaints itself (Run In Background is on); in edit mode
            // the Game view only redraws when something asks it to.
            if (!EditorApplication.isPlaying)
            {
                InternalEditorUtility.RepaintAllViews();
            }

            string palette = hasPalette
                ? $"palette={state.palette.EntryCount} colours"
                : "palette=committed";
            Debug.Log($"[LookLab] enabled={state.enabled} blend={state.blend:0.###} " +
                      $"{palette} on {touched} feature(s)");
        }

        private static void CaptureRestoreState()
        {
            restoreCaptured = false;
            ForEachFeature(pastel =>
            {
                if (restoreCaptured)
                {
                    return;
                }

                restoreActive = pastel.isActive;
                restoreBlend = pastel.settings.blend;
                restoreShape = pastel.settings.paletteShape;
                restoreCaptured = true;
            });
        }

        private static void RestoreState()
        {
            if (!restoreCaptured)
            {
                return;
            }

            ForEachFeature(pastel =>
            {
                pastel.SetActive(restoreActive);
                pastel.settings.blend = restoreBlend;
                pastel.settings.paletteShape = restoreShape;
            });

            restoreCaptured = false;
        }

        /// <summary>
        /// Applies an edit to every installed feature, so the PC and mobile renderers
        /// cannot drift into previewing different looks. Returns how many were touched.
        /// Unlike the install path in <see cref="PastelQuantizeSetup"/> this never marks
        /// anything dirty — see the note at the top of the file.
        /// </summary>
        private static int ForEachFeature(Action<PastelQuantizeRenderFeature> edit)
        {
            int touched = 0;

            foreach (var renderer in VolumetricSetup.FindRenderers())
            {
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature is PastelQuantizeRenderFeature pastel)
                    {
                        edit(pastel);
                        touched++;
                    }
                }
            }

            return touched;
        }

        [Serializable]
        private class LookState
        {
            public bool enabled;
            public float blend;

            /// <summary>
            /// Omit it and the committed shape is kept. JsonUtility cannot report a
            /// missing field, so an absent object arrives here as a zeroed struct —
            /// <see cref="PaletteShape.IsUnset"/> is how that is told apart from a shape
            /// somebody actually got wrong.
            /// </summary>
            public PaletteShape palette;
        }
    }
}
