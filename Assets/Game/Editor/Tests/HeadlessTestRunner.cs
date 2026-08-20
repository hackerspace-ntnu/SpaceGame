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
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class HeadlessTestRunner
    {
        /// <summary>Where results land. Temp/ is not imported by the AssetDatabase, so writing here
        /// does not kick off a domain reload in the middle of the run.</summary>
        public const string ResultPath = "Temp/headless_tests.txt";

        // Static so neither the api nor the listener is collected while the run is in flight.
        private static TestRunnerApi api;
        private static ResultListener listener;

        /// <summary>Run every EditMode test whose fixture name matches, or all of them if null.</summary>
        public static void RunEditMode(string groupName = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HeadlessTestRunner] Refusing to start an EditMode run in play mode.");
                return;
            }

            if (File.Exists(ResultPath)) File.Delete(ResultPath);

            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(groupName))
                filter.groupNames = new[] { groupName };

            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            listener = new ResultListener();
            api.RegisterCallbacks(listener);
            api.Execute(new ExecutionSettings(filter));
        }

        /// <summary>Set while a deferred run is pending. Survives the domain reload — see below.</summary>
        private const string PendingKey = "SpaceGame.HeadlessTests.Pending";

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
            Schedule();
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload() => Schedule();

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

        /// <summary>Waits for the editor to go idle, then starts the run once and unhooks itself.</summary>
        private static void Pump()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            EditorApplication.update -= Pump;
            StartIfPending();
        }

        private static void StartIfPending()
        {
            string pending = SessionState.GetString(PendingKey, null);
            if (pending == null) return;

            // Entering play mode is itself a domain reload, so a pending request left over from an
            // earlier session gets re-armed by ResumeAfterReload and lands here mid-Play. The test
            // framework's first step is SaveCurrentModifiedScenesIfUserWantsTo, which throws in play
            // mode and then cascades through the teardown tasks. Drop the request instead: an
            // EditMode run started from inside a play session was never what the caller asked for.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                SessionState.EraseString(PendingKey);
                Debug.LogWarning("[HeadlessTestRunner] Discarded a pending EditMode run because the " +
                                 "editor entered play mode. Re-request it from the edit-mode editor.");
                return;
            }

            // Cleared before starting, not after: a run that itself triggers a reload must not come
            // back round and start a second one.
            SessionState.EraseString(PendingKey);
            RunEditMode(string.IsNullOrEmpty(pending) ? null : pending);
        }

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

                if (api != null && listener != null) api.UnregisterCallbacks(listener);
                api = null;
                listener = null;
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
