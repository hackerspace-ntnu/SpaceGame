// A booster clamped to something, burning.
//
// The whole item is two seconds long, and almost everything hard about it is in where the push
// lands rather than in how hard it pushes.
//
// WHY THIS DOES NOT REPARENT. A clamped booster has to ride its target on every machine, and the
// obvious way — parent the NetworkObject to the target's NetworkObject, the way a rider is seated
// on a mount — is the wrong tool here. It only works for a target that HAS a spawned NetworkObject
// (Netcode refuses any other parent outright), an unspawned NetworkObject reverts a reparent in
// silence, and the local pose still has to be replicated by hand because nothing sends a
// localPosition for you. So the pose is the replicated fact and the follow is arithmetic: every
// machine puts the booster at target.TransformPoint(local) each LateUpdate, which is correct for a
// target whose transform is authored by a locomotion solver, a flight model or a physics step
// alike. Stored LOCAL and never world, so a target that rotates carries the booster round with it.
//
// WHO PUSHES. This component decides nothing about that; it publishes what it is doing and
// BoostedBody, on the body being pushed, works out whether this machine is the one entitled to
// move it. That split is the authority rule the whole item turns on: a player's body is
// owner-authoritative, so a server-applied force on it is overwritten within a tick, silently.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The clamp and the burn. One per booster in the world; on the clamped-booster prefab beside
    /// <see cref="BoosterShell"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoosterMount : NetworkBehaviour
    {
        [Header("Model")]
        [SerializeField] private BoosterShell shell;

        [Header("Burn")]
        [Tooltip("How long the booster burns, seconds. Short on purpose: the item is one committed " +
                 "shove, not a throttle, and the whole of the skill is in where it was stuck.")]
        [SerializeField, Min(0.05f)] private float burnSeconds = 2f;

        [Tooltip("How hard it pushes, m/s². An ACCELERATION and not a force, so the feel does not " +
                 "depend on what it happens to be strapped to — a crate and a player leave at the " +
                 "same rate. Converted to a force against the target's own mass where a force is " +
                 "what the body takes, which is also what makes an off-centre clamp spin it.\n\n" +
                 "Against this world's 18 m/s² of gravity, 40 leaves 22 of climb: two seconds of " +
                 "burn and the coast that follows put a player about 95 m up, leaving the ground " +
                 "at 44 m/s. That is well past the 34 m/s the crash curve calls lethal, so a rider " +
                 "who does not arrange a landing — a jetpack, a wingsuit, deep sand — dies on the " +
                 "way down. It was 26.5 while the item was tuned to stay just under that; it is a " +
                 "launcher now, and the fall is the price.")]
        [SerializeField, Min(0f)] private float thrustAcceleration = 40f;

        [Header("Towing")]
        [Tooltip("How far ahead of a towed machine the booster hangs its anchor, metres. A tow is " +
                 "a pull TOWARDS a point, so a booster asks to be pulled at something out along " +
                 "its own axis. Far enough that a two-second burn never arrives, or the machine " +
                 "would let go halfway through.")]
        [SerializeField, Min(1f)] private float towAnchorDistance = 60f;

        [Header("Impact")]
        [Tooltip("What arriving somewhere at speed costs whatever the booster was pushing.")]
        [SerializeField] private BoosterImpactConfig impact = new BoosterImpactConfig();

        [Tooltip("Seconds after the burn ends during which an impact is still the booster's fault. " +
                 "Most of the ride happens after the motor has stopped, so a window that closed " +
                 "with the burn would make every crash that mattered free.")]
        [SerializeField, Min(0f)] private float impactWindowSeconds = 6f;

        [Header("Husk")]
        [Tooltip("Seconds the spent booster lies where it fell before it is taken away. It is " +
                 "scenery, not loot: a one-charge booster that could be picked up again would not " +
                 "be a one-charge booster.")]
        [SerializeField, Min(0f)] private float spentLingerSeconds = 6f;

        /// <summary>
        /// Everything the clamp is, in one replicated value.
        ///
        /// <para>
        /// One <c>NetworkVariable</c> rather than five, because they are written once, together,
        /// and are useless apart: a machine holding the pose but not yet the target would draw a
        /// booster at the world origin, and one holding the target but not the deadline would burn
        /// forever. One value means one write and one read.
        /// </para>
        /// <para>
        /// <b>The burn is a DEADLINE on the server's clock, not a countdown.</b> A remaining-time
        /// float would have to be written every frame and would still hand a joiner a fresh two
        /// seconds of their own; a deadline is written once and every machine reads its own copy of
        /// the shared clock against it.
        /// </para>
        /// </summary>
        private struct Strap : INetworkSerializable, IEquatable<Strap>
        {
            /// <summary>
            /// Is this booster clamped at all? The one flag that says the rest of this value is
            /// worth reading.
            ///
            /// <para>
            /// It cannot be inferred from <see cref="Target"/>, because a booster stuck to the
            /// world — terrain, a wall, a chunk rock — has no target and is clamped all the same.
            /// A zero Target used to mean "not clamped yet", and reusing it would make every
            /// world clamp invisible to every other machine.
            /// </para>
            /// </summary>
            public bool Attached;

            /// <summary>
            /// NetworkObjectId of the body this is clamped to, or 0 for a clamp on the world
            /// itself. Only meaningful once <see cref="Attached"/> is set.
            /// </summary>
            public ulong Target;

            /// <summary>NetworkObjectId of whoever lit it, for damage attribution. 0 for nobody.</summary>
            public ulong Igniter;

            /// <summary>
            /// The booster's origin in the target's LOCAL space — never world. For a clamp on the
            /// world there is no local space to be in, so this is the world position itself.
            /// </summary>
            public Vector3 LocalPosition;

            /// <summary>
            /// The booster's rotation relative to the target's, or its world rotation for a clamp
            /// on the world.
            /// </summary>
            public Quaternion LocalRotation;

            /// <summary>When the burn ends, on the SERVER's clock.</summary>
            public double BurnEndsAt;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Attached);
                serializer.SerializeValue(ref Target);
                serializer.SerializeValue(ref Igniter);
                serializer.SerializeValue(ref LocalPosition);
                serializer.SerializeValue(ref LocalRotation);
                serializer.SerializeValue(ref BurnEndsAt);
            }

            public bool Equals(Strap other) =>
                Attached == other.Attached
                && Target == other.Target
                && Igniter == other.Igniter
                && LocalPosition.Equals(other.LocalPosition)
                && LocalRotation.Equals(other.LocalRotation)
                && BurnEndsAt.Equals(other.BurnEndsAt);
        }

        private readonly NetworkVariable<Strap> netStrap = new();

        // Held here as well as on the wire, on purpose, and for the reason NetArg keeps an
        // unserialized localTarget beside its network id: the wire copy cannot work offline (there
        // is no NetworkObject to name and no spawn to write through), and the resolved transforms
        // cannot cross a machine. The server and the offline path fill these directly; a peer fills
        // them from the wire, once, in AdoptClamp.
        private Strap strap;
        private Transform target;
        private Transform igniter;

        private bool clamped;
        private bool spent;
        private float despawnAt = -1f;

        private Vector3 exhaustAxis;
        private Rigidbody body;
        private Collider[] colliders;
        private BoostedBody boosted;

        /// <summary>How hard this booster is pushing, m/s². Read by <see cref="BoostedBody"/>.</summary>
        public float Acceleration => thrustAcceleration;

        /// <summary>See <see cref="towAnchorDistance"/>.</summary>
        public float TowAnchorDistance => towAnchorDistance;

        /// <summary>See <see cref="impact"/>.</summary>
        public BoosterImpactConfig Impact => impact;

        /// <summary>See <see cref="impactWindowSeconds"/>.</summary>
        public float ImpactWindowSeconds => impactWindowSeconds;

        /// <summary>Who lit it, for damage attribution. Null on a machine that was never told.</summary>
        public Transform Igniter => igniter;

        /// <summary>Is this booster still burning?</summary>
        public bool IsBurning => clamped && !spent;

        /// <summary>
        /// Where the push is applied, in world space — the clamp itself, taken live off the target's
        /// current pose rather than off this transform, which is only written once a frame.
        ///
        /// <para>
        /// A booster clamped to the world has no target to be measured against, so the pose it was
        /// given IS the world pose. Before any clamp lands, wherever the prefab was spawned.
        /// </para>
        /// </summary>
        public Vector3 AttachPoint =>
            target != null ? target.TransformPoint(strap.LocalPosition)
                           : clamped ? strap.LocalPosition
                                     : transform.position;

        /// <summary>
        /// Which way it pushes: back down its own axis, into the surface it is clamped to. The
        /// exhaust leaves the muzzle, so the thrust goes the other way — which is why a booster on
        /// the underside of a crate flies it and one on the top drives it into the ground.
        /// </summary>
        public Vector3 ThrustDirection
        {
            get
            {
                Vector3 world = ClampRotation * exhaustAxis;
                return world.sqrMagnitude > 1e-8f ? -world.normalized : Vector3.zero;
            }
        }

        private Quaternion ClampRotation =>
            target != null ? target.rotation * strap.LocalRotation
                           : clamped ? strap.LocalRotation
                                     : transform.rotation;

        /// <summary>
        /// The clock every machine agrees on. Server time while there is a session — which there is
        /// even in single-player, a host of one — so a joiner who arrives mid-burn gets the time
        /// that is actually left rather than a fresh two seconds of its own.
        /// </summary>
        private static double Now
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening
                    ? manager.ServerTime.Time
                    : Time.timeAsDouble;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(includeInactive: true);

            if (shell == null) return;

            exhaustAxis = shell.LocalExhaustAxis;
            shell.Hide();
        }

        /// <summary>
        /// Strap it on. The deciding machine's call, once, immediately after the booster is spawned.
        /// </summary>
        /// <param name="strapTo">
        /// What it rides — see <see cref="BoosterClamp.BodyFor"/>. <b>Null clamps it to the
        /// world</b>: terrain, a settlement wall, a chunk rock. It burns where it was stuck and
        /// pushes nothing, which is the honest outcome of aiming at something that does not move.
        /// </param>
        /// <param name="worldPosition">Where the booster's origin ended up.</param>
        /// <param name="worldRotation">How it is seated — see <see cref="BoosterClamp.Seat"/>.</param>
        /// <param name="ignitedBy">Who lit it, for attribution when the ride kills somebody.</param>
        public void Clamp(Transform strapTo, Vector3 worldPosition, Quaternion worldRotation,
                          GameObject ignitedBy)
        {
            if (clamped) return;

            target = strapTo;
            igniter = ignitedBy != null ? ignitedBy.transform : null;

            strap = new Strap
            {
                Attached = true,
                Target = strapTo != null ? NetArg.IdOf(strapTo.gameObject) : 0ul,
                Igniter = NetArg.IdOf(ignitedBy),

                // LOCAL, never world, whenever there is a local space to be in. A target that turns
                // has to carry the booster round with it, and a world pose recorded once would
                // leave the booster hanging in the air pushing at a direction the target stopped
                // facing a second ago. The world itself does not turn, so a world clamp stores the
                // world pose in the same two fields.
                LocalPosition = strapTo != null
                    ? strapTo.InverseTransformPoint(worldPosition)
                    : worldPosition,
                LocalRotation = strapTo != null
                    ? Quaternion.Inverse(strapTo.rotation) * worldRotation
                    : worldRotation,
                BurnEndsAt = Now + burnSeconds,
            };

            clamped = true;

            // Skipped entirely when there is no session to publish into, which is the offline case
            // the fields above already cover on their own.
            if (IsSpawned && IsServer) netStrap.Value = Publishable(worldPosition, worldRotation);

            Begin();
        }

        /// <summary>
        /// The clamp as the rest of the session can read it.
        ///
        /// <para>
        /// A body with no spawned <see cref="NetworkObject"/> — a crate authored into a chunk
        /// scene, an interior prop — cannot be NAMED on the wire, so no peer could follow it even
        /// though this machine can. The pose is republished in WORLD space in that case, so every
        /// other machine at least draws the booster and its flame where it actually is rather than
        /// treating a body-local offset as a world position and hanging it near the origin.
        /// </para>
        /// </summary>
        private Strap Publishable(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (strap.Target != 0 || target == null) return strap;

            Strap wire = strap;
            wire.LocalPosition = worldPosition;
            wire.LocalRotation = worldRotation;
            return wire;
        }

        public override void OnNetworkSpawn()
        {
            // Read on spawn as well as polled below, because a joiner is handed the current value
            // and never the change that produced it. The server has usually not published by this
            // point — it spawns and then clamps — so this is the late joiner's path.
            if (!IsServer) AdoptClamp();
        }

        public override void OnNetworkDespawn()
        {
            // The booster is being taken away, from under a body that may well outlive it.
            Detach();
        }

        private void OnDisable()
        {
            // Nothing here touches the target's transform or its hierarchy: this only takes the
            // booster off the list of things pushing that body. Reparenting anything from OnDisable
            // is how a rider ends up unparented mid-teardown, and Netcode skips deparenting during
            // shutdown anyway.
            Detach();
        }

        private void LateUpdate()
        {
            if (!clamped)
            {
                AdoptClamp();
                return;
            }

            if (target == null && strap.Target != 0)
            {
                // Whatever it was strapped to has gone — despawned, destroyed, streamed out. There
                // is nothing left to ride. A clamp on the WORLD has no target and never had one, so
                // it is not caught here: it stays where it was stuck and burns out on its clock.
                Extinguish();
            }
            else if (!spent)
            {
                // The follow, on every machine. LateUpdate so the target has already been moved by
                // whatever owns it this frame — a physics step, a flight model, a legged solver.
                transform.SetPositionAndRotation(AttachPoint, ClampRotation);

                if (Now >= strap.BurnEndsAt) Extinguish();
            }

            if (spent && despawnAt >= 0f && Time.time >= despawnAt) Despawn();
        }

        /// <summary>
        /// Take the clamp off the wire. Retried every frame until it lands, because the booster's
        /// own spawn can beat the arrival of the object it is strapped to.
        /// </summary>
        private void AdoptClamp()
        {
            if (clamped) return;

            Strap wire = netStrap.Value;
            if (!wire.Attached) return;

            // A named target has to have ARRIVED before the clamp can be adopted; a world clamp
            // names nobody and is adopted the moment it is published.
            GameObject named = wire.Target != 0 ? new NetArg(wire.Target).Resolve() : null;
            if (wire.Target != 0 && named == null) return;

            strap = wire;
            target = named != null ? named.transform : null;

            GameObject lighter = new NetArg(wire.Igniter).Resolve();
            igniter = lighter != null ? lighter.transform : null;

            clamped = true;
            Begin();
        }

        /// <summary>
        /// The clamp has landed on this machine: seat it, light it, and tell the body it is being
        /// pushed. Runs on every machine, because every machine draws the burn — and because
        /// exactly one of them will turn out to own the body, and it does not find that out here.
        /// </summary>
        private void Begin()
        {
            transform.SetPositionAndRotation(AttachPoint, ClampRotation);

            // Out of every physics query for as long as it is clamped, and deliberately: a booster
            // strapped to a crate is part of the crate. A live collider there would put a second
            // body inside the one it is riding, and would answer the next player's aim ray with the
            // booster instead of the thing it is stuck to.
            SetCollidersEnabled(false);
            if (body != null) body.isKinematic = true;

            // Only a body can be pushed. A booster on the world burns against something that is
            // never going anywhere, so there is nothing to attach it to.
            if (target != null)
            {
                boosted = BoostedBody.Ensure(target.gameObject);
                if (boosted != null) boosted.Attach(this);
            }

            if (shell != null)
            {
                shell.SetSpent(false);
                shell.CloseJaw();
            }

            // A joiner who arrives after the burn is already over gets the husk, not a flame that
            // lasts one frame.
            if (Now >= strap.BurnEndsAt)
            {
                Extinguish();
                return;
            }

            if (shell != null) shell.Ignite();
        }

        /// <summary>
        /// The burn is over. Every machine: the lamp goes out, the exhaust stops and the clamp lets
        /// go. Idempotent, because more than one thing can end a burn.
        /// </summary>
        private void Extinguish()
        {
            if (spent) return;
            spent = true;

            if (shell != null)
            {
                shell.Cut();
                shell.SetSpent(true);
            }

            Detach();
            Release();

            despawnAt = Time.time + spentLingerSeconds;
        }

        private void Detach()
        {
            if (boosted == null) return;

            boosted.Detach(this);
            boosted = null;
        }

        /// <summary>
        /// Drop it. The husk goes back to being an ordinary loose body, carrying the speed the point
        /// it was clamped to was travelling at — a booster that let go of a moving crate and stopped
        /// dead in the air would read as it having been deleted and replaced.
        ///
        /// <para>
        /// Every machine simulates its own husk. It is cosmetic for a few seconds, it holds nothing
        /// anybody can act on, and replicating it would mean a <c>NetworkTransform</c> that spent
        /// the whole burn fighting the follow above.
        /// </para>
        /// </summary>
        private void Release()
        {
            Vector3 carried = Vector3.zero;

            if (target != null && target.TryGetComponent(out Rigidbody carrier) && !carrier.isKinematic)
                carried = carrier.GetPointVelocity(transform.position);

            SetCollidersEnabled(true);

            // Not against the thing it was strapped to. The husk is still inside that body's
            // collider at the instant it lets go, and a solver handed two overlapping bodies
            // answers by firing one of them across the desert.
            if (target != null) IgnoreCollisionsWith(target.gameObject);

            if (body == null) return;

            body.isKinematic = false;
            body.linearVelocity = carried;
        }

        private void Despawn()
        {
            despawnAt = -1f;

            // One machine takes it away and the rest are told. Offline that is this machine; in a
            // session it is the server, and a client's copy goes with the replicated despawn.
            if (Network.Simulates(this)) GameServices.World.Despawn(gameObject);
        }

        private void SetCollidersEnabled(bool value)
        {
            if (colliders == null) return;

            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = value;
        }

        private void IgnoreCollisionsWith(GameObject other)
        {
            if (colliders == null) return;

            Collider[] theirs = other.GetComponentsInChildren<Collider>();

            // Both colliders have to be live for this to take: Unity refuses the pair with a warning
            // otherwise, and the husk's own set includes whichever model is currently switched off.
            foreach (Collider mine in colliders)
            {
                if (mine == null || !mine.enabled || !mine.gameObject.activeInHierarchy) continue;

                foreach (Collider mask in theirs)
                {
                    if (mask == null || !mask.enabled || !mask.gameObject.activeInHierarchy) continue;

                    Physics.IgnoreCollision(mine, mask, true);
                }
            }
        }
    }
}
