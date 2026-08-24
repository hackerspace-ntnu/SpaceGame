using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>What one end of a rope is tied to, which decides how it can be pulled.</summary>
    public enum LeashEndKind
    {
        /// <summary>A player's hand. Their own machine resolves it — see <see cref="LeashedBody"/>.</summary>
        PlayerHand,

        /// <summary>A world object with a body. Force if it is dynamic, a nudge if it is kinematic.</summary>
        Object,

        /// <summary>Bare geometry — a wall, the ground. Anchors and never moves.</summary>
        Static,
    }

    /// <summary>
    /// One end of a leash: the knot, and everything needed to pull on it.
    ///
    /// <para>
    /// A class rather than two sets of <c>a*</c>/<c>b*</c> fields on the rope, which is what this
    /// replaces. Every operation a rope performs it performs twice, once per end, and with the ends
    /// flattened that meant every method took six parallel parameters and every configuration
    /// helper existed in an A copy and a B copy that had already drifted apart.
    /// </para>
    /// </summary>
    public class LeashEnd
    {
        /// <summary>How this end resolves. See <see cref="LeashEndKind"/>.</summary>
        public LeashEndKind Kind { get; private set; } = LeashEndKind.Static;

        /// <summary>What the knot is tied to. Null once that thing has been destroyed.</summary>
        public Transform Anchor { get; private set; }

        /// <summary>Where the knot sits on <see cref="Anchor"/>, in its local space.</summary>
        public Vector3 LocalOffset { get; private set; }

        /// <summary>The body to push, when there is one.</summary>
        public Rigidbody Body { get; private set; }

        /// <summary>Set when the body is steered by an agent, which must be moved through its own API.</summary>
        public NavMeshAgent Agent { get; private set; }

        /// <summary>The marker on the leashed object, so it can find its ropes. Null for hand ends.</summary>
        public LeashAttachable Attachable { get; private set; }

        /// <summary>
        /// The live muzzle on an equipped leash artifact, for a hand end.
        ///
        /// <para>
        /// Tracked separately from <see cref="Anchor"/>, which stays the player root, because the
        /// artifact prefab is destroyed on every hot-swap and the rope must outlive that. While the
        /// muzzle exists the knot is drawn at the hand; when it goes the knot falls back to the
        /// baked offset on the body, which is roughly where the hand was.
        /// </para>
        /// </summary>
        public Transform Muzzle { get; set; }

        /// <summary>A stand-in this end created for bare geometry, and is therefore responsible for.</summary>
        private GameObject ownedAnchor;

        /// <summary>Whether the thing this end was tied to still exists.</summary>
        public bool IsAlive => Anchor != null;

        /// <summary>
        /// Whether this end is tied to a player, and therefore not the server's to push.
        ///
        /// <para>
        /// Asked of the BODY rather than of <see cref="Kind"/>, because a player can be on the far
        /// end of a rope too — one player roping another. Keyed on the tag for the same reason the
        /// spawn-clearance check is: the player capsule sits on layer 0, so no mask can pick it out,
        /// and PlayerCharacter is the only prefab in the project carrying the tag.
        /// </para>
        /// </summary>
        public bool IsPlayer =>
            Kind == LeashEndKind.PlayerHand || (Body != null && Body.CompareTag("Player"));

        /// <summary>Where the knot is right now.</summary>
        public Vector3 Position
        {
            get
            {
                if (Muzzle != null) return Muzzle.position;
                return Anchor != null ? Anchor.TransformPoint(LocalOffset) : Vector3.zero;
            }
        }

        public Vector3 Velocity => Body != null ? Body.linearVelocity : Vector3.zero;

        /// <summary>
        /// How hard this end resists being moved, as the constraint shares out the error.
        ///
        /// <para>
        /// Mass, because it comes off the prefab and is therefore the same number on every machine —
        /// which is what lets the two machines resolving the two ends agree on the split without
        /// exchanging anything. An end that cannot move is infinitely heavy, so the other end does
        /// all the work.
        /// </para>
        /// </summary>
        public float Mass => Body != null ? Mathf.Max(0.01f, Body.mass) : Mathf.Infinity;

        /// <summary>Whether pulling on this end can move anything at all.</summary>
        public bool CanMove => Body != null;

        // ── Tying ──────────────────────────────────────────────────────────────

        /// <summary>Tie this end to a world object at a point on its surface.</summary>
        public void TieTo(GameObject targetRoot, Vector3 worldHitPoint, Leash rope)
        {
            Release(rope);

            var body = targetRoot.GetComponentInParent<Rigidbody>();
            Transform root = body != null ? body.transform : targetRoot.transform;

            Kind = body != null ? LeashEndKind.Object : LeashEndKind.Static;
            Anchor = root;
            Body = body;
            Agent = root.GetComponentInParent<NavMeshAgent>();
            LocalOffset = root.InverseTransformPoint(worldHitPoint);

            Attachable = LeashAttachable.GetOrAdd(root.gameObject);
            Attachable.AddLeash(rope);

            // Roping another player. Their body is theirs, so the pull has to be applied on their
            // machine and after their own movement has run — exactly as for the end in a hand.
            if (IsPlayer) LeashedBody.Ensure(root.gameObject);
        }

        /// <summary>
        /// Tie this end into a player's hand.
        ///
        /// <para>
        /// The anchor is the player ROOT rather than the muzzle, because the muzzle belongs to an
        /// artifact prefab that dies on every hot-swap and would take the rope with it. The muzzle
        /// is tracked alongside so the knot is still drawn in the hand while it exists.
        /// </para>
        /// </summary>
        public void TieToHand(GameObject playerRoot, Transform muzzle, Leash rope)
        {
            Release(rope);

            Kind = LeashEndKind.PlayerHand;
            Anchor = playerRoot.transform;
            Body = playerRoot.GetComponentInParent<Rigidbody>();
            Agent = null;
            Muzzle = muzzle;
            Attachable = null;

            LocalOffset = muzzle != null
                ? playerRoot.transform.InverseTransformPoint(muzzle.position)
                : Vector3.zero;

            LeashedBody.Ensure(playerRoot);
        }

        /// <summary>Tie this end to a place rather than a thing, and own the stand-in that represents it.</summary>
        public void PinTo(Vector3 worldPoint, Leash rope)
        {
            Release(rope);

            ownedAnchor = new GameObject("LeashAnchor");
            ownedAnchor.transform.position = worldPoint;

            Kind = LeashEndKind.Static;
            Anchor = ownedAnchor.transform;
            LocalOffset = Vector3.zero;
            Body = null;
            Agent = null;
            Attachable = null;
        }

        /// <summary>Restore-only: tie to an object at a knot offset that was recorded, not measured.</summary>
        public void RestoreOnto(GameObject root, Vector3 localOffset, bool held, Leash rope)
        {
            if (held) TieToHand(root, null, rope);
            else TieTo(root, root.transform.TransformPoint(localOffset), rope);

            LocalOffset = localOffset;
        }

        /// <summary>Let go of whatever this end held, including any stand-in it created.</summary>
        public void Release(Leash rope)
        {
            if (Attachable != null) Attachable.RemoveLeash(rope);
            Attachable = null;

            if (ownedAnchor != null) Object.Destroy(ownedAnchor);
            ownedAnchor = null;

            Anchor = null;
            Body = null;
            Agent = null;
            Muzzle = null;
        }

        // ── Resolution ─────────────────────────────────────────────────────────

        /// <summary>
        /// Is this end mine to move?
        ///
        /// <para>
        /// A player's body is theirs and nobody else's — their transform is owner-authoritative, so
        /// anything another machine writes into it is overwritten within the tick, silently. That is
        /// the rule this whole split exists to respect. Everything else belongs to the server, or to
        /// the only machine there is when offline.
        /// </para>
        /// </summary>
        public bool ResolvedHere =>
            IsPlayer ? Body != null && Network.Owns(Body) : !Network.IsNetworked || Network.Server;

        /// <summary>
        /// Pull this end toward the other one.
        ///
        /// <paramref name="toward"/> is the unit direction to the other end, <paramref name="speed"/>
        /// the velocity change the constraint has decided this end owes, already shared out and
        /// clamped by the rope.
        /// </summary>
        /// <summary>
        /// Apply the rope's pull to a player's velocity in the one way a leash is allowed to: it may
        /// take speed away, and it may never give any.
        ///
        /// <para>
        /// <b>This is what stops the leash being a grappling hook.</b> The arrest term normally only
        /// cancels motion that is opening the gap, but the gap can also open because the OTHER end
        /// is leaving — a vehicle driving off, a walker breaking into a run — and then the same term
        /// reads as a tow and would happily accelerate the player along the rope. Fire it at
        /// something fast, or at the right moment on a swing, and that is a launch.
        /// </para>
        /// <para>
        /// So the result is capped at the speed the body already had. A leash can still drag a
        /// player — the position correction moves them, and being towed behind a vehicle looks and
        /// works exactly as it should — but they arrive carrying no speed the rope gave them, so
        /// cutting it or letting go never flings anyone anywhere.
        /// </para>
        /// </summary>
        public static Vector3 Restrain(Vector3 velocity, Vector3 toward, float arrestSpeed)
        {
            if (arrestSpeed <= 0f) return velocity;

            Vector3 pulled = velocity + toward * arrestSpeed;

            float before = velocity.magnitude;
            return pulled.magnitude > before ? pulled.normalized * before : pulled;
        }

        public void Pull(Vector3 toward, float arrestSpeed, float correctionDistance)
        {
            if (Body == null) return;
            if (arrestSpeed <= 0f && correctionDistance <= 0f) return;

            Vector3 step = toward * correctionDistance;

            if (Body.isKinematic)
            {
                // A kinematic body has no velocity to take off, so the position correction is the
                // whole of it. An agent must be moved through its OWN API: MovePosition fights the
                // agent's position writes and loses, and Warp — what this used to do, fifty times a
                // second — re-projects onto the NavMesh and resets navigation state, which is
                // precisely the teleport-jitter every leashed creature had.
                if (correctionDistance <= 0f) return;

                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh) Agent.Move(step);
                else Body.MovePosition(Body.position + step);
                return;
            }

            if (IsPlayer)
            {
                // No torque for a player: their capsule is upright by construction and spinning it
                // at the knot would tip the camera over.
                Body.linearVelocity = Restrain(Body.linearVelocity, toward, arrestSpeed);
                Body.position += step;
                return;
            }

            // Non-player. No restraint clamp here on purpose: a crate SHOULD be flung by a rope
            // that a vehicle is towing. It is only the player who must never gain speed from one.

            // At the KNOT, not the centre of mass. This is what makes a crate roped by one corner
            // turn to face the pull instead of sliding flat — the torque is the whole difference
            // between a dragged object and a skidding one.
            if (arrestSpeed > 0f)
                Body.AddForceAtPosition(toward * (arrestSpeed * Body.mass), Position, ForceMode.Impulse);

            // Written as a position rather than folded into that impulse, deliberately. Correcting a
            // position error with velocity leaves the velocity behind on the next step, which is how
            // a solver gains energy and how two roped objects end up slamming together.
            Body.position += step;
        }
    }
}
