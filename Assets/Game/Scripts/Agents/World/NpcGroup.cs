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

        [Tooltip("This member sets the group's route. Exactly one member of a group should lead; " +
                 "if none does, the first spawned takes it.")]
        public bool isLeader;

        [Min(1)]
        public int count = 1;
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

        [NonSerialized] public readonly List<GameObject> Live = new();

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
        }
    }
}
