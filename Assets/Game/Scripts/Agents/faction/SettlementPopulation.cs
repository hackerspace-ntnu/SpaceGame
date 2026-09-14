// A settlement that keeps its people coming.
//
// Put on the root of a settlement with the faction that owns it. Every spawnInterval it counts
// the owner's own inside `countRadius` and, if the town is below `maxPopulation`, spawns a few
// more from `inhabitants` onto the NavMesh somewhere in the ring between innerRadius and
// outerRadius -- never within minPlayerDistance of a player, because a robot materialising in
// front of you is a bug however it is dressed.
//
// Two rules of pace, both deliberate (GDC-L1-LEVEL-0003: a peak needs release after it):
//
//   * It is a CAP, not a stream. The town refills what it lost, at an interval; it does not pour
//     bodies at the player for as long as they stand there.
//   * It HOLDS while the alarm is raised (holdWhileAlarmRaised). Reinforcements arriving
//     mid-fight would make the fight unwinnable, so the clock restarts once the intruders are
//     gone and the town repopulates in the quiet that follows -- which is also when nobody sees
//     where they came from.
//
// Deciding and spawning run only where Network.Decides is true (the server, and offline -- not
// Simulates, which says yes on a client for anything without a NetworkObject and had this
// spawner refused by World.Spawn 29 times in one session); the spawn goes through
// GameServices.World.Spawn, which puts the new inhabitant on the wire and opts it into the world
// save under its own prefabId. So the PEOPLE persist and replicate by themselves; this component
// persists nothing on purpose -- the only state it owns is the clock to the next wave, and a
// reload re-arming that clock costs one interval, not a population.
//
// A wave is a BAND, not a scatter: its members share a FormationModule id and the first one out
// leads, so the reinforcements walk the town together the way the generated groups do.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public class SettlementPopulation : MonoBehaviour
    {
        [System.Serializable]
        public struct Inhabitant
        {
            [Tooltip("Registered network prefab with a stamped prefabId, or it exists on the host " +
                     "only and vanishes on reload.")]
            public GameObject prefab;
            [Tooltip("Relative chance per spawn. 0 never spawns.")]
            [Min(0)] public int weight;
        }

        [Header("Territory")]
        [Tooltip("Whose settlement this is. Entities Allied to it are its population.")]
        [SerializeField] private FactionDefinition owner;
        [SerializeField] private FactionRelationshipTable relationshipTable;
        [Tooltip("Metres from this object inside which the owner's people count toward the cap.")]
        [SerializeField] private float countRadius = 170f;

        [Header("Population")]
        [SerializeField] private Inhabitant[] inhabitants;
        [Tooltip("The town stops spawning once this many of its own are alive inside countRadius. " +
                 "A mounted pair counts as two.")]
        [SerializeField] private int maxPopulation = 12;
        [Tooltip("Seconds between waves.")]
        [SerializeField] private float spawnInterval = 60f;
        [Tooltip("At most this many per wave, and never more than the shortfall.")]
        [SerializeField] private int spawnsPerWave = 2;
        [Tooltip("No wave while the settlement's alarm is raised; the clock restarts when it lowers.")]
        [SerializeField] private bool holdWhileAlarmRaised = true;

        [Header("Placement")]
        [Tooltip("Spawns land in the ring between these two radii, on the NavMesh.")]
        [SerializeField] private float innerRadius = 45f;
        [SerializeField] private float outerRadius = 120f;
        [Tooltip("Never this close to a player.")]
        [SerializeField] private float minPlayerDistance = 45f;
        [SerializeField] private float navSampleDistance = 8f;
        [SerializeField] private int placementAttempts = 12;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        public int Population { get; private set; }

        private SettlementPopulationLogic.State state;
        private SettlementAlarm alarm;
        private int waveCount;
        private readonly List<EntityFaction> people = new List<EntityFaction>(32);

        /// <summary>Everything the builder decides, in one call.</summary>
        public void Configure(FactionDefinition owner, FactionRelationshipTable table, Inhabitant[] inhabitants,
                              int maxPopulation, float spawnInterval, float innerRadius, float outerRadius,
                              float countRadius)
        {
            this.owner = owner;
            relationshipTable = table;
            this.inhabitants = inhabitants;
            this.maxPopulation = maxPopulation;
            this.spawnInterval = spawnInterval;
            this.innerRadius = innerRadius;
            this.outerRadius = outerRadius;
            this.countRadius = countRadius;
        }

        private void Awake() => alarm = GetComponent<SettlementAlarm>();

        private void OnEnable() => state = default;

        private void Update()
        {
            if (!Network.Decides)
                return;

            EntityTargetRegistry.Query(owner, relationshipTable, FactionRelationship.Allied,
                                       transform.position, countRadius, people);
            Population = people.Count;

            bool hold = holdWhileAlarmRaised && alarm != null && alarm.IsRaised;
            int wanted = SettlementPopulationLogic.Step(ref state, Population, maxPopulation, spawnsPerWave,
                                                        hold, Time.time, spawnInterval);
            if (wanted <= 0)
                return;

            waveCount++;
            string band = $"{name}/wave{waveCount}";
            bool leaderPlaced = false;
            for (int i = 0; i < wanted; i++)
            {
                GameObject spawned = SpawnOne();
                if (spawned == null) continue;
                if (spawned.TryGetComponent(out FormationModule formation))
                {
                    formation.SetFormation(band, leader: !leaderPlaced);
                    leaderPlaced = true;
                }
            }
        }

        private GameObject SpawnOne()
        {
            GameObject prefab = Pick();
            if (prefab == null)
                return null;
            if (!TryFindSpawnPoint(out Vector3 position))
                return null;

            Quaternion facing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject spawned = GameServices.World.Spawn(prefab, position, facing);
            if (spawned != null)
                Population++;
            return spawned;
        }

        private GameObject Pick()
        {
            if (inhabitants == null || inhabitants.Length == 0)
                return null;
            int index = SettlementPopulationLogic.Pick(inhabitants, Random.value);
            return index >= 0 ? inhabitants[index].prefab : null;
        }

        // A random point in the ring that is on the NavMesh and out of everyone's face. Gives up
        // after placementAttempts rather than looping: a town with players standing all over it
        // simply spawns nobody this wave.
        private bool TryFindSpawnPoint(out Vector3 position)
        {
            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(innerRadius, outerRadius);
                Vector3 candidate = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleDistance, NavMesh.AllAreas))
                    continue;
                if (NearAPlayer(hit.position))
                    continue;

                position = hit.position;
                return true;
            }

            position = default;
            return false;
        }

        private bool NearAPlayer(Vector3 point)
        {
            float minSqr = minPlayerDistance * minPlayerDistance;
            IReadOnlyList<PlayerIdentity> players = PlayerIdentity.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerIdentity player = players[i];
                if (player == null) continue;
                if ((player.transform.position - point).sqrMagnitude < minSqr)
                    return true;
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.5f);
            DrawRing(innerRadius);
            DrawRing(outerRadius);
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.2f);
            DrawRing(countRadius);
        }

        private void DrawRing(float radius)
        {
            const int segments = 48;
            Vector3 prev = transform.position + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }

    /// <summary>
    /// The population clock, pure so the test suite can run it: no scene, no registry, no clock.
    /// </summary>
    public static class SettlementPopulationLogic
    {
        public struct State
        {
            public bool Armed;
            public float NextWaveAt;
        }

        /// <summary>
        /// How many to spawn on this tick. The first tick only arms the clock -- a town does not
        /// spawn the frame its chunk loads -- and a held tick (alarm raised) pushes the next wave a
        /// whole interval out, so the quiet after a fight is a full interval long.
        /// </summary>
        public static int Step(ref State state, int alive, int maxPopulation, int perWave, bool hold,
                               float now, float interval)
        {
            if (!state.Armed)
            {
                state.Armed = true;
                state.NextWaveAt = now + interval;
                return 0;
            }

            if (hold)
            {
                state.NextWaveAt = now + interval;
                return 0;
            }

            if (now < state.NextWaveAt)
                return 0;

            state.NextWaveAt = now + interval;
            return Mathf.Clamp(maxPopulation - alive, 0, Mathf.Max(0, perWave));
        }

        /// <summary>
        /// Weighted choice: the index whose weight band <paramref name="roll01"/> lands in, or -1
        /// when nothing has weight. A null prefab keeps its band, so the mix stays as authored.
        /// </summary>
        public static int Pick(IReadOnlyList<SettlementPopulation.Inhabitant> inhabitants, float roll01)
        {
            int total = 0;
            for (int i = 0; i < inhabitants.Count; i++)
                total += Mathf.Max(0, inhabitants[i].weight);
            if (total <= 0)
                return -1;

            float target = Mathf.Clamp01(roll01) * total;
            float edge = 0f;
            int lastWeighted = -1;
            for (int i = 0; i < inhabitants.Count; i++)
            {
                int weight = Mathf.Max(0, inhabitants[i].weight);
                if (weight == 0) continue;
                lastWeighted = i;
                edge += weight;
                if (target < edge)
                    return i;
            }
            // A roll of exactly 1 lands on the top edge; it belongs to the last band that has one.
            return lastWeighted;
        }
    }
}
