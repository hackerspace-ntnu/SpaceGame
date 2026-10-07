// What the agent benchmark spawns and how long it measures. One object, so the editor menu, a
// bridge call and the play-mode harness all read the same numbers, and a run with a different load
// is a different settings object rather than an edited constant.
using System;
using UnityEngine;

namespace SpaceGame.Agents
{
    [Serializable]
    public sealed class AgentBenchmarkSettings
    {
        [Tooltip("Agents spawned, cycling through prefabPaths in order.")]
        public int agentCount = 200;

        [Tooltip("Agent prefabs, as asset paths. Only the editor resolves them; the harness gets the loaded prefabs.")]
        public string[] prefabPaths =
        {
            "Assets/Game/Prefabs/agents/Characters/Raxy/Raxy_poor.prefab",
            "Assets/Game/Prefabs/agents/Characters/Nomad_Tan.prefab",
            "Assets/Game/Prefabs/agents/Robots/Clanker.prefab",
            "Assets/Game/Prefabs/agents/creatures/Vrescal.prefab",
            "Assets/Game/Prefabs/agents/creatures/RobotHorse.prefab",
            "Assets/Game/Prefabs/agents/creatures/Appa.prefab",
        };

        [Tooltip("Scripted stand-ins for players: targetable Humans bodies circling the arena.")]
        public int fakePlayerCount = 4;

        [Tooltip("Frames measured with the arena and fake players but no agents: the floor the agent cost sits on.")]
        public int baselineFrames = 120;

        [Tooltip("Frames run after spawning before measuring, so Awake/Start spikes and first-path costs settle.")]
        public int warmupFrames = 300;

        [Tooltip("Frames measured with every agent live.")]
        public int recordFrames = 600;

        [Tooltip("Time.captureDeltaTime = 1 / this. An unfocused editor runs at a few fps; a fixed step keeps the simulation honest.")]
        public float simulatedFrameRate = 60f;

        [Tooltip("Side of the square arena, metres.")]
        public float arenaSize = 160f;

        [Tooltip("Agents spawn at least this far inside the arena edge, metres.")]
        public float spawnMargin = 8f;

        [Tooltip("How far a spawn point may be snapped onto the NavMesh, metres.")]
        public float spawnSnapDistance = 4f;

        [Tooltip("Height of the NavMesh build bounds around the arena surface, metres.")]
        public float navMeshBoundsHeight = 20f;

        [Tooltip("Fake player walking speed, m/s.")]
        public float fakePlayerSpeed = 4f;

        [Tooltip("Innermost fake player's orbit, as a fraction of half the arena.")]
        [Range(0.05f, 1f)] public float fakePlayerOrbitMin = 0.2f;

        [Tooltip("Outermost fake player's orbit, as a fraction of half the arena.")]
        [Range(0.05f, 1f)] public float fakePlayerOrbitMax = 0.85f;

        [Tooltip("Overhead camera height, metres. Some presentation code only runs for visible bodies.")]
        public float cameraHeight = 140f;

        [Tooltip("Frames to wait for the game's Bootstrap scene chain to finish loading before the run gives up.")]
        public int settleTimeoutFrames = 1800;

        [Tooltip("Seeds spawn positions and headings, so two runs place the same agents in the same spots.")]
        public int seed = 20261002;

        [Tooltip("Where the report is written, relative to the project root. Its last line is DONE.")]
        public string reportPath = "Temp/agent_benchmark.txt";
    }
}
