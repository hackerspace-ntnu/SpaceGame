using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Characters;
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

        /// <summary>
        /// The machine this end is tied to, when it is one that would rather be ASKED than pushed.
        ///
        /// <para>
        /// A mounted rider's body is kinematic and parented into a seat, so writing velocity to it
        /// achieves nothing whatsoever — exactly what a hook fired from an ornithopter's cradle
        /// used to achieve. The vehicle owns what a pull costs it; the rope owns where the far end
        /// is tied. Anything that cannot usefully be hauled simply does not implement the
        /// interface, and a rope tied to it hangs slack, which is the honest answer.
        /// </para>
        /// </summary>
        public ITowable Towable { get; private set; }

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

        /// <summary>
        /// The best speed the thing on this end can manage under its own power, in m/s.
        ///
        /// <para>
        /// Resolved in order of specificity, because a ridden walker carries both a legged
        /// locomotion and a driver and the locomotion is the one that owns the figure. A crate
        /// answers zero and therefore tows nothing, which is correct: it has no engine.
        /// </para>
        /// </summary>
        public float TopSpeed
        {
            get
            {
                if (Anchor == null) return 0f;

                var motor = Anchor.GetComponentInParent<IMovementMotor>();
                if (motor != null) return Mathf.Max(0f, motor.TopSpeed);

                var movement = Anchor.GetComponentInParent<PlayerMovement>();
                if (movement != null) return Mathf.Max(0f, movement.SprintSpeed);

                return 0f;
            }
        }

        /// <summary>How hard this end can haul. See <see cref="Leash.PullOf"/>.</summary>
        public float PullStrength => Leash.PullOf(Mass, TopSpeed);

        /// <summary>Whether pulling on this end can move anything at all.</summary>
        public bool CanMove => Body != null;

        // ── Tying ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Tie this end to a world object at a knot offset in that object's own local space.
        ///
        /// <para>
        /// An OFFSET rather than a world point, deliberately. The point was measured on the
        /// clicking machine and arrives here a relay later, by which time a moving target has
        /// moved — so re-projecting it against this machine's interpolated pose puts the knot
        /// somewhere the player never clicked, and somewhere different on every machine. Both the
        /// rope's shape and its break verdict follow from that number, and it is fixed once tied.
        /// See LeashArtifact.OnRequestUse.
        /// </para>
        /// </summary>
        public void TieTo(GameObject targetRoot, Vector3 localOffset, Leash rope)
        {
            Release(rope);

            // Tying to somebody in a saddle takes them out of it. Until they are down their
            // transform belongs to the animal carrying them, so the rope pulls on something that
            // cannot move — and the offset below would be measured against a mount that is about to
            // walk away with it. See NpcPassenger.UnseatRider.
            NpcPassenger.UnseatRider(targetRoot);

            var body = targetRoot.GetComponentInParent<Rigidbody>();
            Transform root = body != null ? body.transform : targetRoot.transform;

            Kind = body != null ? LeashEndKind.Object : LeashEndKind.Static;
            Anchor = root;
            Body = body;
            Agent = root.GetComponentInParent<NavMeshAgent>();
            Towable = root.GetComponentInParent<ITowable>();

            // Taken, not measured. The caller resolved it against the copy the player actually
            // clicked; this machine's copy is somewhere else by now.
            LocalOffset = localOffset;

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

            // A player in a saddle is carried by something towable, but the rope is in their HAND
            // — it pulls on them, not on the machine under them. That end is the far one's job.
            Towable = null;
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
            Towable = null;
            Attachable = null;
        }

        /// <summary>Restore-only: tie to an object at a knot offset that was recorded, not measured.</summary>
        public void RestoreOnto(GameObject root, Vector3 localOffset, bool held, Leash rope)
        {
            // No TransformPoint round trip any more: TieTo takes the offset directly, so a restore
            // and a fresh tie now travel the same path with the same units.
            if (held) TieToHand(root, null, rope);
            else TieTo(root, localOffset, rope);

            LocalOffset = localOffset;
        }

        /// <summary>Let go of whatever this end held, including any stand-in it created.</summary>
        public void Release(Leash rope)
        {
            if (Attachable != null) Attachable.RemoveLeash(rope);
            Attachable = null;

            // Leash.Remove rather than Object.Destroy: this stand-in is a bare runtime GameObject
            // and is torn down from EditMode tests and editor tooling too, where Destroy is refused.
            if (ownedAnchor != null) Leash.Remove(ownedAnchor);
            ownedAnchor = null;

            Anchor = null;
            Body = null;
            Agent = null;
            Towable = null;
            Muzzle = null;
        }

        // ── Resolution ─────────────────────────────────────────────────────────

        /// <summary>
        /// Is this end mine to move?
        ///
        /// <para>
        /// Ownership, and only ownership. A transform is written by the machine that owns its
        /// NetworkObject, and anything another machine writes into it is overwritten within the
        /// tick, silently — which is the rule this whole split exists to respect.
        /// </para>
        /// <para>
        /// The owner is NOT always the server, and that is what the earlier
        /// <c>Network.Server</c> test here got wrong. A ridden mount belongs to its RIDER —
        /// <c>MountNetworkSync</c> hands ownership over so the motion replicates outward from
        /// them — so the server's pulls on it were thrown away while the rider's machine declined
        /// to resolve an end it did not consider its own. The rope held a host-ridden animal and
        /// was inert against a client-ridden one. A player answers the same question the same way,
        /// so the two branches collapse into one.
        /// </para>
        /// <para>
        /// Anything with no spawned NetworkObject is owned by every machine, which is right: each
        /// then resolves its own unshared copy, and single-player keeps working.
        /// </para>
        /// </summary>
        public bool ResolvedHere =>
            Network.Owns(Body != null ? (Component)Body : Anchor);

        /// <summary>
        /// Pull this end toward the other one.
        ///
        /// <paramref name="toward"/> is the unit direction to the other end, and
        /// <paramref name="arrestSpeed"/> and <paramref name="correctionDistance"/> are what the
        /// constraint has decided this end owes, already shared out and clamped by the rope.
        /// </summary>
        /// <param name="towCap">
        /// The fastest this end may be DRAGGED, in m/s — <see cref="Leash.TowCap"/>, which is the
        /// spare pull of whatever is winning the contest divided by this end's mass. Infinity for
        /// an end that is winning or evenly matched, since nothing is towing it.
        /// </param>
        public void Pull(Vector3 toward, float arrestSpeed, float correctionDistance, float towCap)
        {
            if (Body == null && Towable == null) return;
            if (arrestSpeed <= 0f && correctionDistance <= 0f) return;

            // Force over mass, applied as the ceiling on how far this end may be carried in one
            // step. This is what replaces Restrain: the rope may now ADD speed to the end that is
            // losing the contest, but only as fast as the winner's spare pull can shift its mass.
            correctionDistance = Mathf.Min(correctionDistance, towCap * Time.fixedDeltaTime);

            Vector3 step = toward * correctionDistance;

            if (Towable != null)
            {
                // Ask, do not push. RequestTow returning false means the tow is over -- arrived,
                // out of energy, or no longer under way -- and the rope should stop asking.
                if (!Towable.RequestTow(Position + step)) Towable = null;
                return;
            }

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
                // No clamp on the result any more. A rope may now tow a player who is standing
                // still, which is the whole feature -- and it is safe without Restrain because
                // there is still no winch anywhere in this system, so a player has no way to pull
                // THEMSELVES along a rope. Something else must do the dragging.
                Body.linearVelocity += toward * arrestSpeed;
                Body.position += step;
                return;
            }

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
