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
//
// Two things about it are specifically about multiplayer, and both were learned from the same bug —
// clients spawning under the world and staying there while the host never did.
//
//   * It runs on whoever OWNS the body, not on the server. The player's transform is
//     owner-authoritative, so a server-side lift of a remote player is overwritten within a tick.
//     Gating on the server ran the guard only where it could not work. See HasAuthority.
//
//   * It would rather hold a body still than let it fall. Chunk loads are issued by the server and
//     applied on each client asynchronously, so a client's player object can exist before that
//     client's copy of the ground under it does. Waiting where you stand costs a moment; falling
//     through ground that is on its way costs a six-hundred-metre drop and a burial at the end of
//     it. See IsAwaitingGround.
//
//   * But it will not hold forever, and that is the third thing multiplayer taught it. A hold
//     assumes the ground is coming; a body that ends up somewhere the streamer owes nothing — off
//     the grid, or over a chunk authored without terrain — is waiting for something nobody has
//     promised. That happened for real: a wing-pack launch put a pilot at the world origin and
//     they were pinned there, in the dark, with no way out but quitting. So the hold is bounded
//     and ends in a recovery to the last ground the body actually stood on. See Recover.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

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

        [Header("Waiting for ground")]
        [Tooltip("How long to hold a body still while the chunk it is standing over loads. Bounds " +
                 "the wait so a hold can never become permanent: if the ground has not arrived by " +
                 "then, something is wrong that holding will not fix, and a body left frozen " +
                 "forever is worse than one that falls and gets lifted back out.\n\n" +
                 "Bounds the below-the-floor hold as well, which is the one that has no way of " +
                 "ending on its own when the body is somewhere the streamer owes nothing.")]
        [SerializeField] private float groundWaitTimeout = 20f;

        [Tooltip("How far under a body to look for a built floor. A body standing on one is " +
                 "supported whatever the heightmap says, and must not be lifted to the surface " +
                 "outside — that moves it INTO the floor rather than out of trouble. A shade over " +
                 "the surface clearance, so the floor it is standing on is within reach and the " +
                 "deck below is not.")]
        [SerializeField] private float structureFloorReach = 1.7f;

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

        /// <summary>
        /// When this body first found itself inside the streamed world with no ground under it, or
        /// -1 when it is not waiting. See <see cref="IsAwaitingGround"/>.
        /// </summary>
        private float groundWaitStartedAt = -1f;

        /// <summary>
        /// When the body was first parked below the absolute floor with nothing owed here, or -1
        /// when it is not in that hold. Separate from <see cref="groundWaitStartedAt"/> because
        /// they are opposite situations: that one is waiting for ground the streamer is bringing,
        /// this one is waiting for ground nobody has promised.
        /// </summary>
        private float floorParkStartedAt = -1f;

        /// <summary>
        /// The last place this body was measurably standing on the world — terrain under it and
        /// nothing wrong. It is where a body that has fallen out of the world is put back, and it
        /// is the only recovery target that is certainly somewhere the player can play from.
        /// </summary>
        private Vector3 lastSafePosition;
        private bool hasSafePosition;

        /// <summary>Said once per stranding, so a recovery that cannot be made is not silent spam.</summary>
        private bool warnedNoRecoveryTarget;

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
            if (!HasAuthority())
            {
                // Released, not just skipped, and the difference matters because authority can be
                // lost while a hold is running. A body is owned by everyone between Instantiate and
                // its network spawn (an unspawned NetworkObject has no remote truth to defer to),
                // and ownership moves again when a rider takes a vehicle. A hold is only ever ended
                // by an evaluation, so one begun on a machine that then stops deciding this body's
                // position would go on pinning it — against the transform sync — for good.
                if (isParked) ExitPark();
                groundWaitStartedAt = -1f;
                return;
            }

            if (Time.time < nextCheckTime) return;

            nextCheckTime = Time.time + Mathf.Max(0.01f, checkInterval);
            EnsureInitialised();
            Evaluate();
        }

        /// <summary>
        /// Only the side that owns the position may move it.
        ///
        /// Ownership, NOT the server, and the difference is the whole reason this guard did nothing
        /// online. The player's NetworkTransform is owner-authoritative (AuthorityMode: Owner), so
        /// for a remote player the server is not the side that owns the position — a lift written
        /// there is overwritten by that owner's next state update, within a tick and silently. A
        /// server-gated guard therefore ran on the one machine that could not make the move stick
        /// and refused to run on the one that could, which is exactly why a buried player recovered
        /// for the host and stayed buried for everyone else.
        ///
        /// Nothing else changes: offline <see cref="Network.Owns"/> is true, the host owns its own
        /// body, and a server-spawned bot or vehicle is owned by the server as before.
        /// </summary>
        private bool HasAuthority() => Network.Owns(this);

        private void Evaluate()
        {
            Transform b = Body;

            // A carried body is not independently positioned: a rider on a mount, a passenger on a
            // walker deck, a crew member strapped into a ship riding a crash landing down. Its
            // carrier has its own guard, and lifting the two separately would tear the rider off the
            // seat.
            //
            // Two tests, because there are two ways to carry. Parenting covers the mount system and
            // anything else that carries by hierarchy. CarriedBody covers the carriers that cannot
            // parent at all — the player's NetworkTransform is owner-authoritative and world-space,
            // so the arrival carries its crew by writing their pose, and such a rider has no parent
            // to find. Riding 2 km down inside a hull, that rider is over ground the guard has every
            // reason to think it has fallen through.
            if (b.parent != null || SpaceGame.Agents.CarriedBody.IsHeld(b.gameObject))
            {
                if (isParked) ExitPark();
                return;
            }

            Vector3 position = b.position;
            bool hasTerrain = TerrainProbe.TryGetTerrainHeight(position, out float terrainY);
            bool awaitingGround = IsAwaitingGround(position, hasTerrain);
            var verdict = Rule.Evaluate(position.y, hasTerrain, terrainY, awaitingGround,
                                        parkExpired: HasFloorParkExpired());

            switch (verdict.Action)
            {
                case UnderTerrainAction.Lift:
                    if (isParked) ExitPark();
                    ForgetFloorPark();
                    Lift(b, verdict.TargetY, terrainY);
                    break;

                case UnderTerrainAction.Park:
                    // Only the below-the-floor hold is timed. A body waiting on a chunk the
                    // streamer is genuinely fetching has IsAwaitingGround's own bounded wait, and
                    // starting a second clock for it would expire the two at different moments.
                    if (awaitingGround) ForgetFloorPark();
                    else if (floorParkStartedAt < 0f) floorParkStartedAt = Time.time;

                    EnterPark(b);
                    break;

                case UnderTerrainAction.Recover:
                    if (isParked) ExitPark();
                    ForgetFloorPark();
                    Recover(b);
                    break;

                default:
                    if (isParked) ExitPark();
                    ForgetFloorPark();
                    RememberSafePosition(position, hasTerrain);
                    break;
            }
        }

        /// <summary>
        /// Has the below-the-floor hold run longer than this guard is willing to hold anything?
        ///
        /// Read from the clock the previous evaluation started rather than from this one, so the
        /// first tick of a hold always parks. That ordering is the point: a body that fell through
        /// a chunk which then loads is recovered by the ordinary lift on the next tick, and never
        /// reaches this at all.
        /// </summary>
        private bool HasFloorParkExpired() =>
            floorParkStartedAt >= 0f &&
            Time.time - floorParkStartedAt > Mathf.Max(0f, groundWaitTimeout);

        private void ForgetFloorPark() => floorParkStartedAt = -1f;

        /// <summary>
        /// Note where the body is while it is demonstrably fine — on terrain, at or above the
        /// surface. This is the only position a recovery can trust, because it is the only one the
        /// body has actually occupied without anything being wrong.
        ///
        /// Deliberately not recorded while parked, lifted, or anywhere with no terrain: an interior
        /// floor and a deck are fine places to stand and terrible places to be teleported back to
        /// from outside, and a position taken mid-recovery would remember the fault rather than the
        /// world.
        /// </summary>
        private void RememberSafePosition(Vector3 position, bool hasTerrain)
        {
            if (!hasTerrain) return;

            lastSafePosition = position;
            hasSafePosition = true;
            warnedNoRecoveryTarget = false;
        }

        /// <summary>
        /// Put a body that has fallen out of the world back into it.
        ///
        /// The hold this follows has already had its chance: <see cref="groundWaitTimeout"/>
        /// seconds of pinning the body over its own X/Z, which is all a chunk that was ever coming
        /// needs. Reaching here means nothing is coming — the body is off the grid, or over a chunk
        /// authored with no terrain — so continuing to hold is a player frozen in the void with
        /// nothing to do but quit. That is the failure this method exists to prevent, and it is
        /// worth a teleport the player did not ask for.
        ///
        /// Somewhere this body has genuinely stood is preferred over a spawn point, because it is
        /// where they were before whatever went wrong and it costs them no walk back.
        /// </summary>
        private void Recover(Transform b)
        {
            if (!TryResolveRecoveryPosition(out Vector3 target, out string source))
            {
                if (!warnedNoRecoveryTarget)
                {
                    warnedNoRecoveryTarget = true;
                    Debug.LogError(
                        $"[UnderTerrainGuard] {name} is stranded below y={absoluteFloorY} at " +
                        $"{b.position.x:F0},{b.position.z:F0} with nowhere known to put it back. " +
                        "Still holding — but nothing here will end that on its own.", this);
                }

                EnterPark(b);
                return;
            }

            Vector3 was = b.position;
            NetworkedTeleport.Move(b.gameObject, target, b.rotation);
            RecoveryCount++;

            Debug.LogWarning(
                $"[UnderTerrainGuard] {name} was stranded at {was.x:F0},{was.z:F0} below " +
                $"y={absoluteFloorY} with no ground on its way — returned to {source} at " +
                $"{target.x:F0},{target.y:F0},{target.z:F0}.", this);
        }

        private bool TryResolveRecoveryPosition(out Vector3 target, out string source)
        {
            if (hasSafePosition)
            {
                target = lastSafePosition;
                source = "the last ground it stood on";
                return true;
            }

            // Never stood anywhere measurable — a body that was already lost when this guard first
            // looked at it. A spawn point is not where they were, but it is in the world.
            if (SpawnManager.Instance != null &&
                SpawnManager.Instance.TryGetRespawnPosition(out Vector3 spawn))
            {
                target = spawn;
                source = "a spawn point";
                return true;
            }

            target = default;
            source = null;
            return false;
        }

        /// <summary>
        /// Whether the world owes ground here and has not delivered it yet.
        ///
        /// Three conditions, and each one is load-bearing:
        ///
        ///   * no terrain to sample — with a surface present there is nothing to wait for;
        ///   * inside the streamed grid, so an ornithopter flying over off-grid space, the minigame
        ///     arena and every interior are places where "no terrain" is the permanent truth rather
        ///     than a delay, and freezing a body there would be the bug this is meant to prevent;
        ///   * nothing solid underfoot, so a player standing on a ship's deck or an interior floor
        ///     inside the grid's footprint is supported and is not waiting for anything.
        ///
        /// Bounded by <see cref="groundWaitTimeout"/>, because a hold with no way out is worse than
        /// the fall it prevents. The clock starts the first tick all three agree and is dropped the
        /// moment they stop, so a body that waits, lands, and is later stranded again gets a fresh
        /// wait rather than a clock that has already run down.
        /// </summary>
        private bool IsAwaitingGround(Vector3 position, bool hasTerrain)
        {
            if (hasTerrain
                || !TerrainProbe.IsInsideStreamedWorld(position)
                || SpawnClearance.StandsOnStructure(position, structureFloorReach))
            {
                groundWaitStartedAt = -1f;
                return false;
            }

            if (groundWaitStartedAt < 0f) groundWaitStartedAt = Time.time;

            if (Time.time - groundWaitStartedAt <= Mathf.Max(0f, groundWaitTimeout)) return true;

            Debug.LogError(
                $"[UnderTerrainGuard] {name} waited {groundWaitTimeout:F0}s at " +
                $"{position.x:F0},{position.z:F0} for terrain that never loaded. Releasing it — " +
                "the absolute floor is the only failsafe left from here.", this);

            return false;
        }

        private void Lift(Transform b, float targetY, float terrainY)
        {
            Vector3 was = b.position;

            // A body on a built floor is already held up, whatever the heightmap says. Lifting it
            // to the surface outside would move it INTO that floor — the ship's cargo bay clears
            // the sand under the hull by well under a metre, so driving the ship onto rising ground
            // puts its whole interior under the terrain without anything being wrong. Same
            // exemption, and the same reason, as SpawnManager.ClampAboveTerrain.
            if (SpawnClearance.StandsOnStructure(was, structureFloorReach)) return;

            Vector3 recovered = new(was.x, targetY, was.z);

            // Through the shared placement path rather than a raw transform write. This guard is on
            // NavMeshAgent-driven bodies too (DuneRat, the patrol robots, Nomad), and an agent
            // navigates from its own position: moving only the transform leaves the agent where it
            // was and it is dragged straight back on the next frame. That path also handles the
            // CharacterController's cached position and the interpolation trap that makes a
            // teleported Rigidbody spend a frame travelling back toward where it came from.
            //
            // NetworkedTeleport rather than SaveTeleport directly so the move is addressed to
            // whoever owns the body. We are that owner here — HasAuthority saw to it — so this
            // takes the local path, and it stays correct if a body is ever guarded from elsewhere.
            NetworkedTeleport.Move(b.gameObject, recovered, b.rotation);
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

            // Two very different situations reach the same hold, and they do not deserve the same
            // noise. Waiting for a chunk is the ordinary case this guard now exists to catch — a
            // client whose player object outran its own copy of the world — and it resolves itself
            // in a moment. Sitting below the absolute floor means a body fell hundreds of metres
            // before anything noticed, which is a genuine fault worth an error.
            if (groundWaitStartedAt >= 0f)
            {
                Debug.Log(
                    $"[UnderTerrainGuard] {name} is at {parkedPosition.x:F0},{parkedPosition.z:F0} " +
                    "with no ground under it yet — holding still instead of falling through, until " +
                    "the chunk there loads.", this);
                return;
            }

            Debug.LogError(
                $"[UnderTerrainGuard] {name} fell below y={absoluteFloorY} at " +
                $"{parkedPosition.x:F0},{parkedPosition.z:F0} with no terrain to sample. Holding " +
                $"position for up to {groundWaitTimeout:F0}s in case the chunk there loads, then " +
                "putting it back on the last ground it stood on.", this);
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
