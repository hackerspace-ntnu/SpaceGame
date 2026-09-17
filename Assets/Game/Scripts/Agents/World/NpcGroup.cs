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

        [Min(1)]
        public int count = 1;
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

        // Runtime only, never saved. Counted by GroupMembership while the group is spawned and reset on
        // every spawn, which is why a folded party cannot be "defeated": nobody can reach it.
        [NonSerialized] public int FightersSpawned;
        [NonSerialized] public int FightersDead;

        /// <summary>
        /// A war party with no member left and no fighter standing, dismounted riders included
        /// (WarPartyRules.IsWipedOut). It never re-spawns; the director resolves it. Saved
        /// (Record.wipedOut), so a party wiped out just before a save does not respawn at full
        /// strength on load — the director sees it Defeated instead.
        /// </summary>
        public bool WipedOut;

        /// <summary>
        /// A group with a transport has been dropped off and goes on foot from now on. False while it is
        /// still on its way in: folded it travels at the transport's speed, and it spawns aboard a
        /// vessel. Saved (Record.delivered), so a party that landed before a save does not fly in again.
        /// </summary>
        public bool Delivered;

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
            // 0 is what an older save reads. Taking it would re-seed a caravan once and save that back
            // for good, so the group keeps the seed it was created with (its id's StableHash).
            if (record.rosterSeed != 0) RosterSeed = record.rosterSeed;
            QuarryProfileId = record.quarryProfileId ?? string.Empty;
            Tier = Mathf.Max(0, record.tier);
            WipedOut = record.wipedOut;
            Delivered = record.delivered;
        }
    }
}
