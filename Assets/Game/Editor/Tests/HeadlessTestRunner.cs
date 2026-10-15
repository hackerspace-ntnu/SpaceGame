// Runs EditMode tests and writes the result to a file, so a headless caller can read it.
//
// The Test Runner API is asynchronous: `TestRunnerApi.Execute` returns immediately and results
// arrive on a callback later. Anything driving the editor from outside — the MCP bridge, a script,
// CI — has no way to observe that callback, so the run has to leave a trace on disk.
//
// It lives in a file rather than being pasted into the bridge because the bridge compiles one
// flat class per command and hoists nested types out of it, which breaks any listener defined
// inline. It also has to survive the command that started it, and a static holder is what keeps
// the callback from being collected mid-run.
//
// It must also never raise a dialog. The Test Framework's first step,
// SaveCurrentModifiedScenesIfUserWantsTo, asks "Scene(s) Have Been Modified" MODALLY whenever a
// loaded scene is dirty, and a modal dialog blocks the editor loop — and with it the MCP bridge of
// every session sharing this editor — until someone at the keyboard answers. So the runner settles
// the scene question itself before it starts, and cleans up its own scratch after it ends.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class HeadlessTestRunner
    {
        /// <summary>Where results land. Temp/ is not imported by the AssetDatabase, so writing here
        /// does not kick off a domain reload in the middle of the run.</summary>
        public const string ResultPath = "Temp/headless_tests.txt";

        /// <summary>The scene a discarded scratch scene is replaced with: the game's entry scene.</summary>
        private const string BootstrapScenePath = "Assets/Game/Scenes/Core/Bootstrap.unity";

        /// <summary>
        /// What to do about the loaded scenes before a run. Ordered by severity, so the worst scene
        /// decides for the whole setup (<see cref="Worse"/>).
        /// </summary>
        public enum PreRunAction
        {
            /// <summary>Nothing unsaved: the framework will not prompt.</summary>
            Proceed,
            /// <summary>Unsaved changes in an untitled scene — scratch a test run or a probe left
            /// behind, never user work. Thrown away by reopening Bootstrap.</summary>
            DiscardScratch,
            /// <summary>Unsaved changes in a saved scene. Might be someone's work: neither save nor
            /// discard it, and do not run.</summary>
            Abort,
        }

        public static PreRunAction PreRunActionFor(bool isDirty, string scenePath)
        {
            if (!isDirty) return PreRunAction.Proceed;
            return string.IsNullOrEmpty(scenePath) ? PreRunAction.DiscardScratch : PreRunAction.Abort;
        }

        public static PreRunAction Worse(PreRunAction a, PreRunAction b) => a > b ? a : b;

        /// <summary>The result file of a run refused over a dirty saved scene. It ends in DONE like
        /// any other result, so a caller polling for DONE sees why nothing ran.</summary>
        public static string DirtySceneAbortResult(string scenePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ABORTED=dirty-scene {scenePath}");
            sb.AppendLine("DONE");
            return sb.ToString();
        }

        /// <summary>How long a pending request or an in-flight run is trusted before it is treated
        /// as stale. The Test Runner resumes a run across domain reloads by itself, so without this
        /// an unattended run cut short by a script edit could resume much later, with nobody
        /// watching, and write its fixtures into whatever scene happens to be open by then.</summary>
        private static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(10);

        // Static so neither the api nor the listener is collected while the run is in flight.
        private static TestRunnerApi api;
        private static ResultListener listener;

        /// <summary>Run every EditMode test whose fixture name matches, or — if null — every test in
        /// this project's own assemblies (<see cref="IsProjectAssembly"/>).</summary>
        public static void RunEditMode(string groupName = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HeadlessTestRunner] Refusing to start an EditMode run in play mode.");
                return;
            }

            if (File.Exists(ResultPath)) File.Delete(ResultPath);

            PreRunAction action = AssessLoadedScenes(out string dirtySavedScene);
            if (action == PreRunAction.Abort)
            {
                File.WriteAllText(ResultPath, DirtySceneAbortResult(dirtySavedScene));
                Debug.LogError($"[HeadlessTestRunner] Not running: '{dirtySavedScene}' has unsaved changes, " +
                               "and the Test Framework would stop on a modal save prompt. Save or revert it, then re-queue.");
                return;
            }
            if (action == PreRunAction.DiscardScratch) DiscardScratchScene();

            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(groupName))
                filter.groupNames = new[] { groupName };
            else
                filter.assemblyNames = ProjectAssemblyNames();

            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            listener = new ResultListener();
            api.RegisterCallbacks(listener);
            string runId = api.Execute(new ExecutionSettings(filter));

            SessionState.SetString(ActiveRunKey, runId);
            SessionState.SetString(ActiveRunStartedKey, Stamp(DateTime.UtcNow));
            WatchActiveRun();
        }

        /// <summary>Source root of the project's own code. Everything else — Packages/, including the
        /// embedded ones — belongs to someone else's test suite.</summary>
        private const string ProjectSourceRoot = "Assets/";

        /// <summary>
        /// Whether an assembly is compiled from this project's own sources (Assets/).
        ///
        /// An unfiltered run is narrowed to these because embedded packages' tests otherwise ride
        /// along — Netcode's <c>BuildTests.BasicBuildTest</c> builds a player over the run's dirty
        /// scratch scene, and that build can stop on the same modal save prompt mid-run, where no
        /// pre-run check can reach. Assets/ assemblies without tests cost nothing: the framework
        /// finds no fixtures in them. (Not narrowed further by an NUnit reference — in the editor
        /// every assembly carries one.) An explicit group filter still reaches package tests.
        /// </summary>
        public static bool IsProjectAssembly(string[] sourceFiles) =>
            Array.Exists(sourceFiles, f => f.Replace('\\', '/').StartsWith(ProjectSourceRoot, StringComparison.Ordinal));

        private static string[] ProjectAssemblyNames()
        {
            var names = new List<string>();
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                if (IsProjectAssembly(assembly.sourceFiles)) names.Add(assembly.name);
            }
            return names.ToArray();
        }

        /// <summary>The worst <see cref="PreRunAction"/> over every loaded scene, and the path of
        /// the first dirty saved scene when that is what decided it.</summary>
        private static PreRunAction AssessLoadedScenes(out string dirtySavedScene)
        {
            PreRunAction worst = PreRunAction.Proceed;
            dirtySavedScene = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                PreRunAction action = PreRunActionFor(scene.isDirty, scene.path);
                if (action == PreRunAction.Abort && dirtySavedScene == null) dirtySavedScene = scene.path;
                worst = Worse(worst, action);
            }
            return worst;
        }

        /// <summary>OpenScene in Single mode replaces every loaded scene without asking — the only
        /// scene API that discards unsaved changes without a prompt.</summary>
        private static void DiscardScratchScene()
        {
            Debug.Log("[HeadlessTestRunner] Discarding an unsaved untitled scratch scene; reopening Bootstrap.");
            EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Whether any Test Framework run is in flight in this editor — ours, another session's, or
        /// the Test Runner window's.
        ///
        /// A run spends its whole life inside an untitled scratch scene its tests dirty. A second
        /// run started meanwhile meets that scene at its first step and prompts to save it; and our
        /// own pre-run discard would reopen Bootstrap under the first run's feet, so its remaining
        /// tests would build their objects into Bootstrap. The framework only exposes this check
        /// internally, hence the reflection.
        /// </summary>
        private static readonly Func<bool> IsAnyTestRunActive = ResolveIsRunActive();

        private static Func<bool> ResolveIsRunActive()
        {
            MethodInfo method = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null || method.ReturnType != typeof(bool) || method.GetParameters().Length != 0)
            {
                Debug.LogError("[HeadlessTestRunner] TestRunnerApi.IsRunActive() no longer exists in this Test Framework " +
                               "version. Queued runs can no longer wait for a run already in flight and may prompt " +
                               "over its scratch scene; find the replacement check before relying on this runner.");
                return () => false;
            }
            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
        }

        /// <summary>Set while a deferred run is pending. Survives the domain reload — see below.</summary>
        private const string PendingKey = "SpaceGame.HeadlessTests.Pending";

        /// <summary>When the pending request was made, so a stale one can be told apart from a
        /// fresh one on the far side of however many domain reloads.</summary>
        private const string PendingRequestedKey = "SpaceGame.HeadlessTests.PendingRequestedAt";

        /// <summary>The guid <see cref="TestRunnerApi.Execute"/> returned for the run currently in
        /// flight, so it can be cancelled if it runs long. Cleared once the run finishes.</summary>
        private const string ActiveRunKey = "SpaceGame.HeadlessTests.ActiveRun";

        /// <summary>When the active run started.</summary>
        private const string ActiveRunStartedKey = "SpaceGame.HeadlessTests.ActiveRunStartedAt";

        /// <summary>
        /// As <see cref="RunEditMode"/>, but started once the editor is next idle.
        ///
        /// Two reasons it cannot just be called directly. The MCP bridge refuses a command that
        /// asks the editor to do something interactive while it is still inside the call, and
        /// starting a test run counts. And a plain <c>delayCall</c> is not enough either: any
        /// script edit — including the one being tested — triggers a domain reload that throws the
        /// pending callback away, so a run scheduled next to a code change silently never happened,
        /// which reads exactly like a run that is still going.
        ///
        /// <see cref="SessionState"/> outlives the reload, so the request is picked up on the far
        /// side of it by <see cref="ResumeAfterReload"/>.
        /// </summary>
        public static void RunEditModeDeferred(string groupName = null)
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            SessionState.SetString(PendingKey, groupName ?? string.Empty);
            SessionState.SetString(PendingRequestedKey, Stamp(DateTime.UtcNow));
            Schedule();
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            Schedule();
            // A run in flight when the reload happened is resumed by the Test Runner itself on
            // the far side of it, but the watcher that cancels a stale one is a plain update
            // subscription and does not survive the reload with it. Re-arm unconditionally: if
            // there is no active run, the next tick finds ActiveRunStartedKey missing, treats it
            // as stale, and unsubscribes itself — a harmless no-op.
            WatchActiveRun();
        }

        /// <summary>
        /// Arms the pump below.
        ///
        /// Not <c>EditorApplication.delayCall</c>, which is what this used to be: that queue is only
        /// drained by the editor's interactive tick, so with the Unity window in the background —
        /// the normal state for a headless caller driving it from a terminal — a scheduled run
        /// simply never started, and a run that never started is indistinguishable from one still
        /// going. <c>EditorApplication.update</c> keeps ticking, so the request survives being
        /// unfocused.
        /// </summary>
        private static void Schedule()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        /// <summary>Waits for the editor to go idle and for any other run to finish, then starts the
        /// run once and unhooks itself.</summary>
        private static void Pump()
        {
            if (!IsIdle()) return;

            EditorApplication.update -= Pump;
            StartIfPending();
        }

        private static bool IsIdle() =>
            !EditorApplication.isCompiling && !EditorApplication.isUpdating && !IsAnyTestRunActive();

        /// <summary>
        /// Leaves the editor on a clean scene once the run is fully over, so the next caller does
        /// not start over a dirty scratch. Not done inside <c>RunFinished</c>: that fires before the
        /// framework's own <c>RestoreSceneSetupTask</c>, while the run's scratch scene is still
        /// active. A dirty saved scene is left alone — that is someone's work.
        /// </summary>
        private static void CleanUpAfterRun()
        {
            if (!IsIdle()) return;

            EditorApplication.update -= CleanUpAfterRun;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssessLoadedScenes(out _) == PreRunAction.DiscardScratch) DiscardScratchScene();
        }

        private static void StartIfPending()
        {
            string pending = SessionState.GetString(PendingKey, null);
            if (pending == null) return;

            // A request that has outlived MaxRequestAge belongs to a caller that is long gone —
            // starting it now would run in whatever scene happens to be open, for nobody watching.
            if (IsStale(PendingRequestedKey))
            {
                ClearPending();
                Debug.LogWarning("[HeadlessTestRunner] Discarded a pending EditMode run request because " +
                                 $"it was more than {MaxRequestAge.TotalMinutes} minutes old. Re-request it.");
                return;
            }

            // Entering play mode is itself a domain reload, so a pending request left over from an
            // earlier session gets re-armed by ResumeAfterReload and lands here mid-Play. The test
            // framework's first step is SaveCurrentModifiedScenesIfUserWantsTo, which throws in play
            // mode and then cascades through the teardown tasks. Drop the request instead: an
            // EditMode run started from inside a play session was never what the caller asked for.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                ClearPending();
                Debug.LogWarning("[HeadlessTestRunner] Discarded a pending EditMode run because the " +
                                 "editor entered play mode. Re-request it from the edit-mode editor.");
                return;
            }

            // Cleared before starting, not after: a run that itself triggers a reload must not come
            // back round and start a second one.
            ClearPending();
            RunEditMode(string.IsNullOrEmpty(pending) ? null : pending);
        }

        private static void ClearPending()
        {
            SessionState.EraseString(PendingKey);
            SessionState.EraseString(PendingRequestedKey);
        }

        private static void WatchActiveRun()
        {
            EditorApplication.update -= CancelStaleRun;
            EditorApplication.update += CancelStaleRun;
        }

        /// <summary>
        /// Cancels the run once it is older than <see cref="MaxRequestAge"/>. The Test Runner resumes
        /// a run on the far side of every domain reload by itself, so a run cut short by a script
        /// edit can pick up again long after whoever started it has gone, in whatever scene is open
        /// by then.
        /// </summary>
        private static void CancelStaleRun()
        {
            if (!IsStale(ActiveRunStartedKey)) return;

            string runId = SessionState.GetString(ActiveRunKey, null);
            ForgetActiveRun();
            // False when the run already finished without our listener hearing it (a reload
            // drops the listener), which is nothing to cancel.
            if (runId == null || !TestRunnerApi.CancelTestRun(runId)) return;

            File.WriteAllText(ResultPath, $"CANCELLED: still running {MaxRequestAge.TotalMinutes} minutes " +
                                          "after it started\nDONE\n");
            Debug.LogWarning("[HeadlessTestRunner] Cancelled an EditMode run that started more than " +
                             $"{MaxRequestAge.TotalMinutes} minutes ago.");
        }

        private static void ForgetActiveRun()
        {
            EditorApplication.update -= CancelStaleRun;
            SessionState.EraseString(ActiveRunKey);
            SessionState.EraseString(ActiveRunStartedKey);
        }

        /// <summary>True when the timestamp under <paramref name="key"/> is older than
        /// <see cref="MaxRequestAge"/>, or missing or unreadable — an undated request is treated
        /// as stale rather than trusted.</summary>
        private static bool IsStale(string key)
        {
            string stamp = SessionState.GetString(key, null);
            if (!long.TryParse(stamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                return true;

            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > MaxRequestAge;
        }

        private static string Stamp(DateTime utc) => utc.Ticks.ToString(CultureInfo.InvariantCulture);

        [MenuItem("Tools/Tests/Run EditMode Tests (headless)")]
        private static void RunAll() => RunEditMode();

        private class ResultListener : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"PASSED={result.PassCount} FAILED={result.FailCount} " +
                              $"SKIPPED={result.SkipCount} INCONCLUSIVE={result.InconclusiveCount}");
                AppendFailures(result, sb);
                sb.AppendLine("DONE");

                File.WriteAllText(ResultPath, sb.ToString());
                ForgetActiveRun();

                if (api != null && listener != null) api.UnregisterCallbacks(listener);
                api = null;
                listener = null;

                EditorApplication.update -= CleanUpAfterRun;
                EditorApplication.update += CleanUpAfterRun;
            }

            /// Only failures are written out. A passing run's useful content is its counts; listing
            /// every passing test buries the one line that matters.
            private static void AppendFailures(ITestResultAdaptor result, StringBuilder sb)
            {
                if (!result.HasChildren)
                {
                    if (result.TestStatus != TestStatus.Passed)
                    {
                        sb.AppendLine($"{result.TestStatus}: {result.Test.FullName}");
                        if (!string.IsNullOrEmpty(result.Message))
                            sb.AppendLine($"    {result.Message.Replace("\n", "\n    ")}");
                    }
                    return;
                }

                foreach (ITestResultAdaptor child in result.Children)
                    AppendFailures(child, sb);
            }
        }
    }
}
