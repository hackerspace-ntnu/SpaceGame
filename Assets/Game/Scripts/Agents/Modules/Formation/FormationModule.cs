// Keeps a group travelling together in a loose column.
//
// Sibling to HerdModule, not a replacement for it, and deliberately not an edit of it: a herd
// spreads onto a CIRCLE around a shared destination, which is right for animals settling at a
// waterhole and wrong for anything crossing a map. A caravan wants a line — mostly. Both live at
// Social priority and a prefab takes whichever it wants.
//
// The division of labour: the LEADER is not managed at all. It runs its own task, goal, wander and
// combat modules exactly as if it were alone, and this module only reads where it is and which way
// it is going. Followers steer to a slot behind it. That is what keeps a caravan's route the
// product of one NPC's actual errand rather than of a formation controller inventing destinations.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public class FormationModule : BehaviourModuleBase
    {
        [Header("Formation")]
        [Tooltip("Members sharing this id travel together. One of them should be the leader.")]
        [SerializeField] private string formationId = "caravan";

        [Tooltip("This member sets the route; everyone else follows it. If nobody in a formation is " +
                 "flagged, the first to register leads by default.")]
        [SerializeField] private bool isLeader = false;

        [Header("Shape")]
        [SerializeField] private FormationShape shape = new FormationShape
        {
            Lanes = 2,
            RowSpacing = 4.5f,
            LaneSpacing = 3f,
            LateralJitter = 0.9f,
            LongitudinalJitter = 1.2f,
            DriftAmplitude = 0.7f,
            DriftRate = 0.08f,
        };

        [Header("Keeping up")]
        [Tooltip("How close to its slot a follower must be before it stops steering and yields the " +
                 "frame to idle/look-around modules. Too small and followers never stop correcting, " +
                 "which reads as twitching.")]
        [SerializeField] private float slotTolerance = 1.6f;

        [Tooltip("Extra speed per metre behind the slot. 0.12 means a follower 3 m adrift moves " +
                 "about 36% faster until it closes.")]
        [SerializeField] private float catchUpGain = 0.12f;

        [SerializeField] private float minSpeedMultiplier = 0.85f;
        [SerializeField] private float maxSpeedMultiplier = 1.35f;

        [Header("At rest")]
        [Tooltip("Below this leader speed the group is treated as stopped: followers gather loosely " +
                 "within restRadius and then yield, instead of holding a marching column in place.")]
        [SerializeField] private float leaderMovingSpeed = 0.4f;

        [Tooltip("How loosely the group clusters once the leader has stopped.")]
        [SerializeField] private float restRadius = 6f;

        [Header("Recovery")]
        [Tooltip("A follower further than this from its slot has been separated — by a fight, a " +
                 "cliff, a chunk boundary — and heads straight for the leader instead of its slot, " +
                 "which is what stops a lost member trying to hold formation from 200 m away.")]
        [SerializeField] private float regroupDistance = 40f;

        [SerializeField] private float navSampleDistance = 6f;

        // ── Shared membership ────────────────────────────────────────────────────
        private static readonly Dictionary<string, List<FormationModule>> formations = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => formations.Clear();

        /// <summary>
        /// The leader's travel direction, smoothed.
        ///
        /// Written by the leader during its own tick and read by followers during theirs, so it can
        /// be one frame stale. That is fine and is why it is not computed per-follower: the
        /// alternative is every follower sampling the leader's motor, which both costs more and
        /// gives each of them a slightly different answer about which way the group is facing.
        /// </summary>
        private Vector3 smoothedHeading = Vector3.forward;

        private int memberSeed;

        public string FormationId => formationId;
        public bool IsLeader => isLeader;

        private void Reset() => SetPriorityDefault(ModulePriority.Social);

        private void Awake()
        {
            // Stable for this member's lifetime, which is what FormationMath needs to give it a
            // consistent personal offset. Instance id rather than list index: index changes when
            // anyone ahead of it in the list dies, and the whole formation would visibly reshuffle.
            memberSeed = GetInstanceID();
            smoothedHeading = transform.forward;
        }

        private void OnEnable()  => Register(this);
        private void OnDisable() => Unregister(this);

        public override string ModuleDescription =>
            "Group travel in a loose column. Sibling to HerdModule, which spreads onto a circle instead.\n\n" +
            "• formationId — members sharing this string travel together\n" +
            "• isLeader — this member routes; everyone else follows. Its own task/goal drives the group.\n" +
            "• shape.Lanes — 1 = single file, 2 = mostly a line, 3+ = a travelling mob\n" +
            "• LateralJitter / DriftAmplitude — what stops the column looking printed\n" +
            "• catchUpGain — stragglers speed up to close, so the leader never has to wait\n\n" +
            "Followers yield the frame once in position, so idle/look-around modules still run while " +
            "walking. For a mounted caravan, put this on the MOUNTS — the animals form the line.";

        // ── Registry ─────────────────────────────────────────────────────────────

        private static void Register(FormationModule member)
        {
            if (string.IsNullOrWhiteSpace(member.formationId)) return;

            if (!formations.TryGetValue(member.formationId, out List<FormationModule> list))
                formations[member.formationId] = list = new List<FormationModule>();

            if (!list.Contains(member)) list.Add(member);
        }

        private static void Unregister(FormationModule member)
        {
            if (member.formationId != null && formations.TryGetValue(member.formationId, out List<FormationModule> list))
                list.Remove(member);
        }

        /// <summary>
        /// Move this member into a different formation at runtime.
        ///
        /// Needed because formation membership is baked into a prefab, and NpcWorldSim spawns many
        /// groups from the same prefabs — without re-keying, every caravan in the world would be one
        /// enormous formation trying to follow a single leader on the other side of the map.
        /// </summary>
        public void SetFormation(string newFormationId, bool leader)
        {
            bool wasRegistered = isActiveAndEnabled;
            if (wasRegistered) Unregister(this);

            formationId = string.IsNullOrWhiteSpace(newFormationId) ? "caravan" : newFormationId;
            isLeader = leader;

            if (wasRegistered) Register(this);
        }

        public void SetShape(FormationShape newShape) => shape = newShape.Sanitised();

        /// <summary>The current leader of <paramref name="id"/>, or null if that formation is empty.</summary>
        public static FormationModule LeaderOf(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !formations.TryGetValue(id, out List<FormationModule> list))
                return null;

            FormationModule fallback = null;

            for (int i = 0; i < list.Count; i++)
            {
                FormationModule member = list[i];
                if (member == null || !member.isActiveAndEnabled) continue;

                if (member.isLeader) return member;
                fallback ??= member;
            }

            // Nobody flagged: the first live member leads, so a formation whose designated leader
            // has died keeps moving instead of stopping where it stood.
            return fallback;
        }

        public static int MemberCount(string id) =>
            !string.IsNullOrWhiteSpace(id) && formations.TryGetValue(id, out List<FormationModule> list)
                ? list.Count
                : 0;

        // ── Tick ─────────────────────────────────────────────────────────────────

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            FormationModule leader = LeaderOf(formationId);
            if (leader == null) return null;

            if (leader == this)
            {
                TrackHeading(in context, deltaTime);

                // The leader is never steered by this module. Its route is its own business — that
                // is the entire point of the split.
                return null;
            }

            int followerIndex = FollowerIndexOf(this);
            if (followerIndex < 0) return null;

            Vector3 leaderPosition = leader.transform.position;
            bool moving = leader.LeaderSpeed >= leaderMovingSpeed;

            // Separated members abandon the shape and just come back. Trying to hold a slot from
            // across a ravine produces an agent walking into a wall for as long as it takes someone
            // to notice.
            float distanceToLeader = Flat(leaderPosition - context.Position).magnitude;
            if (distanceToLeader > regroupDistance)
            {
                return MoveIntent.MoveTo(leaderPosition, restRadius, maxSpeedMultiplier, isRunning: true);
            }

            Vector3 slot = moving
                ? FormationMath.SlotPosition(followerIndex, leaderPosition, leader.smoothedHeading,
                                             in shape, memberSeed, Time.time)
                : RestPosition(followerIndex, leaderPosition);

            // Tolerance widens when the group has stopped, so a halted caravan settles into a loose
            // cluster and hands the frame to idle behaviour rather than shuffling onto marks.
            float tolerance = moving ? slotTolerance : restRadius;

            float distanceToSlot = Flat(slot - context.Position).magnitude;
            if (distanceToSlot <= tolerance)
                return null;

            if (NavMesh.SamplePosition(slot, out NavMeshHit hit, navSampleDistance, NavMesh.AllAreas))
                slot = hit.position;

            float speed = FormationMath.CatchUpSpeed(distanceToSlot, tolerance, catchUpGain,
                                                     minSpeedMultiplier, maxSpeedMultiplier);

            return MoveIntent.MoveTo(slot, tolerance * 0.75f, speed);
        }

        // ── Leader state ─────────────────────────────────────────────────────────

        /// <summary>Horizontal speed of this member, as the formation measures it.</summary>
        public float LeaderSpeed { get; private set; }

        private void TrackHeading(in AgentContext context, float deltaTime)
        {
            Vector3 velocity = Flat(context.Velocity);
            LeaderSpeed = velocity.magnitude;

            if (LeaderSpeed < 0.05f)
            {
                // Standing still has no direction. Keeping the last one rather than snapping to
                // transform.forward stops the whole column swinging round the moment the leader
                // pauses and its body settles a few degrees.
                return;
            }

            Vector3 desired = velocity / LeaderSpeed;
            smoothedHeading = Vector3.Slerp(smoothedHeading, desired,
                                            1f - Mathf.Exp(-4f * Mathf.Max(0.0001f, deltaTime)));
        }

        /// <summary>
        /// Where a follower waits when the group has halted: a ring around the leader, offset by the
        /// member's own fixed bias so a stopped caravan is a scatter rather than a wheel.
        /// </summary>
        private Vector3 RestPosition(int followerIndex, Vector3 leaderPosition)
        {
            int total = Mathf.Max(1, MemberCount(formationId) - 1);
            float angle = (followerIndex / (float)total) * Mathf.PI * 2f
                          + FormationMath.Hash01(memberSeed, 7) * 0.8f;

            float radius = restRadius * (0.55f + FormationMath.Hash01(memberSeed, 11) * 0.45f);

            return leaderPosition + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        }

        /// <summary>
        /// This member's index among the followers, leader excluded and dead members skipped.
        ///
        /// Recomputed rather than cached because membership changes: a caravan that loses two
        /// animals to a fight should close up into a shorter column, not keep two holes in it.
        /// </summary>
        private int FollowerIndexOf(FormationModule member)
        {
            if (!formations.TryGetValue(formationId, out List<FormationModule> list)) return -1;

            int index = 0;
            for (int i = 0; i < list.Count; i++)
            {
                FormationModule candidate = list[i];
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                if (candidate.isLeader) continue;

                if (candidate == member) return index;
                index++;
            }

            return -1;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        protected override void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(formationId)) formationId = "caravan";

            shape = shape.Sanitised();
            slotTolerance = Mathf.Max(0.3f, slotTolerance);
            catchUpGain = Mathf.Max(0f, catchUpGain);
            minSpeedMultiplier = Mathf.Clamp(minSpeedMultiplier, 0.1f, 1f);
            maxSpeedMultiplier = Mathf.Max(1f, maxSpeedMultiplier);
            leaderMovingSpeed = Mathf.Max(0.05f, leaderMovingSpeed);
            restRadius = Mathf.Max(1f, restRadius);
            regroupDistance = Mathf.Max(restRadius + 5f, regroupDistance);
            navSampleDistance = Mathf.Max(0.5f, navSampleDistance);
        }
    }
}
