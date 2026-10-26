// Circles its target at a distance, darting from one spot on a ring around it to the next -- the
// witch, unseen between her attacks, never standing still long enough for a slow rue bomb to land.
//
// A new reason to pick a destination, not a variant of chase or keep-distance: those close on the
// target or back away from it along one line, and a prowler that does either is easy to lead. This
// one keeps the range and changes the ANGLE, so a stasis shot has to be read off where she is going
// (footprints, the pulse) rather than aimed at where she was (GDC-L1-DESIGN-0002). Each dart picks a
// point some way round the ring from where she stands now, alternating sides at random, inside her
// arena so she never leaves the clearing.
//
// Reactive priority, where chase sits: attacks above it take the frame when they fire, and it hands
// the frame back down when there is no target.
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.Agents
{
    public class ProwlModule : BehaviourModuleBase
    {
        [Header("Ring")]
        [Tooltip("Closest she prowls to her target, metres.")]
        [SerializeField, Min(1f)] private float minRadius = 8f;

        [Tooltip("Furthest she prowls from her target, metres.")]
        [SerializeField, Min(1f)] private float maxRadius = 13f;

        [Tooltip("Smallest and largest turn round the ring per dart, degrees.")]
        [SerializeField] private Vector2 dartDegrees = new(40f, 100f);

        [Header("Pacing")]
        [Tooltip("Longest she runs at one spot before choosing another, seconds.")]
        [SerializeField, Min(0.2f)] private float repickSeconds = 2.5f;

        [Tooltip("How close to a spot counts as there, metres.")]
        [SerializeField, Min(0.1f)] private float arriveDistance = 0.8f;

        [Tooltip("How far off the NavMesh a chosen spot may be and still be used, metres.")]
        [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 3f;

        [Tooltip("How far her clearing's WitchArena may be, metres. With none she prowls unbounded.")]
        [SerializeField, Min(1f)] private float arenaSearchRadius = 60f;

        private bool hasSpot;
        private Vector3 spot;
        private float repickAt;

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

        private void OnEnable() => hasSpot = false;

        public override string ModuleDescription =>
            "Circles its target between minRadius and maxRadius, darting dartDegrees round the ring " +
            "each time, inside the nearest WitchArena. Runs. Passes with no target.\n\n" +
            "• minRadius / maxRadius / dartDegrees\n" +
            "• repickSeconds / arriveDistance";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentTargeting targeting = context.Targeting;
            if (targeting == null || !targeting.HasTarget)
            {
                hasSpot = false;
                return null;
            }

            Vector3 target = targeting.Target.position;

            bool arrived = hasSpot && (spot - context.Position).sqrMagnitude <= arriveDistance * arriveDistance;
            if (!hasSpot || arrived || Time.time >= repickAt)
            {
                hasSpot = TryPickSpot(context.Position, target, out spot);
                repickAt = Time.time + repickSeconds;
            }

            return hasSpot ? MoveIntent.MoveTo(spot, arriveDistance, 1f, isRunning: true) : null;
        }

        private bool TryPickSpot(Vector3 from, Vector3 target, out Vector3 result)
        {
            Vector3 offset = from - target;
            offset.y = 0f;
            float heading = offset.sqrMagnitude > 0.01f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);

            float turn = Random.Range(dartDegrees.x, dartDegrees.y) * (Random.value < 0.5f ? -1f : 1f);
            float radius = Random.Range(minRadius, maxRadius);
            Vector3 candidate = target + Quaternion.Euler(0f, heading + turn, 0f) * Vector3.forward * radius;

            if (WitchArena.TryFindNear(target, arenaSearchRadius, out WitchArena arena))
                candidate = arena.Clamp(candidate);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }

            result = from;
            return false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxRadius = Mathf.Max(minRadius, maxRadius);
            dartDegrees.y = Mathf.Max(dartDegrees.x, dartDegrees.y);
        }
    }
}
