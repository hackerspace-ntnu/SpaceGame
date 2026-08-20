// Last line of defence against ending up underneath the world.
//
// Nothing here tries to work out HOW a body got under the terrain, because the causes have nothing
// in common: a spawn resolved into a chunk that had not finished loading, a physics step that
// tunnelled a fast capsule through a terrain seam, a chunk unloading beneath someone standing on
// it, a vehicle settling into geometry that arrived a frame late. Fixing each of those where it
// happens is worth doing — and the spawn path is fixed directly, in SpawnPoint and SpawnManager —
// but a failsafe that enumerated causes would only ever cover the ones already known about.
// This measures the outcome instead, so a cause nobody has hit yet is still caught.
//
// Deliberately opt-in per prefab rather than automatic on every Rigidbody. Most things in the world
// are props whose falling through the floor costs nothing, while the bodies this protects — the
// player, the vehicles — are also the ones with their own ground handling to avoid fighting.
//
// It never fires during normal play. Every vehicle's own ground probe (FoilLift's centre ray,
// LeggedLocomotion's foot solver, rover suspension) works strictly above the surface, so the only
// way to reach the depth this reacts to is for something to have already gone wrong.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.World.Safety
{
    [DisallowMultipleComponent]
    public class UnderTerrainGuard : MonoBehaviour
    {
        [Header("Body")]
        [Tooltip("The transform to measure and move. Leave empty to use this GameObject. Set this " +
                 "on a vehicle whose pivot is not its body — the lift has to move the root that " +
                 "carries everything else, not a visual child.")]
        [SerializeField] private Transform bodyRoot;

        [Header("Detection")]
        [Tooltip("Seconds between checks. This is a failsafe, not a physics step — it never needs " +
                 "to run per-frame, and a terrain sample per body per frame is pure waste.")]
        [SerializeField] private float checkInterval = 0.25f;

        [Tooltip("How far below the terrain surface counts as buried. Must stay above the depth a " +
                 "body reaches by standing in a dip or clipping a mesh seam, or the guard will " +
                 "teleport players who are simply standing still.")]
        [SerializeField] private float depthTolerance = 0.5f;

        [Tooltip("How far above the surface a recovered body is placed. Matches SpawnPoint's " +
                 "groundClearance: the player capsule's bottom sits ~1m below the prefab pivot, so " +
                 "placing the pivot exactly on the surface buries half the collider and PhysX " +
                 "sometimes resolves that penetration downwards — straight back through the ground.")]
        [SerializeField] private float surfaceClearance = 1.2f;

        [Tooltip("With no terrain to sample, a body below this has left the world. Must sit well " +
                 "below the lowest real ground in any scene so ordinary play never reaches it.")]
        [SerializeField] private float absoluteFloorY = -500f;

        [Header("Debug")]
        [SerializeField] private bool logRecoveries = true;

        /// <summary>
        /// Every Rigidbody under the body root, not just the root's own.
        ///
        /// Moving a transform does move its children, but PhysX keeps each Rigidbody's position
        /// independently and snaps it back to whatever it last simulated. On a single-body prefab
        /// that costs one wrong frame; on an articulated one — the Rover's legs, bogies and twelve
        /// wheels all hang off joints — resyncing only the root drags the chassis away from parts
        /// that stayed behind and rips the joints apart. So every body is moved together.
        ///
        /// Empty is a normal case, not a failure: the DuneFoil has no Rigidbody anywhere and is
        /// driven entirely through its transform.
        /// </summary>
        private Rigidbody[] bodies;
        private bool[] bodyGravityWasOn;

        private float nextCheckTime;

        private bool isParked;
        private Vector3 parkedPosition;

        private Transform Body => bodyRoot != null ? bodyRoot : transform;

        /// <summary>Recoveries performed since load. Read by tests and diagnostics.</summary>
        public int RecoveryCount { get; private set; }

        private UnderTerrainRule Rule => new(depthTolerance, surfaceClearance, absoluteFloorY);

        private void Awake()
        {
            EnsureInitialised();

            // Stagger the first check so a lobby's worth of players spawning on the same frame do
            // not all sample terrain on the same one forever after.
            nextCheckTime = Time.time + Random.Range(0f, Mathf.Max(0.01f, checkInterval));
        }

        /// <summary>
        /// Lazy rather than Awake-only so the guard is usable the moment it exists — Awake does not
        /// run for a component added outside play mode, and a failsafe that silently does nothing
        /// until the next scene load is not one.
        /// </summary>
        private void EnsureInitialised()
        {
            if (bodies != null) return;

            bodies = Body.GetComponentsInChildren<Rigidbody>(true);
            bodyGravityWasOn = new bool[bodies.Length];
        }

        /// <summary>
        /// Run one evaluation now, ignoring the check interval. The timer exists to keep the cost
        /// down, not as part of the decision, so tests and diagnostics drive this directly rather
        /// than waiting real seconds for a tick.
        /// </summary>
        public void RunCheckNow()
        {
            EnsureInitialised();
            Evaluate();
        }

        /// <summary>
        /// Push each Rigidbody's simulated pose back to where its transform now is, and drop the
        /// velocity it built up on the way down — a body arrives here having fallen, so it carries
        /// enough downward speed to punch straight back through the surface on the next step and
        /// land right where it started.
        /// </summary>
        private void ResyncBodies()
        {
            foreach (var rb in bodies)
            {
                if (rb == null) continue;

                rb.position = rb.transform.position;
                rb.rotation = rb.transform.rotation;

                if (rb.isKinematic) continue;

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void FixedUpdate()
        {
            // While parked the body is held every physics step, not on the check interval — a body
            // held only four times a second visibly sinks between holds.
            if (isParked) HoldAtParkedPosition();
        }

        private void Update()
        {
            if (!HasAuthority()) return;
            if (Time.time < nextCheckTime) return;

            nextCheckTime = Time.time + Mathf.Max(0.01f, checkInterval);
            EnsureInitialised();
            Evaluate();
        }

        /// <summary>
        /// Only the side that owns the position may move it. On a client this would fight the
        /// server's replicated transform every interval; offline there is no server, and the
        /// local simulation is authoritative.
        /// </summary>
        private static bool HasAuthority() => !Network.IsNetworked || Network.Server;

        private void Evaluate()
        {
            Transform b = Body;

            // A parented body is carried, not independently positioned: a rider on a mount, a
            // passenger on a walker deck. Its carrier has its own guard, and lifting the two
            // separately would tear the rider off the seat. Deliberately phrased as "has a parent"
            // rather than a MountModule lookup so any future carrier is covered for free.
            if (b.parent != null)
            {
                if (isParked) ExitPark();
                return;
            }

            Vector3 position = b.position;
            bool hasTerrain = TerrainProbe.TryGetTerrainHeight(position, out float terrainY);
            var verdict = Rule.Evaluate(position.y, hasTerrain, terrainY);

            switch (verdict.Action)
            {
                case UnderTerrainAction.Lift:
                    if (isParked) ExitPark();
                    Lift(b, verdict.TargetY, terrainY);
                    break;

                case UnderTerrainAction.Park:
                    EnterPark(b);
                    break;

                default:
                    if (isParked) ExitPark();
                    break;
            }
        }

        private void Lift(Transform b, float targetY, float terrainY)
        {
            Vector3 was = b.position;
            Vector3 recovered = new(was.x, targetY, was.z);

            // Transform first — that carries the whole hierarchy — then resync physics to it.
            b.position = recovered;
            ResyncBodies();
            RecoveryCount++;

            if (logRecoveries)
            {
                Debug.LogWarning(
                    $"[UnderTerrainGuard] {name} was {terrainY - was.y:F1}m under the terrain at " +
                    $"{was.x:F0},{was.z:F0} — lifted to y={targetY:F1}.", this);
            }
        }

        /// <summary>
        /// Below the floor with no terrain to measure. Holding position is the whole point: the
        /// body stays at its own X/Z, which keeps the streamer's chunk request pinned there (the
        /// player registers itself as a tracked transform in PlayerController), so the terrain it
        /// fell through loads and the next evaluation turns this into an ordinary lift.
        ///
        /// The alternative — guessing a surface, or teleporting to a spawn point — moves the body
        /// away from the only place that can rescue it.
        /// </summary>
        private void EnterPark(Transform b)
        {
            if (isParked) return;

            isParked = true;
            parkedPosition = b.position;

            // Gravity off on every body, not just the root: on an articulated machine the parts
            // fall independently, so a held chassis with falling legs tears itself apart while it
            // waits. Each body's own setting is remembered — some are authored without gravity.
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;

                bodyGravityWasOn[i] = bodies[i].useGravity;
                bodies[i].useGravity = false;
            }

            ResyncBodies();

            Debug.LogError(
                $"[UnderTerrainGuard] {name} fell below y={absoluteFloorY} at " +
                $"{parkedPosition.x:F0},{parkedPosition.z:F0} with no terrain to sample. Holding " +
                "position until the chunk there loads.", this);
        }

        private void ExitPark()
        {
            isParked = false;

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                    bodies[i].useGravity = bodyGravityWasOn[i];
            }
        }

        private void HoldAtParkedPosition()
        {
            Body.position = parkedPosition;
            ResyncBodies();
        }
    }
}
