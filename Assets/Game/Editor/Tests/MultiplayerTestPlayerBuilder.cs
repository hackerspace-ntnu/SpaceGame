// Builds the small player that MultiplayerAutotest drives, and prints how to run it.
//
// The client half of the netcode can only be tested with a real second process — see the header of
// MultiplayerAutotest for why one process cannot stand in. This builds the minimum needed for that:
// Bootstrap (which owns the NetworkManager and the registries), MainMenu, and persistentScene,
// which carries the networked agents under test. Leaving out the 48 chunk scenes is most of the
// build time, and none of them matter to the test.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class MultiplayerTestPlayerBuilder
    {
        private static readonly string[] Scenes =
        {
            "Assets/Game/Scenes/Core/Bootstrap.unity",
            "Assets/Game/Scenes/Core/MainMenu.unity",
            // Lowercase "world" — that is the casing on disk and in the index. Netcode hashes scene
            // PATHS case-sensitively, so a drifted capital here is a join failure waiting to happen.
            "Assets/Game/Scenes/world/persistentScene.unity",
        };

        /// <summary>Where the player lands. Outside Assets/ so it is never imported.</summary>
        public static string OutputPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Build", "MPTest", "SpaceGameMP.app");

        [MenuItem("Tools/Tests/Build Multiplayer Test Player")]
        public static void Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            EditorUtility.ClearProgressBar();

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[MPTest] Build {report.summary.result} with {report.summary.totalErrors} error(s). " +
                               "Player builds compile scripts separately from the editor, so this can fail on code " +
                               "the editor was happy with — check the errors above before blaming the netcode.");
                return;
            }

            Debug.Log($"[MPTest] Built in {(int)report.summary.totalTime.TotalSeconds}s.\n\n" +
                      "Run the two peers (separate terminals, or the second with a trailing &):\n\n" +
                      RunInstructions());
        }

        [MenuItem("Tools/Tests/Print Multiplayer Test Commands")]
        private static void PrintCommands() => Debug.Log("[MPTest]\n" + RunInstructions());

        private static string RunInstructions()
        {
            string exe = Path.Combine(OutputPath, "Contents", "MacOS",
                                      Path.GetFileNameWithoutExtension(OutputPath));

            return
                $"  \"{exe}\" -batchmode -nographics -sgmode host   -logFile /tmp/mp_host.log &\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode client -logFile /tmp/mp_client.log &\n\n" +
                "Then read the verdict off both logs:\n\n" +
                "  grep '\\[MPTEST\\]' /tmp/mp_host.log /tmp/mp_client.log\n\n" +
                "What the result has to show, and why each line is the interesting one:\n" +
                "  HOST_CLIENTS=2                       a second process really connected\n" +
                "  CLIENT_SPAWNED > 0                   the world replicated to a machine that did not build it\n" +
                "  CLIENT_SUPPRESSED == CLIENT_AUTHORITIES   every agent stopped simulating itself on the client\n" +
                "  CLIENT_DRIVERS_DISABLED > 0          ...and it was the motors/brains that were switched off\n" +
                "  CLIENT_HEALTH_SEEN == HOST_HEALTH_AFTER   damage the SERVER applied arrived here\n" +
                "  HOST_RELAY_FROM_CLIENT=1             a client-to-server relay message crossed the wire\n" +
                "  CLIENT_PLAYER_OBJECT=True            the joining player got a body\n\n" +
                "To PLAY this build against the editor instead of running the autotest, launch it with\n" +
                "its own Unity Services profile — a player and the editor share one PlayerPrefs file, so\n" +
                "they otherwise sign in as the same anonymous PlayerId and the lobby refuses the second\n" +
                "one as already a member (see SessionLauncher.ProfileArg):\n\n" +
                $"  open \"{OutputPath}\" --args -sgprofile client";
        }
    }
}
