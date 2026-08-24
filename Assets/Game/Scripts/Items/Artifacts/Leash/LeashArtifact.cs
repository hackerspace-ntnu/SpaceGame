using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The leash — a rope you can tie between any two things in the world.
    ///
    /// <para>
    /// Hook, then hook. Click something and the rope runs from it to your hand; click anything else
    /// and it is tied. Click nothing, or press the drop key, and you let go. Two clicks tie anything
    /// to anything, in either order.
    /// </para>
    /// <para>
    /// That replaces a flow where clicking a FRESH object always started a new rope and only an
    /// already-leashed one could be tied to — so joining two fresh objects took three clicks in a
    /// particular order, you could be holding any number of ropes at once, and nothing on screen
    /// told you which. The rule was learnable and nobody could have guessed it.
    /// </para>
    /// </summary>
    public class LeashArtifact : ToolItem
    {
        /// <summary>
        /// Aimed by the holder. Where the rope is SIMULATED is decided per end inside
        /// <see cref="Leash"/> — a player's end on their own machine, everything else on the
        /// server — so the authority here governs only the aim, which is genuinely this item's:
        /// it is the machine with the camera.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        [Header("Targeting")]
        [Tooltip("How far you can throw a knot.")]
        [SerializeField] private float maxRange = 30f;

        [Tooltip("Must not include the player's own layer, or you will rope yourself.")]
        [SerializeField] private LayerMask leashableLayers = ~0;

        [Header("Rope")]
        [Tooltip("How much rope there is. Fixed — a rope does not grow just because you tied it " +
                 "across a wide gap.")]
        [SerializeField] private float length = 8f;

        [Tooltip("Fraction of the remaining overstretch each end gives back per physics step. " +
                 "One resolves the whole error in a single step, which is a visible jolt.")]
        [SerializeField, Range(0.05f, 1f)] private float correction = 0.35f;

        [Tooltip("Ceiling on the velocity one step may take off an end, in m/s.")]
        [SerializeField] private float maxCorrectionSpeed = 25f;

        [Tooltip("Ceiling on how far one step may move an end, in metres. This is what stops a " +
                 "teleported or streamed-in endpoint dragging whatever it is tied to across the map.")]
        [SerializeField] private float maxCorrectionStep = 0.5f;

        [Header("Breaking")]
        [Tooltip("Metres past its length the rope tolerates before it breaks. Zero is unbreakable.")]
        [SerializeField] private float breakStretch = 2.5f;

        [Tooltip("How long that overstretch must last. A momentary spike is not a break.")]
        [SerializeField] private float breakTime = 0.35f;

        [Header("Tying")]
        [Tooltip("Slack added when a tie lands further away than the rope is long.")]
        [SerializeField] private float payOutMargin = 0.4f;

        [Tooltip("Hard ceiling on that paying out, so one awkward tie cannot produce a 40 m rope.")]
        [SerializeField] private float maxPaidOutLength = 16f;

        [Header("Visuals")]
        [SerializeField] private LeashRope rope = new();

        [Tooltip("Where the rope leaves your hand. Without this the knot sits at the player's " +
                 "origin, which is between their feet.")]
        [SerializeField] private Transform muzzle;

        [Header("Untying")]
        [Tooltip("How close the aim has to pass to a rope to count as pointing at it, in metres. " +
                 "A rope is 5 cm wide, so some forgiveness is the difference between a control and " +
                 "a nuisance.")]
        [SerializeField] private float grabRadius = 0.4f;

        [Tooltip("How near a rope must pass to the clicked point for another machine to agree it " +
                 "is the same rope. Generous: the two machines drew it from replicated endpoints " +
                 "and differ by centimetres, not metres.")]
        [SerializeField] private float untieTolerance = 1f;

        /// <summary>
        /// The rope in this player's hand, or null. One at a time, deliberately.
        ///
        /// <para>
        /// Not a list. Multiple simultaneous held ropes were possible before and had no display,
        /// no way to choose between them, and a drop key that removed whichever happened to be last.
        /// </para>
        /// </summary>
        private Leash held;

        // ── Aiming ─────────────────────────────────────────────────────────────

        private const int Miss = 0;
        private const int Hit = 1;
        private const int Untie = 2;

        /// <summary>
        /// Owner side: aim, and put the answer in the message.
        ///
        /// <para>
        /// The raycast happens here and only here, because this is the one machine with the camera
        /// that aimed it. A peer re-running it would trace from its own view and rope something
        /// else — or, on the host, rope whatever the host happens to be looking at.
        /// </para>
        /// <para>
        /// <see cref="NetArg.Target"/> carries the object when it is a spawned NetworkObject and
        /// <see cref="NetArg.P"/> always carries the world hit point. Between them every endpoint
        /// that CAN agree across machines is addressable: static geometry by its point, which is the
        /// same point everywhere, and networked objects by id. A dynamic prop nobody networked is
        /// neither, and ropes to it stay local — its physics already differs per machine, so a shared
        /// rope to it could not have been made to agree anyway.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            base.OnRequestUse(ref arg);
            arg.B = Miss;

            if (aimProvider == null) return;

            RaycastHit? aimed = aimProvider.GetRayCast(maxRange);
            float surface = aimed.HasValue ? aimed.Value.distance : float.MaxValue;

            // Empty hands: a click on a rope unties it. This is the only way to get rid of a rope
            // that is tied at both ends — until it existed, a rope in the world was permanent.
            //
            // Only when empty-handed, deliberately. While a rope is in hand every click is about
            // THAT rope, and a second meaning for the same button on the same target is how a
            // control stops being predictable.
            if (held == null && TryAimAtRope(surface, ref arg)) return;

            if (!aimed.HasValue || aimed.Value.collider == null) return;

            RaycastHit hit = aimed.Value;
            if ((leashableLayers.value & (1 << hit.collider.gameObject.layer)) == 0) return;

            // Roping yourself: both as a collider under the player, and as a target that resolves
            // up to the player root through some rigging that lives outside their hierarchy.
            if (owner != null && hit.collider.transform.IsChildOf(owner.transform)) return;

            var body = hit.collider.GetComponentInParent<Rigidbody>();
            GameObject root = body != null ? body.gameObject : hit.collider.gameObject;
            if (root == owner) return;

            arg = arg.With(root);
            arg.P = hit.point;
            arg.B = Hit;
        }

        /// <summary>
        /// Is the player pointing at a rope, and is that rope in front of whatever solid thing the
        /// aim also hit?
        ///
        /// <para>
        /// The comparison is what stops a rope being clicked through a wall, and the
        /// <see cref="grabRadius"/> in it is what lets one lying ON the ground still be clicked —
        /// the rope and the ground it rests on are at very nearly the same distance, and without
        /// some slack the ground would win every time.
        /// </para>
        /// <para>
        /// The world point goes in <c>P</c>. That is the whole of the rope's identity over the
        /// wire: a rope has no NetworkObject and no id, but its shape comes from two replicated
        /// endpoints, so the point where it was clicked names the same rope on every machine.
        /// </para>
        /// </summary>
        private bool TryAimAtRope(float surfaceDistance, ref NetArg arg)
        {
            Ray aim = aimProvider.GetAimRay();

            Leash rope = Leash.Aimed(aim, maxRange, grabRadius, out Vector3 point, out float distance);
            if (rope == null) return false;

            if (distance > surfaceDistance + grabRadius) return false;

            arg.P = point;
            arg.B = Untie;
            return true;
        }

        /// <summary>
        /// Nothing. The rope is built by <see cref="Present"/> on every machine, and which machine
        /// resolves which END of it is decided inside <see cref="Leash"/>.
        /// </summary>
        protected override void Use() { }

        /// <summary>Every machine: act on the click the owner reported.</summary>
        protected override void Present()
        {
            NetArg arg = UseArg;

            if (arg.B == Untie)
            {
                UntieAt(arg.P);
                return;
            }

            // A click at nothing lets go of what you are holding. It is the same gesture as
            // throwing a rope away and needs no second key.
            if (arg.B != Hit)
            {
                DropHeld();
                return;
            }

            GameObject root = arg.Resolve();

            // No id resolved: either we are offline, where the local reference survives in the arg
            // and Resolve has already answered, or the endpoint is bare geometry, which has no
            // NetworkObject and needs none — the point is the anchor and it is the same point on
            // every machine.
            if (root != null) TieTo(root, arg.P);
            else PinTo(arg.P);
        }

        // ── The two clicks ─────────────────────────────────────────────────────

        private void TieTo(GameObject root, Vector3 point)
        {
            if (held == null)
            {
                Hook(leash => leash.TieEndTo(true, root, point));
                return;
            }

            // Tying a rope to the thing already on its other end would be a loop that does nothing.
            if (held.ReferencesObject(root)) return;

            held.TieHandEndOnto(root, point, payOutMargin, maxPaidOutLength);
            held = null;
            Sfx.Play(SfxId.InteractLever, point);
        }

        private void PinTo(Vector3 point)
        {
            if (held == null)
            {
                Hook(leash => leash.PinEndTo(true, point));
                return;
            }

            held.PinHandEndAt(point, payOutMargin, maxPaidOutLength);
            held = null;
            Sfx.Play(SfxId.InteractLever, point);
        }

        /// <summary>Start a rope: one end wherever <paramref name="tieFarEnd"/> puts it, the other in the hand.</summary>
        private void Hook(System.Action<Leash> tieFarEnd)
        {
            if (owner == null) return;

            Leash leash = Leash.Create(RopeSettings);
            tieFarEnd(leash);
            leash.TieEndToHand(false, owner, muzzle);

            held = leash;
            Sfx.Play(SfxId.InteractPickup, leash.A.Position);
        }

        private void DropHeld()
        {
            if (held == null) return;

            held.Dispose();
            held = null;
        }

        /// <summary>
        /// Untie the rope the owner clicked, on this machine's own copy of it.
        ///
        /// <para>
        /// Found by the point rather than by an id, because a rope has no id to send — see
        /// <see cref="Leash.Nearest"/>. A machine that does not have this rope (it was tied to a
        /// prop nobody networked) simply finds nothing, which is the same answer it gives to every
        /// other question about that rope.
        /// </para>
        /// </summary>
        private void UntieAt(Vector3 point)
        {
            Leash rope = Leash.Nearest(point, untieTolerance);
            if (rope == null) return;

            // If this was the rope in our hand, the hand is empty now.
            if (rope == held) held = null;

            rope.Dispose();
            Sfx.Play(SfxId.InteractDrop, point);
        }

        // ── Settings ───────────────────────────────────────────────────────────

        /// <summary>
        /// This artifact's rope tuning, as the shared factory takes it.
        ///
        /// <para>
        /// Also what a load builds ropes from — see <see cref="TryResolveSettings"/>. A rope is a
        /// runtime <c>new GameObject</c> with a material reference in it, and a save file can carry
        /// neither, so the settings come from the prefab that would have made it.
        /// </para>
        /// </summary>
        public Leash.Settings RopeSettings => new()
        {
            length = length,
            correction = correction,
            maxCorrectionSpeed = maxCorrectionSpeed,
            maxCorrectionStep = maxCorrectionStep,
            breakStretch = breakStretch,
            breakTime = breakTime,
            rope = rope,
        };

        /// <summary>
        /// The rope tuning to rebuild a saved leash with, read off the leash item's own prefab.
        ///
        /// <para>
        /// The registry rather than a serialized reference on the saver: the item table already
        /// holds every <c>InventoryItem</c> in the build together with the prefab it equips, so the
        /// authored numbers and — the part nothing else can supply — the rope MATERIAL are reachable
        /// with no second asset to wire up and keep in step. Falls back to a plain rope if the leash
        /// item has been removed from the build, which draws something visible rather than nothing.
        /// </para>
        /// </summary>
        public static bool TryResolveSettings(out Leash.Settings settings)
        {
            foreach (InventoryItem item in Registry<InventoryItem>.All)
            {
                if (item == null || item.itemPrefab == null) continue;

                var artifact = item.itemPrefab.GetComponent<LeashArtifact>();
                if (artifact == null) continue;

                settings = artifact.RopeSettings;
                return true;
            }

            settings = new Leash.Settings
            {
                length = 8f, correction = 0.35f, maxCorrectionSpeed = 25f, maxCorrectionStep = 0.5f,
                breakStretch = 2.5f, breakTime = 0.35f, rope = new LeashRope(),
            };
            return false;
        }

        // ── Upkeep ─────────────────────────────────────────────────────────────

        // There is deliberately no second key here, and there used to be.
        //
        // `dropAction` was an InputActionReference read in Update, which is wrong twice over. Update
        // runs on EVERY copy of this artifact on this machine — including the ones in other players'
        // hands — and an InputActionReference reads local input, so pressing the key dropped every
        // remote player's rope on your screen and never dropped yours on theirs. It also bypassed
        // Use/Present entirely, which is the only channel an item has that reaches other machines.
        //
        // Clicking at nothing already means "let go", and it goes through that channel. One button.

        /// <summary>
        /// Unequipping drops the rope in your hand. Ropes tied at both ends are world objects and
        /// are none of this artifact's business.
        /// </summary>
        private void OnDestroy() => DropHeld();
    }
}
