// The editor half of the agent benchmark: Tools ▸ Agents ▸ Run Agent Benchmark, or one bridge call
//
//     SpaceGame.EditorTools.AgentBenchmark.Run();
//
// then poll Temp/agent_benchmark.txt for a last line of DONE. The play-mode half, and what is
// measured, is AgentBenchmarkRun. Getting into and out of play mode is PlayModeHarness's; the run
// plays in an empty scene (an open world scene would be loaded by the Bootstrapper and measured).
using System.Collections.Generic;
using SpaceGame.Agents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class AgentBenchmark
    {
        private const string PlayerFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/HumansFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        private static readonly PlayModeHarness Harness =
            new(nameof(AgentBenchmark), StageEmptyScene, StartRun, Refuse);

        [MenuItem("Tools/Agents/Run Agent Benchmark")]
        private static void RunFromMenu() => Run();

        /// <summary>The default load: 200 agents over the six-prefab mix, 4 fake players.</summary>
        public static void Run() => Run(new AgentBenchmarkSettings());

        /// <summary>
        /// Arms a run and returns at once; play mode starts on a later editor tick. The outcome —
        /// numbers or the reason there are none — is always in <see cref="AgentBenchmarkSettings.reportPath"/>.
        /// </summary>
        public static void Run(AgentBenchmarkSettings settings) => Harness.Request(JsonUtility.ToJson(settings));

        [InitializeOnLoadMethod]
        private static void Install() => Harness.Install();

        private static void StageEmptyScene(string json) => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        private static void Refuse(string json, string reason) =>
            AgentBenchmarkRun.WriteFailure(JsonUtility.FromJson<AgentBenchmarkSettings>(json).reportPath, "not started: " + reason);

        private static void StartRun(string json)
        {
            AgentBenchmarkSettings settings = JsonUtility.FromJson<AgentBenchmarkSettings>(json);

            var missing = new List<string>();
            var prefabs = new List<GameObject>();
            foreach (string path in settings.prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) prefabs.Add(prefab);
                else missing.Add(path);
            }

            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(PlayerFactionPath);
            var relationships = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);
            if (faction == null) missing.Add(PlayerFactionPath);
            if (relationships == null) missing.Add(RelationshipsPath);

            if (missing.Count > 0)
            {
                AgentBenchmarkRun.WriteFailure(settings.reportPath, "missing assets: " + string.Join(", ", missing));
                PlayModeHarness.Leave();
                return;
            }

            AgentBenchmarkRun.Begin(settings, prefabs.ToArray(), faction, relationships, PlayModeHarness.Leave);
        }
    }
}
