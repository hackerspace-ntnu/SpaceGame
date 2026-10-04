// Builds the small player that MultiplayerAutotest drives, and prints how to run it.
//
// The client half of the netcode can only be tested with a real second process — see the header of
// MultiplayerAutotest for why one process cannot stand in. This builds the minimum needed for that:
// Bootstrap (which owns the NetworkManager and the registries), MainMenu, and persistentScene,
// which carries the networked agents under test, plus the dozen chunk scenes the settlement runs
// need: the nomad settlement's chunk with its neighbours, and the chunks around the spawn. The
// other 36 are most of the build time and none of them matter to the test.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class MultiplayerTestPlayerBuilder
    {
        private static readonly string[] CoreScenes =
        {
            "Assets/Game/Scenes/Core/Bootstrap.unity",
            "Assets/Game/Scenes/Core/MainMenu.unity",
            // Lowercase "world" — that is the casing on disk and in the index. Netcode hashes scene
            // PATHS case-sensitively, so a drifted capital here is a join failure waiting to happen.
            "Assets/Game/Scenes/world/persistentScene.unity",
        };

        // Chunk (column, row) around the settlement at Chunk_6_3 and around the spawn in the north-east corner,
        // Chunk_7_5, one ring wider than the 3x3 the streamer loads about a player there: an expedition band hands
        // off ~400 m out along the settlement's road and walks on, so a player following it reaches the next column
        // (column 4 since the 2026-10-04 regenerate). A chunk the streamer asks for that is not in the build is an
        // error, and a player put there has no ground.
        private static readonly Vector2Int[] ChunkGridCells =
        {
            new(4, 1), new(5, 1), new(6, 1), new(7, 1),
            new(4, 2), new(5, 2), new(6, 2), new(7, 2),
            new(4, 3), new(5, 3), new(6, 3), new(7, 3),
            new(4, 4), new(5, 4), new(6, 4), new(7, 4),
            new(4, 5), new(5, 5), new(6, 5), new(7, 5),
        };

        private static string[] Scenes
        {
            get
            {
                var scenes = new List<string>(CoreScenes);
                foreach (Vector2Int cell in ChunkGridCells)
                    scenes.Add($"Assets/Game/Scenes/world/Chunks/Chunk_{cell.x}_{cell.y}.unity");
                return scenes.ToArray();
            }
        }

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

            // Most of the 48 chunk scenes are not in this player, so the world NavMesh bake check this build
            // would otherwise be refused over is a bake nothing here can use. See the flag.
            BuildReport report;
            World.NavMeshTools.WorldNavMeshBuildCheck.BuildingWithoutChunks = true;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                World.NavMeshTools.WorldNavMeshBuildCheck.BuildingWithoutChunks = false;
            }

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
            // The executable is named for the product, not for the .app folder.
            string exe = Path.Combine(OutputPath, "Contents", "MacOS", PlayerSettings.productName);

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
                "  CLIENT_PLAYER_OBJECT=True            the joining player got a body\n" +
                "  CLIENT_NETS_SEEN == HOST_NETS        the net the host fired was drawn here too\n" +
                "  CLIENT_NET_CAPTIVES > 0              ...and this machine was told what it caught\n\n" +
                "Then the save/load half, which runs alone because none of it is about a peer:\n\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode persist -logFile /tmp/mp_persist.log\n\n" +
                "  PERSIST_CHARGES_BEFORE_SAVE=1        two of three charges spent\n" +
                "  PERSIST_CHARGES_AFTER_LOAD=1         ...and the gun came back spent, not full\n" +
                "  PERSIST_QUARRY_BOUND_AFTER_LOAD=False   nobody reloaded still netted\n" +
                "  PERSIST_QUARRY_TRAVELLED > 0         ...and the creature can still move\n\n" +
                "The settlement, across two machines and across a reload (the build holds its chunk):\n\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode settlement-host   -logFile /tmp/mp_settlement_host.log &\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode settlement-client -logFile /tmp/mp_settlement_client.log &\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode settlement-persist -logFile /tmp/mp_settlement_persist.log\n\n" +
                "  CLIENT_RESIDENTS == HOST_RESIDENTS      the client sees every resident the host does\n" +
                "  CLIENT_STOCK == HOST_STOCK              ...and every penned animal\n" +
                "  CLIENT_GATE_OPEN_SEEN=True              ...and the gate the host opened\n" +
                "  PERSIST_AFTER_LOAD_* == PERSIST_BEFORE_SAVE_*   residents, stock and the open gate survive a reload\n\n" +
                "The settlement's expedition band, across two machines and across a reload (about 10 minutes each):\n\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode expedition-host   -logFile /tmp/mp_expedition_host.log &\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode expedition-client -logFile /tmp/mp_expedition_client.log &\n" +
                $"  \"{exe}\" -batchmode -nographics -sgmode expedition-persist -logFile /tmp/mp_expedition_persist.log\n\n" +
                "  HOST_EXP_PASS=True / CLIENT_EXP_PASS=True / PERSIST_EXP_PASS=True   every check held; else *_FAILED names each\n" +
                "  CLIENT_EXP_STAND_INS == HOST_EXP_STAND_INS             the client sees every stand-in the host spawned\n" +
                "  CLIENT_EXP_STAND_IN_LINE == HOST_EXP_STAND_IN_LINE     ...with the same names and the same weapons in hand\n\n" +
                "To PLAY this build against the editor instead of running the autotest, launch it with\n" +
                "its own Unity Services profile — a player and the editor share one PlayerPrefs file, so\n" +
                "they otherwise sign in as the same anonymous PlayerId and the lobby refuses the second\n" +
                "one as already a member (see SessionProfile.Arg):\n\n" +
                $"  open \"{OutputPath}\" --args -sgprofile client";
        }
    }
}
