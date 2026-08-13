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
            if (File.Exists(ResultPath)) File.Delete(ResultPath);

            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(groupName))
                filter.groupNames = new[] { groupName };

            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            listener = new ResultListener();
            api.RegisterCallbacks(listener);
            api.Execute(new ExecutionSettings(filter));
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
