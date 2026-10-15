// The half of a lasso that only the roped PLAYER's machine can run.
//
// LassoTether is the creature end: it takes an animal's legs off its AI and drives them itself. A
// player is not that. Their body is owner-authoritative — the server cannot push it, and anything
// it writes there is overwritten within a tick, silently — so a rope thrown at a player has to be
// applied by that player, on their own machine.
//
// This is what roping a player did before: the tether landed on the server, wrote a position fifty
// times a second that nobody ever saw, and the victim's machine was never told anything at all. It
// looked like a working feature to whoever was hosting, because the server does own the host's
// body, and did nothing whatsoever to a client.
//
// Nothing extra has to be sent for this to work. The catch is already broadcast to every machine
// (NetMsg.LassoRoped), and both ends of the rope are replicated transforms, so the victim's machine
// has everything it needs to compute its own half. Which is the same trick the two ends of a leash
// use, and the reason PlayerPullShare is a pure function of two masses rather than a number
// somebody puts on the wire.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// Holds a roped BODY back: a standing player on that player's own machine, or a limp one —
    /// player or creature — being dragged, on the machine that drives its ragdoll.
    ///
    /// <para>
    /// Added on demand rather than authored on the prefab, because any player can be roped at any
    /// time and the alternative is a component every player in the game carries for a case most of
    /// them never hit. Same shape and same reasoning as <see cref="LeashedBody"/> and
    /// <see cref="LassoTether"/>.
    /// </para>
    /// <para>
    /// <b>A limp body is the same rope with a different authority.</b> A hogtied captive is not
    /// moved through its root Rigidbody — the ragdoll's adapter made that kinematic, and the root
    /// follows the pelvis — but through <see cref="RagdollRig.Tow"/>, on whichever machine
    /// <see cref="RagdollRig.Drives"/> the body: the server for a creature, the owner for a player.
    /// Everything else — the binder, the length, the split by mass — is exactly the standing case,
    /// which is why it is one component and not two copies of it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    [DefaultExecutionOrder(200)] // after PlayerMovement — see LeashedBody's file header
    public sealed class LassoedBody : MonoBehaviour
    {
        private Rigidbody body;
        private RagdollRig rig;
        private Transform anchor;
        private float ropeLength;
        private float throwerMass = 80f;
        private float bodyMass = 80f;
        private bool bound;

        /// <summary>Whether a rope currently holds this player.</summary>
        public bool IsBound => bound;

        public static LassoedBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out LassoedBody existing)
                ? existing
                : player.AddComponent<LassoedBody>();
        }

        private void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>
        /// The body this holds back, resolved on demand.
        ///
        /// <para>
        /// Not trusted from <see cref="Awake"/> alone. This component is added at runtime by
        /// <see cref="Ensure"/>, and Unity does not raise Awake for an AddComponent outside play
        /// mode — so in an EditMode test, and in any editor tooling that builds a rope, the field
        /// is still null when the constraint first runs. The rope then silently held nothing.
        /// </para>
        /// </summary>
        private Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        /// <summary>The ragdoll, for when this body is limp. Resolved on demand for the reason <see cref="Body"/> is.</summary>
        private RagdollRig Rig => rig != null ? rig : rig = GetComponentInParent<RagdollRig>();

        /// <summary>
        /// Take hold. False when somebody else's rope already has this body.
        ///
        /// One rope at a time, for the reason <see cref="LassoTether.Bind"/> documents: two ropes
        /// sharing one constraint means whichever is released first frees the victim from both.
        /// </summary>
        /// <param name="heldMass">
        /// What this end weighs on the rope. Both ends compute their share from the same two numbers
        /// rather than exchanging one, so the thrower's machine must be handed the same figure.
        /// </param>
        public bool Bind(Transform ropeAnchor, float length, float ropeHolderMass, float heldMass)
        {
            if (bound && anchor != ropeAnchor) return false;

            anchor = ropeAnchor;
            ropeLength = length;
            throwerMass = ropeHolderMass;
            bodyMass = heldMass;
            bound = true;
            return true;
        }

        /// <summary>The rope was reeled in or paid out. Kept in step with the visual.</summary>
        public void SetRopeLength(float length) => ropeLength = length;

        public void Release(Transform ropeAnchor)
        {
            if (!bound || (ropeAnchor != null && ropeAnchor != anchor)) return;

            bound = false;
            anchor = null;
        }

        private void OnDestroy() => Release(anchor);

        /// <summary>
        /// Advance one step without a physics frame. The seam the EditMode tests use, and public
        /// for the same reason <see cref="LassoTether.AdvanceStruggle"/> is: the tests compile into
        /// Assembly-CSharp-Editor, which cannot see internals of Assembly-CSharp.
        /// </summary>
        public void Step()
        {
            if (!bound || anchor == null) return;

            RagdollRig limp = Rig;
            if (limp != null && limp.IsLimp)
            {
                StepLimp(limp);
                return;
            }

            Rigidbody rb = Body;
            if (rb == null) return;

            // Kinematic is a body something else is placing — a saddle, a seat — and a rope must
            // leave it alone. (A player tied up by the lasso is dragged by a joint instead: see
            // BoundBody.)
            if (rb.isKinematic) return;

            // Everyone in the session has one of these once a rope has been thrown at them; only
            // the machine that owns the body may move it. Elsewhere this player is a replica whose
            // position is somebody else's to publish.
            if (!Network.Owns(this)) return;

            Vector3 rope = rb.position - anchor.position;
            float distance = rope.magnitude;
            if (distance <= ropeLength || distance < 0.001f) return;

            Vector3 radial = rope / distance;

            float share = BodyShare;

            // Velocity first, and only ever REMOVED. A rope may take a player's speed away and it
            // may drag them; it may never give them speed, or a well-timed catch is a launch. Same
            // rule as SnaredBody.Step, and the leash rework's finding that a rope must never be a
            // way to get around. The leash now keeps that boundary structurally instead — it may
            // tow, but it has no winch, so nobody can pull themselves along one.
            float outward = Vector3.Dot(rb.linearVelocity, radial);
            if (outward > 0f) rb.linearVelocity -= radial * outward;

            // The position error is given back as a POSITION, never folded into that velocity.
            // Velocity added to close a gap is still there on the next step, which is how a solver
            // gains energy and how two roped bodies end up slamming together.
            rb.position -= radial * ((distance - ropeLength) * share);
        }

        /// <summary>
        /// How much of a taut rope's correction this end absorbs, 0 to 1.
        ///
        /// <para>
        /// COMPUTED, not sent: the thrower's machine runs the mirror of this —
        /// <see cref="LassoArtifact.PlayerPullShare"/> of this body's mass against the thrower's —
        /// and reaches the same split, which is what lets two different computers resolve the two
        /// ends of one rope without exchanging anything. The two masses must go in the same way
        /// round on both machines; this used to pass the thrower's as the target's, which only
        /// agreed because every player weighs the same.
        /// </para>
        /// </summary>
        private float BodyShare => 1f - LassoArtifact.PlayerPullShare(bodyMass, throwerMass);

        /// <summary>
        /// A limp body being dragged: haul the whole ragdoll back onto the rope by its share.
        ///
        /// Measured from the pelvis, which is where the root sits while the body is limp (see
        /// <c>RagdollRig.FollowHips</c>) and therefore where every machine draws the rope to.
        /// </summary>
        private void StepLimp(RagdollRig limp)
        {
            if (!limp.Drives || limp.Hips == null) return;

            Vector3 rope = limp.Hips.position - anchor.position;
            float distance = rope.magnitude;
            if (distance <= ropeLength || distance < 0.001f) return;

            limp.Tow(-rope / distance, (distance - ropeLength) * BodyShare);
        }

        private void FixedUpdate() => Step();
    }
}
