// Every global number settlement expeditions are tuned by, in one asset (Resources/Expeditions/ExpeditionTuning).
// Per-culture and per-goal numbers live on the profile and goal assets instead. Clock times are hours of the
// day (0–24), durations are GAME minutes unless the name says otherwise, distances are metres. The defaults
// below are the shipped tuning, so a fresh CreateInstance plays correctly.
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Expeditions
{
    [CreateAssetMenu(menuName = "SpaceGame/Expeditions/Tuning", fileName = "ExpeditionTuning")]
    public sealed class ExpeditionTuning : ScriptableObject
    {
        private const string ResourcePath = "Expeditions/ExpeditionTuning";

        [Header("Road (hours of the day)")]
        [Tooltip("Hour of the day (0–24) a band out on the road starts walking.")]
        [Range(0f, 24f)] public float travelDawn = 6f;

        [Tooltip("Hour of the day (0–24) a band out on the road stops walking for the night.")]
        [Range(0f, 24f)] public float travelDusk = 19f;

        [Header("Departure")]
        [Tooltip("Hour of the day (0–24) the band musters and walks out.")]
        [Range(0f, 24f)] public float departHour = 7.5f;

        [Tooltip("GAME minutes the band stands mustered at the gate before it walks out.")]
        [Min(0f)] public float musterMinutes = 30f;

        [Tooltip("Metres back inside the outer end of the settlement's farthest-reaching street that a muster spot placed by " +
                 "rule stands (Generate, Tools/SpaceGame/Expeditions/Place Muster Spots). A spot on a prefab ignores it.")]
        [Min(0f)] public float musterInset = 5f;

        [Tooltip("Metres between neighbours in the row a band stands in at the muster spot, across the spot's facing.")]
        [Min(0f)] public float musterSpacing = 1.2f;

        [Tooltip("Metres from the muster spot past which an unobserved band is handed off to stand-ins.")]
        [Min(0f)] public float departRadius = 150f;

        [Tooltip("Metres from the muster spot at which a band is handed off even while a player watches.")]
        [Min(0f)] public float maxHandoffDistance = 400f;

        [Tooltip("Metres: a player within this distance with a line of sight to the walking band counts as watching the hand-off. " +
                 "Capped at the NpcWorldSim's despawn radius, within which the hand-off spawns stand-ins: a farther watcher counts " +
                 "as nobody (ExpeditionDirector.HandOffObserveRadius).")]
        [Min(0f)] public float handoffObserveRadius = 400f;

        [Tooltip("GAME minutes a walk-out or a walk-in may last. Past it a walking-out band is handed off, and a walking-in " +
                 "band is home, wherever it stands, so a member stuck on the way never holds the band.")]
        [Min(1f)] public float walkLimitMinutes = 240f;

        [Header("Home")]
        [Tooltip("Whole game days a member stays home after its band returns before it can be picked again.")]
        [Min(0)] public int restDays = 2;

        [Tooltip("Share (0–1) of a settlement's residents that must stay home; a band that would take more is not raised.")]
        [Range(0f, 1f)] public float minHomeShare = 0.8f;

        [Tooltip("Metres: a player within this distance with a line of sight to a band or a resident counts as watching it, " +
                 "so nothing pops in or out in front of them.")]
        [Min(0f)] public float observeRadius = 150f;

        [Header("Rotation")]
        [Tooltip("Factor (0–1) on the weight of the goal the settlement's last band ran, so the draw varies.")]
        [Range(0f, 1f)] public float varietyPenalty = 0.5f;

        [Tooltip("REAL seconds between the director's decision passes.")]
        [Min(0.05f)] public float decisionInterval = 0.5f;

        [Header("Search")]
        [Tooltip("Metres from the search centre a waypoint lies, rolled per waypoint between x and y.")]
        public Vector2 searchRing = new Vector2(150f, 300f);

        [Header("Travel")]
        [Tooltip("Metres from where a Travel stage starts that its destination lies, rolled per stage between x and y. " +
                 "A known site of one of the kinds below within that band is preferred over open ground.")]
        public Vector2 travelReach = new Vector2(800f, 1600f);

        [Tooltip("Kinds of site a Travel stage prefers as its destination, when one lies within travelReach.")]
        public SiteKind[] travelSiteKinds = { SiteKind.Ruin, SiteKind.ScrapField, SiteKind.Landmark };

        [Tooltip("Metres from its destination (a Travel's end, a Search waypoint, the hand-off point) at which a band has arrived.")]
        [Min(1f)] public float arriveRadius = 15f;

        [Tooltip("Metres around a destination searched for walkable ground (the world NavMesh); a waypoint with none in reach " +
                 "is skipped for the next one.")]
        [Min(1f)] public float destinationSampleDistance = 25f;

        [Tooltip("Metres: the longest leg a band with stand-ins in the world is sent at once, on the straight line toward its " +
                 "destination; the next leg is given when it arrives. A NavMesh path a kilometre long is never found for an " +
                 "agent (see Expeditions.md, Gotchas). A folded band walks the whole way in one go.")]
        [Min(1f)] public float spawnedLegLength = 120f;

        [Tooltip("Metres from its walk point at which a spawned band's leader is told to stop. Below arriveRadius: the leader " +
                 "stops at this distance plus its motor's margin, measured along its path, and the band has arrived only within " +
                 "arriveRadius.")]
        [Min(0.5f)] public float leaderStopRadius = 5f;

        private static ExpeditionTuning instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>The project's tuning asset; the code defaults when the asset is missing. Never null.</summary>
        public static ExpeditionTuning Instance
        {
            get
            {
                if (instance) return instance;

                instance = Resources.Load<ExpeditionTuning>(ResourcePath);
                if (instance) return instance;

                Debug.LogWarning($"ExpeditionTuning: no asset at Resources/{ResourcePath} — playing on code defaults.");
                instance = CreateInstance<ExpeditionTuning>();
                return instance;
            }
        }
    }
}
