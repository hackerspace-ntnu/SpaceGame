// A group of NPCs as data: where they are, what they are doing, and who is in them.
//
// The whole point is that this can be true of a caravan 3 km away with no GameObjects anywhere. The
// world is 4000x3000 m and the journeys the design calls for are kilometres long — simulating those
// as real NavMeshAgents would mean every group pinning chunks around itself for the entire trip, so
// a dozen caravans keeps most of the world resident and pathfinding for members nobody can see.
//
// So a group is a record that walks, and only becomes agents when somebody is close enough to look
// at it. Which means a group's state has to be expressible without a scene — hence this file.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    /// <summary>One kind of member, and how many of them.</summary>
    [Serializable]
    public class NpcGroupMemberSpec
    {
        [Tooltip("What to spawn. For a mounted caravan this is the ANIMAL prefab, carrying an " +
                 "NpcPassenger — the mount is the agent and the rider goes along for the ride.")]
        public GameObject prefab;

        [Tooltip("Used only when prefab is empty: the prefab is drawn from the template tribe's roster.")]
        public RosterRole role;

        [Tooltip("This member sets the group's route. Exactly one member of a group should lead; " +
                 "if none does, the first spawned takes it.")]
        public bool isLeader;

        [Tooltip("Rides one of the group's carriers (a member with a CrewShift) instead of walking: " +
                 "spawned seated on a free crew post while the group marches, on foot by its " +
                 "carrier's gangway while the group is stopped.")]
        public bool crew;

        [Min(1)]
        public int count = 1;

        [Tooltip("When set, how many of this member a group gets is drawn from these weights instead " +
                 "of being count: seeded by the group's roster seed, so a group that folds, unfolds or " +
                 "reloads comes back with the same number. Empty uses count.")]
        public WeightedCount[] countWeights = Array.Empty<WeightedCount>();

        [Tooltip("Where this member rides in the group's column. Shuffled members are dealt into a " +
                 "seeded order (ColumnDeal) instead of marching in the order they are listed.")]
        public ColumnCard column;

        // Keeps this draw apart from the other rolls seeded by the group's roster seed and a plan index.
        private const int CountSalt = 0x5EED;

        /// <summary>How many of this member the group with <paramref name="rosterSeed"/> gets;
        /// <paramref name="index"/> is the plan index the first of them would take.</summary>
        public int DrawCount(int rosterSeed, int index)
        {
            if (countWeights == null || countWeights.Length == 0) return Mathf.Max(1, count);

            var weights = new float[countWeights.Length];
            for (int i = 0; i < weights.Length; i++) weights[i] = countWeights[i].weight;
            int pick = RosterDraw.PickWeighted(weights, RosterDraw.Roll01(rosterSeed, index + CountSalt));
            return pick < 0 ? Mathf.Max(1, count) : Mathf.Max(1, countWeights[pick].count);
        }
    }

    /// <summary>One possible member count and how likely it is (NpcGroupMemberSpec.countWeights).</summary>
    [Serializable]
    public struct WeightedCount
    {
        [Min(1)] public int count;
        [Min(0f)] public float weight;
    }

    /// <summary>
    /// How a group that flies to its work gets there: a vessel from its tribe's fleet, chosen by how
    /// many ride in it. No vessel set, and the group walks like any other.
    /// </summary>
    [Serializable]
    public class NpcGroupTransport
    {
        [Tooltip("The vessel for a party that fits its seats (its VesselSeats capacity).")]
        public GameObject smallVessel;

        [Tooltip("The vessel for a party too big for the small one.")]
        public GameObject largeVessel;

        [Tooltip("Metres per second while the group is a record still on its way in the vessel. " +
                 "Match the vessel's cruise speed. Once dropped off, the template's travelSpeed applies.")]
        public float travelSpeed = 28f;

        [Tooltip("The registered site (by name, any kind, airborne or not) the vessels fly out from " +
                 "and return to.")]
        public string homeSiteName = WorldSite.SkyCityName;

        [Tooltip("Metres between the dock slots vessels park at around the home site, so two parked " +
                 "hulls never overlap. Keep it at least twice the largest vessel's footprint radius.")]
        [Min(1f)]
        public float dockSpacing = 45f;

        public bool Flies => smallVessel != null || largeVessel != null;

        /// <summary>
        /// The party is off the vessel: nobody is left aboard, the hull was shot down, or it is gone.
        /// A run that ended with riders still seated (no landing site anywhere) is not a delivery.
        /// </summary>
        public static bool IsDelivered(bool vesselExists, bool wrecked, int aboard) =>
            !vesselExists || wrecked || aboard <= 0;

        /// <summary>Back home with its party still aboard, parked long enough: fly the drop again.</summary>
        public static bool ShouldRelaunch(bool runDone, bool wrecked, int aboard, float parkedFor, float retryDelay) =>
            runDone && !wrecked && aboard > 0 && parkedFor >= retryDelay;

        /// <summary>
        /// Where the vessel in <paramref name="slot"/> parks: slot 0 over the home site, then rings of
        /// 6, 12, 18… slots at <paramref name="spacing"/>, 2×, 3×… out. Every slot is at least
        /// <paramref name="spacing"/> from every other.
        /// </summary>
        public static Vector3 DockPoint(Vector3 home, int slot, float spacing)
        {
            if (slot <= 0) return home;

            int ring = 1, first = 1;
            while (slot >= first + 6 * ring)
            {
                first += 6 * ring;
                ring++;
            }

            float angle = (slot - first) * 360f / (6 * ring);
            return home + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (ring * spacing);
        }

        /// <summary>The lowest dock slot nobody holds.</summary>
        public static int FirstFreeDock(ICollection<int> taken)
        {
            int slot = 0;
            while (taken.Contains(slot)) slot++;
            return slot;
        }

        /// <summary>The small vessel when <paramref name="riders"/> fit its seats, else the large one; whichever exists.</summary>
        public GameObject VesselFor(int riders)
        {
            if (smallVessel == null) return largeVessel;
            if (largeVessel == null) return smallVessel;

            return smallVessel.TryGetComponent(out VesselSeats seats) && riders <= seats.Capacity
                ? smallVessel
                : largeVessel;
        }
    }

    /// <summary>An authored group: what it is made of and what it does with its time.</summary>
    [Serializable]
    public class NpcGroupTemplate
    {
        [Tooltip("Identifies this group. Also becomes the formation id, so two groups from the same " +
                 "template do not try to follow each other's leader across the map.")]
        public string id = "caravan";

        [Tooltip("Shown in debug and in chatter about the group.")]
        public string displayName = "Caravan";

        [Tooltip("The tribe this group belongs to. Members take its faction, and roles draw from its " +
                 "roster. Leave empty for groups that are not a tribe's (Outlaws have no roster but " +
                 "still set this so their members are stamped).")]
        public FactionDefinition tribe;

        [Tooltip("Never seeded at startup; only created at runtime (war parties). Kept in this list so " +
                 "a saved runtime group can always find its template on load.")]
        public bool runtimeOnly;

        public NpcGroupMemberSpec[] members;

        [Tooltip("What this group does. The same NpcTask data the live module uses — a group runs " +
                 "the identical loop whether or not anyone is watching.")]
        public NpcTask[] tasks;

        [Tooltip("Metres per second while travelling as a record. Match it roughly to the members' " +
                 "actual walking speed, or a group visibly teleports forward when it spawns.")]
        public float travelSpeed = 3.5f;

        [Tooltip("Where the group starts. Uses startPosition when set, otherwise a site of this kind.")]
        public SiteKind startNearSite = SiteKind.Camp;

        public bool useStartPosition;
        public Vector3 startPosition;

        [Tooltip("Seconds a new world's group spends at its start before choosing where to go, as if " +
                 "it had just arrived there. 0 sets off on the first tick. Lets a player who lands " +
                 "nearby reach it before it walks out of range.")]
        [Min(0f)] public float initialStaySeconds;

        [Tooltip("This group hunts players. It roams looking for you rather than working sites, and " +
                 "heads for your last known position when it loses you.")]
        public bool bountyHunters;

        [Tooltip("Set a vessel to fly the group in: it spawns aboard, is dropped off near its goal, and " +
                 "walks from there. Empty for a group that walks all the way.")]
        public NpcGroupTransport transport = new NpcGroupTransport();

        [Tooltip("How the group arranges itself on the move.")]
        public FormationShape formation = new FormationShape
        {
            Lanes = 2,
            RowSpacing = 4.5f,
            LaneSpacing = 3f,
            LateralJitter = 0.9f,
            LongitudinalJitter = 1.2f,
            DriftAmplitude = 0.7f,
            DriftRate = 0.08f,
        };
    }

    /// <summary>
    /// The live state of one group. Plain C#, no MonoBehaviour: this is what exists while the group
    /// does not.
    /// </summary>
    public class NpcGroup
    {
        public string Id;
        public string TemplateId;

        /// <summary>Where the group is. Authoritative while unspawned; recomputed from members while spawned.</summary>
        public Vector3 Position;

        public Vector3 GoalPosition;
        public bool HasGoal;
        public float ArriveRadius = 8f;

        public int TaskIndex = -1;
        public float DwellRemaining;
        public string LastSiteId = string.Empty;

        public bool Spawned;

        /// <summary>Bounty hunters only: where the player was last known to be, and how stale that is.</summary>
        public Vector3 Lead;
        public bool HasLead;
        public float LeadAge;

        /// <summary>Which prefabs and weapons this group draws. Same seed, same people, after every refold.</summary>
        public int RosterSeed;

        /// <summary>The profile this war party is hunting. Empty for every group that is not one.</summary>
        public string QuarryProfileId = string.Empty;

        /// <summary>War-party escalation tier: which row of the roster's warPartyTiers it spawns.</summary>
        public int Tier;

        public bool IsWarParty => !string.IsNullOrEmpty(QuarryProfileId);

        public const string OwnerWar = "war";
        public const string OwnerExpedition = "expedition";

        /// <summary>
        /// The director that decides for this group (<see cref="OwnerWar"/>, <see cref="OwnerExpedition"/>);
        /// empty for a group the sim runs on its own template, and for a war party from an older save.
        /// Saved (Record.owner).
        /// </summary>
        public string Owner = string.Empty;

        /// <summary>
        /// The war director's party: hunting a quarry, and owned by no other director. An empty owner
        /// is an older save's party. A released party (quarry cleared) is nobody's war party any more.
        /// </summary>
        public bool IsOwnedByWar => IsWarParty && (string.IsNullOrEmpty(Owner) || Owner == OwnerWar);

        /// <summary>A director decides this group's goal; the sim's own hunter and errand rules leave it alone.</summary>
        public bool IsDirected => IsWarParty || !string.IsNullOrEmpty(Owner);

        /// <summary>
        /// The owner's members, replacing the template's and the roster's draw (an expedition's are
        /// its own residents). Null: the template decides. Runtime only.
        /// </summary>
        [NonSerialized] public List<PlannedMember> PlannedOverride;

        /// <summary>
        /// Called for each member before its network spawn, after GroupMembership is stamped, with its
        /// plan index. Runtime only.
        /// </summary>
        [NonSerialized] public Action<GameObject, int> MemberStamp;

        /// <summary>
        /// Exact poses for the next spawn, by plan index, used instead of formation slots (a hand-off in
        /// view). Consumed by that spawn: set to null as it starts. Runtime only.
        /// </summary>
        [NonSerialized] public List<Pose> SpawnPoses;

        /// <summary>
        /// Called on a fold for every member still in <see cref="Live"/>, with its plan index, before it
        /// is despawned. The dead are included — a corpse is deactivated, not destroyed — so check its
        /// health; a member destroyed outright (its chunk unloaded) is gone and gets no call. Runtime only.
        /// </summary>
        [NonSerialized] public Action<GameObject, int> ReadBack;

        // Runtime only, never saved. Counted by GroupMembership while the group is spawned and reset on
        // every spawn, which is why a folded party cannot be "defeated": nobody can reach it.
        [NonSerialized] public int FightersSpawned;
        [NonSerialized] public int FightersDead;

        /// <summary>
        /// A group with no member and no fighter left standing, dismounted riders included
        /// (WarPartyRules.IsWipedOut). It never re-spawns in this world: a war party is resolved by the
        /// director, any other group -- a caravan, a herd -- is simply gone. Saved (Record.wipedOut),
        /// so a group wiped out just before a save does not respawn at full strength on load; a war
        /// party is then seen Defeated instead.
        /// </summary>
        public bool WipedOut;

        /// <summary>
        /// A group with a transport has been dropped off and goes on foot from now on. False while it is
        /// still on its way in: folded it travels at the transport's speed, and it spawns aboard a
        /// vessel. Saved (Record.delivered), so a party that landed before a save does not fly in again.
        /// </summary>
        public bool Delivered;

        /// <summary>
        /// The group's crew is ashore (or on its way ashore/back) rather than seated aboard its
        /// carriers. Saved (Record.crewAshore), so a walking city mid-disembark on save comes back
        /// the same way rather than snapping its crew back aboard.
        /// </summary>
        public bool CrewAshore;

        /// <summary>The vessel flying this group in, or flying home after dropping it off. Runtime only.</summary>
        [NonSerialized] public GameObject Transport;

        /// <summary>The prefab <see cref="Transport"/> was spawned from: a parked hull is only reused for its own kind.</summary>
        [NonSerialized] public GameObject TransportPrefab;

        /// <summary>The dock slot <see cref="Transport"/> parks at around its home site; -1 with no vessel.</summary>
        [NonSerialized] public int TransportDock = -1;

        /// <summary>Seconds <see cref="Transport"/> has sat home with its riders still aboard.</summary>
        [NonSerialized] public float TransportParkedFor;

        /// <summary>Released while somebody could see it: removed the moment it folds, never popped out of view.</summary>
        [NonSerialized] public bool DisbandWhenFolded;

        /// <summary>First sight of the quarry since this spawn has been announced.</summary>
        [NonSerialized] public bool QuarrySeenThisSpawn;

        [NonSerialized] public readonly List<GameObject> Live = new();

        /// <summary>
        /// Everyone stamped a fighter this spawn (GroupMembership), on foot or seated. Not Live: a rider
        /// who dismounts belongs to no mount any more, and only this list still knows they are ours.
        /// </summary>
        [NonSerialized] public readonly List<GameObject> Fighters = new();

        public Vector3 Heading => HasGoal ? Flat(GoalPosition - Position).normalized : Vector3.forward;

        public float FlatDistanceTo(Vector3 point) => Flat(point - Position).magnitude;

        /// <summary>
        /// Walk this record toward its goal for one tick. Returns true on the tick it arrives.
        ///
        /// <para>
        /// A straight line, deliberately. Asking the NavMesh to path a group nobody can see costs a
        /// full path query per group per decision for a route that is never drawn — and across open
        /// dunes the two answers differ by a few percent of distance.
        /// </para>
        /// <para>
        /// Lives here rather than on the simulator so it can be asserted on directly: "a caravan at
        /// 3.5 m/s covers 2 km in the time it should" is the single behaviour the virtual layer has
        /// to get right, and it should not need a scene to check.
        /// </para>
        /// </summary>
        public bool AdvanceToward(float speed, float delta)
        {
            if (!HasGoal) return false;

            Vector3 toGoal = GoalPosition - Position;
            toGoal.y = 0f;

            float distance = toGoal.magnitude;
            float step = Mathf.Max(0.01f, speed) * Mathf.Max(0f, delta);

            // Arrival is "within this tick's step OR inside the destination", so a fast group cannot
            // stride past a small site and orbit it forever.
            if (distance <= Mathf.Max(step, ArriveRadius))
            {
                Position = GoalPosition;
                return true;
            }

            Position += toGoal / distance * step;
            return false;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v : Vector3.forward * 1e-3f;
        }

        /// <summary>The serialisable half, for the save file. Live GameObjects are deliberately absent.</summary>
        [Serializable]
        public struct Record
        {
            public string id;
            public string templateId;
            public Vector3 position;
            public Vector3 goalPosition;
            public bool hasGoal;
            public float arriveRadius;
            public int taskIndex;
            public float dwellRemaining;
            public string lastSiteId;
            public Vector3 lead;
            public bool hasLead;
            public float leadAge;

            // Appended 2026-09-16 (rosters spec §4.2). Older saves read 0, null, 0: no seed (ApplyRecord
            // keeps the group's own), not a war party, tier 0.
            public int rosterSeed;
            public string quarryProfileId;
            public int tier;

            // Appended 2026-09-16 (rosters spec, review fix round 1). Older saves read false: a party
            // that was mid-fight when an old save was written comes back alive, same as it always did.
            public bool wipedOut;

            // Appended 2026-09-17 (sky tribe plan, Task 7). Older saves read false: a party with a
            // transport flies in again; one without never reads it.
            public bool delivered;

            // Appended 2026-09-24 (Striders walking city). Older saves read false: the group comes
            // back marching, its crew seated, which is what every group without crew already does.
            public bool crewAshore;

            // Appended 2026-10-03 (settlement expeditions spec §4.4). Older saves read null: no owner, so
            // a war party is still the war director's (IsOwnedByWar).
            public string owner;
        }

        public Record ToRecord() => new Record
        {
            id = Id,
            templateId = TemplateId,
            position = Position,
            goalPosition = GoalPosition,
            hasGoal = HasGoal,
            arriveRadius = ArriveRadius,
            taskIndex = TaskIndex,
            dwellRemaining = DwellRemaining,
            lastSiteId = LastSiteId,
            lead = Lead,
            hasLead = HasLead,
            leadAge = LeadAge,
            rosterSeed = RosterSeed,
            quarryProfileId = QuarryProfileId,
            tier = Tier,
            wipedOut = WipedOut,
            delivered = Delivered,
            crewAshore = CrewAshore,
            owner = Owner,
        };

        public void ApplyRecord(in Record record)
        {
            Position = record.position;
            GoalPosition = record.goalPosition;
            HasGoal = record.hasGoal;
            ArriveRadius = record.arriveRadius > 0f ? record.arriveRadius : 8f;
            TaskIndex = record.taskIndex;
            DwellRemaining = record.dwellRemaining;
            LastSiteId = record.lastSiteId ?? string.Empty;
            Lead = record.lead;
            HasLead = record.hasLead;
            LeadAge = record.leadAge;
            // 0 is what a save from before seeds were saved reads: every group then drew from its id's
            // StableHash, so it keeps drawing the people it had rather than a new world's seed.
            RosterSeed = record.rosterSeed != 0 ? record.rosterSeed : RosterDraw.StableHash(Id);
            QuarryProfileId = record.quarryProfileId ?? string.Empty;
            Tier = Mathf.Max(0, record.tier);
            WipedOut = record.wipedOut;
            Delivered = record.delivered;
            CrewAshore = record.crewAshore;
            Owner = record.owner ?? string.Empty;
        }
    }
}
