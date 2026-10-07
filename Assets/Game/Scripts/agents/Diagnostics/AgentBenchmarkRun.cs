// The play-mode half of the agent benchmark (the editor half is AgentBenchmark under Editor/Agents,
// which enters play mode and starts this). It measures what N agents cost the main thread, so the
// restructure has a number to hold itself to rather than a feeling.
//
// What it measures, and why only that: the Scripts profiler markers (BehaviourUpdate,
// LateBehaviourUpdate, FixedBehaviourUpdate) and the GC counters, per frame, through
// ProfilerRecorder. Frame time is worthless here — an unfocused editor runs at a few fps whatever
// the game does — but a marker's duration is the work done inside it, focused or not.
//
// The run, in order:
//   1. wait for the game's Bootstrap → main menu chain to finish (it fires on every play start);
//   2. move into a fresh empty scene and unload the rest, so the menu's objects are not measured;
//   3. build a flat arena with a NavMesh for every agent type, an overhead camera, fake players;
//   4. measure baselineFrames with no agents — the floor everything else in the editor costs;
//   5. spawn agentCount agents, run warmupFrames, measure recordFrames;
//   6. write the report (last line DONE) and tell the editor to leave play mode.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SpaceGame.Agents
{
    public sealed class AgentBenchmarkRun : MonoBehaviour
    {
        private const string FaultSite = "AgentBenchmark.Run";
        private const string HarnessName = "AgentBenchmark";
        private const string StageSceneName = HarnessName;
        private const double NanosecondsToMilliseconds = 1e-6;

        // The arena slab's thickness, metres. Its top face is the walking surface at ArenaCentre.
        private const float ArenaThickness = 1f;

        // The stage scene holds nothing else, so the arena can sit on the origin.
        private static readonly Vector3 ArenaCentre = Vector3.zero;

        private AgentBenchmarkSettings settings;
        private GameObject[] prefabs;
        private FactionDefinition playerFaction;
        private FactionRelationshipTable relationships;
        private Action finished;

        private readonly List<AgentController> agents = new();
        private readonly Dictionary<string, int> spawnedPerPrefab = new();
        private int spawnsWithoutController;
        private BenchmarkChannel[] channels;
        private string failure;
        private bool done;

        /// <summary>
        /// Starts a run. <paramref name="onFinished"/> is called once, after the report is written,
        /// whether the run succeeded or failed — it is how the editor knows to leave play mode.
        /// </summary>
        public static AgentBenchmarkRun Begin(AgentBenchmarkSettings settings, GameObject[] prefabs,
                                              FactionDefinition playerFaction,
                                              FactionRelationshipTable relationships, Action onFinished)
        {
            var host = new GameObject(nameof(AgentBenchmarkRun));
            DontDestroyOnLoad(host);

            var run = host.AddComponent<AgentBenchmarkRun>();
            run.settings = settings;
            run.prefabs = prefabs;
            run.playerFaction = playerFaction;
            run.relationships = relationships;
            run.finished = onFinished;

            run.StartCoroutine(Fault.Coroutine(run, FaultSite, run.Execute(),
                                               () => run.Finish("the run threw; see the [Fault] line in the console")));
            return run;
        }

        /// <summary>
        /// Writes a report that says only why there is no measurement. For the editor, when it
        /// refuses to start a run at all — the poller reads the same file either way.
        /// </summary>
        public static void WriteFailure(string reportPath, string reason) =>
            HarnessRun.WriteFailure(HarnessName, reportPath, reason);

        private IEnumerator Execute()
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                Finish("no agent prefabs to spawn");
                yield break;
            }

            yield return HarnessRun.WaitForBootstrap(settings.settleTimeoutFrames, reason => failure = reason);
            if (failure != null)
            {
                Finish(failure);
                yield break;
            }

            yield return HarnessRun.MoveToEmptyStage(StageSceneName);

            BuildArena();
            SpawnFakePlayers();

            Time.captureDeltaTime = 1f / Mathf.Max(settings.simulatedFrameRate, 1f);

            // A field, disposed in Finish: Fault.Coroutine abandons this routine on a throw without
            // running its finally blocks, so a try/finally here would leak the recorders.
            channels = CreateChannels();

            // One frame first: LastValue is the previous frame, and that frame built the arena.
            yield return null;
            for (int i = 0; i < settings.baselineFrames; i++)
            {
                yield return null;
                SampleAll(loadedPhase: false);
            }

            SpawnAgents();

            for (int i = 0; i < settings.warmupFrames; i++)
                yield return null;

            for (int i = 0; i < settings.recordFrames; i++)
            {
                yield return null;
                SampleAll(loadedPhase: true);
            }

            HarnessRun.WriteText(settings.reportPath, BuildReport());
            Finish(null);
        }

        private void BuildArena()
        {
            // Every agent type, not just the humanoid one: Appa and the horse walk on their own.
            HarnessRun.BuildNavMeshSlab("Benchmark Arena", ArenaCentre, settings.arenaSize, ArenaThickness,
                                        settings.navMeshBoundsHeight);

            var camera = new GameObject("Benchmark Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(ArenaCentre + Vector3.up * settings.cameraHeight,
                                                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
            camera.farClipPlane = settings.cameraHeight * 2f;
        }

        private void SpawnFakePlayers()
        {
            float halfSize = settings.arenaSize * 0.5f;
            int count = Mathf.Max(settings.fakePlayerCount, 0);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float radius = halfSize * Mathf.Lerp(settings.fakePlayerOrbitMin, settings.fakePlayerOrbitMax, t);
                float speed = i % 2 == 0 ? settings.fakePlayerSpeed : -settings.fakePlayerSpeed;
                float startAngle = 2f * Mathf.PI * i / count;

                // Built inactive: EntityFaction registers itself for targeting in OnEnable, and must
                // carry its faction by then or it registers as nobody.
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.SetActive(false);
                body.name = "Benchmark Player " + i;

                var rigidbody = body.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;

                body.AddComponent<EntityFaction>().SetFaction(playerFaction, relationships);
                body.AddComponent<HealthComponent>();

                // A primitive capsule is 2 m tall with its pivot in the middle.
                float standHeight = body.GetComponent<CapsuleCollider>().height * 0.5f;
                body.AddComponent<BenchmarkPlayer>().Configure(ArenaCentre, radius, speed, startAngle, standHeight);

                body.SetActive(true);
            }
        }

        private void SpawnAgents()
        {
            var random = new System.Random(settings.seed);
            float reach = Mathf.Max(settings.arenaSize * 0.5f - settings.spawnMargin, 0f);

            for (int i = 0; i < settings.agentCount; i++)
            {
                GameObject prefab = prefabs[i % prefabs.Length];
                if (prefab == null) continue;

                var point = new Vector3(ArenaCentre.x + (float)(random.NextDouble() * 2.0 - 1.0) * reach,
                                        ArenaCentre.y,
                                        ArenaCentre.z + (float)(random.NextDouble() * 2.0 - 1.0) * reach);
                if (NavMesh.SamplePosition(point, out NavMeshHit hit, settings.spawnSnapDistance, NavMesh.AllAreas))
                    point = hit.position;

                Quaternion heading = Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f);
                GameObject agent = Instantiate(prefab, point, heading);

                spawnedPerPrefab.TryGetValue(prefab.name, out int spawned);
                spawnedPerPrefab[prefab.name] = spawned + 1;

                AgentController controller = agent.GetComponentInChildren<AgentController>(true);
                if (controller != null) agents.Add(controller);
                else spawnsWithoutController++;
            }
        }

        private BenchmarkChannel[] CreateChannels() => new[]
        {
            new BenchmarkChannel(ProfilerCategory.Scripts, "BehaviourUpdate", "BehaviourUpdate ms",
                                 NanosecondsToMilliseconds, "0.000", settings.baselineFrames, settings.recordFrames),
            new BenchmarkChannel(ProfilerCategory.Scripts, "LateBehaviourUpdate", "LateBehaviourUpdate ms",
                                 NanosecondsToMilliseconds, "0.000", settings.baselineFrames, settings.recordFrames),
            new BenchmarkChannel(ProfilerCategory.Scripts, "FixedBehaviourUpdate", "FixedBehaviourUpdate ms",
                                 NanosecondsToMilliseconds, "0.000", settings.baselineFrames, settings.recordFrames),
            new BenchmarkChannel(ProfilerCategory.Memory, "GC Allocated In Frame", "GC bytes/frame",
                                 1.0, "0", settings.baselineFrames, settings.recordFrames),
            new BenchmarkChannel(ProfilerCategory.Memory, "GC Allocation In Frame Count", "GC allocs/frame",
                                 1.0, "0", settings.baselineFrames, settings.recordFrames),
        };

        private void SampleAll(bool loadedPhase)
        {
            for (int i = 0; i < channels.Length; i++) channels[i].Sample(loadedPhase);
        }

        private string BuildReport()
        {
            int live = 0;
            foreach (AgentController controller in agents)
                if (controller != null && controller.isActiveAndEnabled) live++;

            StringBuilder report = HarnessRun.BeginReport(HarnessName);
            report.AppendLine("status: OK");

            report.Append("agents: ").Append(agents.Count + spawnsWithoutController).Append(" spawned, ")
                  .Append(live).Append(" with an enabled AgentController at the end, ")
                  .Append(spawnsWithoutController).AppendLine(" without one");
            report.Append("mix:");
            foreach (KeyValuePair<string, int> entry in spawnedPerPrefab)
                report.Append(' ').Append(entry.Key).Append(" x").Append(entry.Value);
            report.AppendLine();

            report.Append("fake players: ").Append(settings.fakePlayerCount)
                  .Append(" | frames: baseline ").Append(settings.baselineFrames)
                  .Append(", warm-up ").Append(settings.warmupFrames)
                  .Append(", recorded ").Append(settings.recordFrames)
                  .Append(" | captureDeltaTime 1/").Append(settings.simulatedFrameRate.ToString("0.##", CultureInfo.InvariantCulture))
                  .AppendLine();

            foreach (BenchmarkChannel channel in channels) channel.AppendReport(report);

            report.AppendLine(HarnessRun.DoneLine);
            return report.ToString();
        }

        // null reason: the report is already written. Otherwise the run failed and the report says why.
        private void Finish(string reason)
        {
            if (done) return;
            done = true;

            Time.captureDeltaTime = 0f;

            if (channels != null)
            {
                foreach (BenchmarkChannel channel in channels) channel.Dispose();
                channels = null;
            }

            if (reason != null)
            {
                Debug.LogError("[AgentBenchmark] " + reason, this);
                WriteFailure(settings.reportPath, reason);
            }

            finished?.Invoke();
        }

        // Play mode ended under the run — the user pressed stop, or something exited it. Leave a
        // report that says so rather than a stale one from the last run.
        private void OnDestroy()
        {
            if (done) return;
            Finish("play mode ended before the run finished");
        }
    }
}
