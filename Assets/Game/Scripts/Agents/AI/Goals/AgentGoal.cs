// Where this agent is trying to get to, and the only place that answer is written down.
//
// The same shape as AgentTargeting, for the same reason. Targeting exists because five combat
// modules each resolving their own target meant one agent could chase A, shoot B and back away
// from C in the same frame. Travel has the identical failure available to it: a task module that
// drove movement itself would be a second locomotion authority arguing with WanderModule,
// ChaseModule and the formation over the same body.
//
// So a task writes a destination here and stops. Modules read it and move. Nothing that decides
// WHERE to go is allowed to touch HOW to go, and the priority ladder in AgentController arbitrates
// between travelling and everything more urgent without either side knowing about the other.
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class AgentGoal : MonoBehaviour
    {
        [Tooltip("Draw the current goal in the Scene view while this object is selected.")]
        [SerializeField] private bool drawGizmos = true;

        /// <summary>False when the agent has nowhere it is trying to be. That is a normal state.</summary>
        public bool HasGoal { get; private set; }

        public Vector3 Position { get; private set; }

        /// <summary>How close counts as there. Usually the destination site's radius.</summary>
        public float ArriveRadius { get; private set; } = 2f;

        /// <summary>Human-readable, for chatter, dialog and debugging. "picking over the Vela wreck".</summary>
        public string Reason { get; private set; } = string.Empty;

        /// <summary>The <see cref="World.WorldSite"/> this goal came from, if any. Empty otherwise.</summary>
        public string SiteId { get; private set; } = string.Empty;

        /// <summary>
        /// How fast to cover this particular journey, relative to the motor's base speed.
        ///
        /// Carried on the goal rather than configured on the travelling module because it is a
        /// property of the errand, not of the agent: the same NPC ambles to a well and hurries back
        /// to camp at dusk, and only whoever set the destination knows which this is.
        /// </summary>
        public float SpeedMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Horizontal distance to the goal, or infinity when there is none.
        ///
        /// Flat deliberately: the vertical component over a heightmap is noise, and an agent
        /// standing at the foot of a mesa with its goal on top would otherwise never count as
        /// arrived no matter how long it stood there.
        /// </summary>
        public float DistanceToGoal
        {
            get
            {
                if (!HasGoal) return float.PositiveInfinity;

                Vector3 self = transform.position;
                float dx = Position.x - self.x;
                float dz = Position.z - self.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
        }

        /// <summary>
        /// Stand at the goal once there, rather than handing the frame to wander — a guard at a
        /// gate, a vendor at a stall. <see cref="GoalTravelModule"/> holds the spot.
        /// </summary>
        public bool HoldOnArrival { get; private set; }

        /// <summary>What a holding agent faces. Null keeps whatever heading it arrived with.</summary>
        public Vector3? FacePoint { get; private set; }

        /// <summary>
        /// Slack a HOLDING goal adds to its arrive radius. The motor stops at the radius plus its own
        /// margin, so without this a held agent stops just outside "arrived" and paces back and forth.
        /// </summary>
        public const float HoldArriveSlack = 0.75f;

        /// <summary>How far counts as arrived for a goal of <paramref name="arriveRadius"/>.</summary>
        public static float ArrivalRadius(float arriveRadius, bool hold) =>
            hold ? arriveRadius + HoldArriveSlack : arriveRadius;

        public bool HasArrived => HasGoal && DistanceToGoal <= ArrivalRadius(ArriveRadius, HoldOnArrival);

        /// <summary>
        /// Send this agent somewhere.
        ///
        /// The position is taken as given — sampling it onto the NavMesh is the caller's job,
        /// because only the caller knows whether an off-mesh answer means "pick somewhere else"
        /// (a task choosing between sites) or "go as close as you can" (a hunter walking at a
        /// last-known position that may be mid-air).
        /// </summary>
        public void Set(Vector3 position, float arriveRadius, string reason = null, string siteId = null,
                        float speedMultiplier = 1f)
        {
            Position = position;
            ArriveRadius = Mathf.Max(0.5f, arriveRadius);
            Reason = reason ?? string.Empty;
            SiteId = siteId ?? string.Empty;
            SpeedMultiplier = Mathf.Max(0.01f, speedMultiplier);
            HoldOnArrival = false;
            FacePoint = null;
            HasGoal = true;
        }

        /// <summary>
        /// Send this agent somewhere to stand: as the other <see cref="Set"/>, plus
        /// <paramref name="hold"/> and the point to face there (<paramref name="facePoint"/>, null
        /// to keep the arrival heading). Always true — the position is taken as given — and bool
        /// so it reads the same as <see cref="TrySetSampled(Vector3, float, string, bool, Vector3?, float)"/>
        /// at a call site that chooses between them.
        /// </summary>
        public bool Set(Vector3 position, float arriveRadius, string reason, bool hold, Vector3? facePoint,
                        float speedMultiplier = 1f)
        {
            Set(position, arriveRadius, reason, null, speedMultiplier);
            HoldOnArrival = hold;
            FacePoint = facePoint;
            return true;
        }

        /// <summary>
        /// As <see cref="Set"/>, but snapped onto the NavMesh first. Returns false and leaves the
        /// existing goal untouched when nothing walkable is within <paramref name="sampleDistance"/>.
        /// </summary>
        public bool TrySetSampled(Vector3 position, float arriveRadius, float sampleDistance = DefaultSampleDistance,
                                  string reason = null, string siteId = null, float speedMultiplier = 1f)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
                return false;

            Set(hit.position, arriveRadius, reason, siteId, speedMultiplier);
            return true;
        }

        /// <summary>
        /// The holding <see cref="Set(Vector3, float, string, bool, Vector3?, float)"/>, snapped onto
        /// the NavMesh within <see cref="DefaultSampleDistance"/> first. False, and the existing goal
        /// untouched, when the point is off-mesh. The face point is not sampled — it is looked at,
        /// not walked to.
        /// </summary>
        public bool TrySetSampled(Vector3 position, float arriveRadius, string reason, bool hold, Vector3? facePoint,
                                  float speedMultiplier = 1f)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, DefaultSampleDistance, NavMesh.AllAreas))
                return false;

            return Set(hit.position, arriveRadius, reason, hold, facePoint, speedMultiplier);
        }

        /// <summary>How far off the NavMesh a sampled goal may start. The default of the other overload.</summary>
        public const float DefaultSampleDistance = 12f;

        public void Clear()
        {
            HasGoal = false;
            Reason = string.Empty;
            SiteId = string.Empty;
            SpeedMultiplier = 1f;
            HoldOnArrival = false;
            FacePoint = null;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Deliberately not <see cref="Set"/>: the goal is being put back exactly as it was, not
        /// issued, so it must not be re-sampled onto the NavMesh (the mesh under it may not be
        /// loaded yet) and an absent goal must come back absent rather than as a goal at the origin.
        /// </summary>
        public void RestoreGoal(bool hasGoal, Vector3 position, float arriveRadius, string reason,
                                string siteId, float speedMultiplier, bool hold = false, Vector3? facePoint = null)
        {
            if (!hasGoal)
            {
                Clear();
                Position = Vector3.zero;
                ArriveRadius = 2f;
                return;
            }

            Position = position;
            ArriveRadius = Mathf.Max(0.5f, arriveRadius);
            Reason = reason ?? string.Empty;
            SiteId = siteId ?? string.Empty;
            SpeedMultiplier = Mathf.Max(0.01f, speedMultiplier);
            HoldOnArrival = hold;
            FacePoint = facePoint;
            HasGoal = true;
        }

        /// <summary>
        /// Attach or fetch. Used by AgentController so a prefab that predates this component still
        /// gets one, rather than every reader having to null-check a component that is almost
        /// always wanted.
        /// </summary>
        public static AgentGoal GetOrAdd(GameObject host)
        {
            if (host == null) return null;
            AgentGoal existing = host.GetComponent<AgentGoal>();
            return existing != null ? existing : host.AddComponent<AgentGoal>();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !HasGoal) return;

            Gizmos.color = new Color(0.35f, 0.8f, 1f);
            Gizmos.DrawLine(transform.position, Position);
            Gizmos.DrawWireSphere(Position, ArriveRadius);
        }
    }
}
