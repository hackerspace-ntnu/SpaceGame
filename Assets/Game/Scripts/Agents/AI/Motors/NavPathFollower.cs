// Following a NavMesh route to a destination that may move, for a motor that steers itself.
//
// A motor built on NavMeshAgent gets this for free. One that owns its own body -- a legged machine
// whose legs carry the pose (invariant I4), a wheeled Rigidbody -- cannot hand its transform to an
// agent, so it asks the NavMesh for a route and steers along it itself. This is that asking and
// steering, and nothing else: it answers "which point do I aim at this frame" and leaves turning
// that into throttle and yaw to the motor.
//
// The route is rebuilt when it goes stale -- the destination has moved further than
// `repathTolerance`, or the repath timer has run out -- and when none can be had it answers with
// the destination itself, so the motor still moves rather than standing there waiting for a path
// that is not coming (an unbaked test scene, a chunk the streamer has not finished, a destination
// off the mesh).
//
// The timer is `repathInterval` after a move and while there is no route, and the longer
// `stillTargetRepathInterval` once a route to a destination that has not moved is in hand: rebuilding
// an unchanged route every half second is the cost of a crowd that is not going anywhere new. A body
// pushed further than `cornerArriveRadius` off its route (a knockdown, a walker stepping on it) drops
// back to `repathInterval`, so it recovers as fast as it always did.
//
// Where corners come from is a delegate so the follower can be driven from a test with no baked
// surface. `NavMeshCorners` is the real one.
using SpaceGame.Locomotion;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public sealed class NavPathFollower
    {
        /// Fill `into` with the route from `from` to `to` and return how many corners are real.
        /// Anything under two means there is no route.
        public delegate int CornerSource(Vector3 from, Vector3 to, Vector3[] into);

        /// Longest route kept. NavMesh routes across the streamed world stay well inside this.
        private const int CornerCapacity = 64;

        // Profiler marker (Diagnostics.md → Profiling): the route fetch, the expensive half.
        private const string RepathMarkerName = "SpaceGame.NavPath.Repath";
        private static readonly ProfilerMarker RepathMarker = new(RepathMarkerName);

        private readonly float repathInterval;
        private readonly float stillTargetRepathInterval;
        private readonly float repathTolerance;
        private readonly float cornerArriveRadius;
        private readonly CornerSource corners;

        // The buffer is reused across recalculations; `path` reports how much of it is real, so a
        // stale tail from a longer route is never steered at.
        private readonly Vector3[] cornerBuffer = new Vector3[CornerCapacity];
        private readonly WalkerPath path = new WalkerPath();
        private Vector3? pathTarget;
        private float repathTimer;
        private bool hasPath;

        public NavPathFollower(float repathInterval, float repathTolerance, float cornerArriveRadius,
                               CornerSource corners)
            : this(repathInterval, repathTolerance, cornerArriveRadius, corners,
                   stillTargetRepathInterval: repathInterval)
        {
        }

        public NavPathFollower(float repathInterval, float repathTolerance, float cornerArriveRadius,
                               CornerSource corners, float stillTargetRepathInterval)
        {
            this.repathInterval = repathInterval;
            this.stillTargetRepathInterval = stillTargetRepathInterval;
            this.repathTolerance = repathTolerance;
            this.cornerArriveRadius = cornerArriveRadius;
            this.corners = corners;
        }

        /// A follower over the baked NavMesh, tuned by a motor's serialized settings.
        public NavPathFollower(in NavPathFollowerSettings settings)
            : this(settings.repathInterval, settings.repathTolerance, settings.cornerArriveRadius,
                   NavMeshCorners(settings.navMeshSampleDistance), settings.stillTargetRepathInterval)
        {
        }

        /// True while a route is being followed rather than a straight line to the destination.
        public bool HasPath => hasPath && path.HasPath;

        /// The corner currently being steered at, without advancing anything. For gizmos.
        public Vector3 CurrentCorner => path.CurrentCorner;

        /// Where to steer from `position` to reach `target` this frame: the next corner of the
        /// route, or `target` itself once the corners are spent or when there is no route at all.
        public Vector3 SteerTarget(Vector3 position, Vector3 target, float deltaTime)
        {
            repathTimer -= deltaTime;
            bool targetMoved = !pathTarget.HasValue ||
                               Vector3.Distance(pathTarget.Value, target) > repathTolerance;

            // The long wait is only safe while the body is on the route it is waiting with.
            if (repathTimer > repathInterval && hasPath && path.FlatDistanceFromLeg(position) > cornerArriveRadius)
                repathTimer = repathInterval;

            if (targetMoved || repathTimer <= 0f)
            {
                pathTarget = target;
                using (RepathMarker.Auto())
                {
                    int found = corners(position, target, cornerBuffer);
                    hasPath = found >= 2;
                    if (hasPath) path.Set(cornerBuffer, found);
                }

                // No route keeps the short timer: the streamer may bake the missing chunk any moment.
                repathTimer = targetMoved || !hasPath ? repathInterval : stillTargetRepathInterval;
            }

            // Once the corners are spent the motor is within the last leg of the route; steer at the
            // destination itself, which is where the stop distance is measured from anyway.
            if (hasPath && path.TryGetSteerTarget(position, cornerArriveRadius, out Vector3 corner))
                return corner;
            return target;
        }

        /// The corner beyond the one being steered at from `position`, so a motor can slow for a
        /// turn before it reaches it. False on the last leg and when there is no route.
        public bool TryGetCornerAfter(Vector3 position, out Vector3 corner)
        {
            corner = default;
            if (!hasPath || !path.TryGetSteerTarget(position, cornerArriveRadius, out _)) return false;
            return path.TryGetCornerAfterCurrent(out corner);
        }

        /// Rebuild the route on the next `SteerTarget`, however fresh the current one is.
        public void RequestRepath() => repathTimer = 0f;

        /// Forget the route. The next `SteerTarget` builds a new one from scratch.
        public void Clear()
        {
            path.Clear();
            hasPath = false;
            pathTarget = null;
            repathTimer = 0f;
        }

        /// Routes from the baked NavMesh. Both ends are first snapped onto the mesh within
        /// `sampleDistance` -- a hull riding metres above the ground needs this to clear its ride
        /// height. A partial route is kept: it is the best way toward a destination the mesh cannot
        /// fully reach, and the next repath picks up the rest once the streamer bakes it.
        public static CornerSource NavMeshCorners(float sampleDistance)
        {
            // Created on first use rather than here: NavMeshPath's constructor calls into native
            // code, which Unity forbids during deserialisation -- and a motor may well build its
            // follower from a field initializer.
            NavMeshPath navPath = null;

            return (from, to, into) =>
            {
                if (!TrySampleNavMesh(from, sampleDistance, out Vector3 start)) return 0;
                if (!TrySampleNavMesh(to, sampleDistance, out Vector3 end)) return 0;

                navPath ??= new NavMeshPath();
                // Ground only: the legs follow corners in straight lines and cannot cross a ladder or a
                // jump link, so a route planned over one would walk them into the foot of it.
                if (!NavMesh.CalculatePath(start, end, SpaceGame.Gameplay.NavLinkAreas.GroundMask, navPath)) return 0;
                if (navPath.status == NavMeshPathStatus.PathInvalid) return 0;

                int found = navPath.GetCornersNonAlloc(into);
                return found < 2 ? 0 : found;
            };
        }

        private static bool TrySampleNavMesh(Vector3 around, float sampleDistance, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(around, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
            {
                onMesh = hit.position;
                return true;
            }

            onMesh = around;
            return false;
        }
    }
}
