// The half of a net that only the netted PLAYER's machine can run.
//
// SnareTether is the creature end: it hobbles an animal by writing its transform. A player is not
// that. Their body is owner-authoritative — the server cannot push it, and anything it writes there
// is overwritten within a tick, silently — so a net thrown at a player has to be applied by that
// player, on their own machine.
//
// Nothing extra has to be sent for this to work. The catch is already broadcast to every machine,
// and both ends are replicated transforms, so the victim's machine has everything it needs to
// compute its own half. The same trick the two ends of a leash use.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Holds a netted player back, on that player's own machine.
    ///
    /// <para>
    /// Skipping this is the exact failure the lasso had: the constraint landed on the server, wrote
    /// a position fifty times a second that nobody ever saw, and the victim's machine was never told
    /// anything at all. It looked like a working feature to whoever was hosting, because the server
    /// does own the host's body, and did nothing whatsoever to a client.
    /// </para>
    /// <para>
    /// <b>Why <see cref="Network.Owns"/> here and <see cref="Network.Simulates"/> in
    /// <see cref="SnareTether"/>.</b> The two files sit side by side with opposite gates and that is
    /// deliberate, not an inconsistency. A creature is an AI body the SERVER simulates, so the
    /// server is the machine entitled to move it and <c>Simulates</c> is the question. A player is
    /// owner-authoritative: the only machine whose writes survive is the one that owns the body, and
    /// <c>Simulates</c> would answer true on the server for every remote player's replica — which is
    /// precisely the case that produced fifty writes a second nobody saw.
    /// </para>
    /// <para>
    /// Added on demand rather than authored on the prefab, because any player can be netted at any
    /// time. Same shape and reasoning as <see cref="LassoedBody.Ensure"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    [DefaultExecutionOrder(200)] // after PlayerMovement — see LeashedBody's file header
    public sealed class SnaredBody : MonoBehaviour
    {
        private Rigidbody body;
        private Transform anchor;
        private SnareStruggle settings;
        private bool bound;

        public bool IsBound => bound;

        public static SnaredBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out SnaredBody existing)
                ? existing
                : player.AddComponent<SnaredBody>();
        }

        private void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>
        /// The body this holds back, resolved on demand.
        ///
        /// Not trusted from <see cref="Awake"/> alone: this component is added at runtime, and
        /// Unity does not raise Awake for an AddComponent outside play mode — so in an EditMode
        /// test the field is still null when the constraint first runs, and the net silently holds
        /// nothing. The same trap <see cref="LassoedBody"/> documents.
        /// </summary>
        private Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        /// <summary>Take hold. False when another net already has this player.</summary>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && anchor != netAnchor) return false;

            anchor = netAnchor;
            settings = struggleSettings ?? new SnareStruggle();
            bound = true;
            return true;
        }

        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != anchor)) return;

            bound = false;
            anchor = null;
        }

        private void OnDisable()
        {
            if (bound) Release(anchor);
        }

        /// <summary>
        /// Advance one step without a physics frame. The seam the EditMode tests use.
        ///
        /// <para>
        /// <b>The net may take speed away and it may drag; it may never give speed.</b> Otherwise a
        /// well-timed catch is a launch, and the net becomes a way to get around — the finding the
        /// leash rework paid for, and the rule <c>LeashEnd.Restrain</c> and
        /// <see cref="LassoedBody.Step"/> both state. Every branch below holds to it: the only
        /// write to velocity REMOVES a component of it, which cannot lengthen the vector, and the
        /// positional correction is a teleport that does not touch velocity at all. That stays true
        /// however the anchor itself is moving, because the anchor's motion only ever changes which
        /// direction is radial — never the sign of the removal.
        /// </para>
        /// </summary>
        public void Step()
        {
            Rigidbody rb = Body;
            if (!bound || anchor == null || rb == null || rb.isKinematic) return;

            // Everyone in the session has one of these once a net has landed on this player; only
            // the machine that owns the body may move it. Elsewhere this player is a replica whose
            // position is somebody else's to publish.
            if (!Network.Owns(this)) return;

            Vector3 rope = rb.position - anchor.position;
            rope.y = 0f;

            float distance = rope.magnitude;
            if (distance <= settings.ShuffleRadius || distance < 0.001f) return;

            Vector3 radial = rope / distance;

            // Velocity first, and only ever REMOVED. Removing the outward component is a projection,
            // so the result can never be longer than what went in.
            float outward = Vector3.Dot(rb.linearVelocity, radial);
            if (outward > 0f) rb.linearVelocity -= radial * outward;

            // The position error is given back as a POSITION, never folded into that velocity.
            // Velocity added to close a gap is still there on the next step, which is how a solver
            // gains energy and how a netted player ends up slingshotting.
            rb.position -= radial * (distance - settings.ShuffleRadius);
        }

        private void FixedUpdate() => Step();
    }
}
