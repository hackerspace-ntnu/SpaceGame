// Removes orphaned mount cameras from the editor before they can render a play session.
//
// MountRuntimeCamera.cs explains how they come to exist: an EditMode test that mounts a rider
// spawns the mount's third-person camera, unparented and DontSaveInEditor, and nothing in edit mode
// takes it down again -- Unity delivers no OnDestroy to a plain MonoBehaviour there, so the
// fixture destroying its mount never releases the camera, and closing the scene detaches the
// object rather than destroying it. It is left enabled at the same depth as the player's camera,
// belonging to no scene that any load could clear, and the next play session renders through it.
//
// Three moments cover every way in. When a test run finishes, because that is what makes them.
// After a domain reload, because a run aborted by one never even reached TearDown and the objects
// have just survived the reload. And on ExitingEditMode, because whatever happened since, play
// mode must start with none of them alive -- this is the one that stands between the leak and
// the player's screen.
using SpaceGame.Agents;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    [InitializeOnLoad]
    internal static class MountRuntimeCameraSweep
    {
        // Held so the registration is not collected out from under the test runner.
        private static readonly TestRunnerApi testRunner;

        static MountRuntimeCameraSweep()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
            testRunner.RegisterCallbacks(new AfterTestRun());

            // Deferred: inside the static constructor the editor is still finishing the reload,
            // and destroying objects there is not reliable. The next editor tick is.
            EditorApplication.delayCall += Sweep;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Sweep();
        }

        private static void Sweep()
        {
            int swept = MountRuntimeCamera.SweepOrphans();
            if (swept == 0) return;

            Debug.Log($"[MountRuntimeCamera] Destroyed {swept} orphaned mount camera(s) left behind " +
                      "by an EditMode test run. Left alive, the next play session would have " +
                      "rendered through one of them instead of the player's camera.");
        }

        private sealed class AfterTestRun : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            // Only once the whole run is over: a fixture's own camera is a legitimate object while
            // its test is still running, and the sweep would take it from under an assertion.
            public void RunFinished(ITestResultAdaptor result) => Sweep();
        }
    }
}
