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

        [Header("Resist")]
        [Tooltip("Seconds of pulling squarely away to tear free of an end as strong as you are. " +
                 "Scales with the other end's pull, so a ship holds you far longer than a player.")]
        [SerializeField, Min(0.1f)] private float resistSeconds = 2f;

        [Tooltip("Strain given back per second when you stop pulling away.")]
        [SerializeField, Min(0f)] private float strainDecay = 0.5f;

        [Header("Tying")]
        [Tooltip("Slack added when a tie lands further away than the rope is long.")]
        [SerializeField] private float payOutMargin = 0.4f;

        [Tooltip("Hard ceiling on that paying out, so one awkward tie cannot produce a 40 m rope.")]
        [SerializeField] private float maxPaidOutLength = 16f;

        [Header("Collision")]
        [Tooltip("What the rope may bend around. STATIC GEOMETRY ONLY — every machine derives a " +
                 "rope's shape independently and nothing about it is sent, so a dynamic collider " +
                 "in this mask makes the rope a different shape on every screen.")]
        [SerializeField] private LayerMask wrapLayers = ~0;

        [Tooltip("Probe radius in metres. About the rope's own thickness.")]
        [SerializeField, Min(0.005f)] private float wrapRadius = 0.05f;

        [Tooltip("How far off a surface a bend sits, in metres.")]
        [SerializeField, Min(0.001f)] private float wrapClearance = 0.06f;

        [Tooltip("Ceiling on bends in one rope. Past it the rope stops wrapping rather than " +
                 "misbehaving — a rope with nine bends in it is a rope somebody is abusing.")]
        [SerializeField, Min(0)] private int maxWrapPoints = 8;

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
        /// A tie to something with no networked identity — a loose prop nobody opted into saving.
        ///
        /// <para>
        /// Presented only on the machine that clicked. The alternative is what this replaces: the
        /// id is 0 on the wire and <see cref="NetArg"/>'s local reference deliberately does not
        /// travel, so every peer resolved null and pinned the rope to a phantom point where the
        /// prop had been. That is worse than no rope at all — the two machines then disagree about
        /// the rope's shape, its break verdict and its identity for an untie. Such a prop's physics
        /// already differs per machine, so a shared rope to it could never have been made to agree,
        /// which is what OnRequestUse's own doc comment has always claimed happened.
        /// </para>
        /// </summary>
        private const int HitLocal = 3;

        // ── The verb and the rope's length, packed into NetArg.B ───────────────
        //
        // Both, because there is nowhere else. A is the hotbar slot — EquipmentController's
        // stale-slot guard reads it — and P is carrying the knot. So B holds the verb in its low
        // byte and the paid-out length in centimetres above it, which is the same byte packing
        // NetMsg.PackMove uses for its two surfaces.
        //
        // The length has to travel at all because a tie measured per machine is a DIFFERENT tie per
        // machine: each runs Present at its own moment, a relay apart, so a rope tied across
        // anything moving settled on a length that differed by a metre and stayed that way for
        // good. Centimetres because NetArg has no float field, the convention CraftLaunch uses.

        private const int VerbMask = 0xFF;

        private static int Encode(int verb, float paidOutLength) =>
            (verb & VerbMask) | (Mathf.Max(0, Mathf.RoundToInt(paidOutLength * 100f)) << 8);

        private static int VerbOf(int packed) => packed & VerbMask;

        /// <summary>The paid-out length in metres, or 0 for "this click starts a rope, not ends one".</summary>
        private static float LengthOf(int packed) => (packed >> 8) * 0.01f;

        /// <summary>
        /// Whether a rope may be tied to what the aim ray hit.
        ///
        /// <para>
        /// Terrain is the one refusal: a rope pinned to open ground is a fence post, and the item
        /// is for moving things. Rocks, walls and structures still anchor — only the ground itself
        /// is excluded, and <c>TerrainCollider</c> identifies it exactly, with nothing to keep in
        /// step and no layer to configure.
        /// </para>
        /// </summary>
        public static bool IsTieable(Collider hit) => hit != null && hit is not TerrainCollider;

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
            arg.B = Encode(Miss, 0f);

            if (aimProvider == null) return;

            bool aimed = aimProvider.TryGetAimHit(maxRange, out RaycastHit hit);
            float surface = aimed ? hit.distance : float.MaxValue;

            // Empty hands: a click on a rope unties it. This is the only way to get rid of a rope
            // that is tied at both ends — until it existed, a rope in the world was permanent.
            //
            // Only when empty-handed, deliberately. While a rope is in hand every click is about
            // THAT rope, and a second meaning for the same button on the same target is how a
            // control stops being predictable.
            if (held == null && TryAimAtRope(surface, ref arg)) return;

            if (!aimed || hit.collider == null) return;

            // Terrain is the one refusal. A bare return leaves arg.B on the Miss seeded at the top
            // of this method, which already means "drop what you are holding".
            if (!IsTieable(hit.collider)) return;

            if ((leashableLayers.value & (1 << hit.collider.gameObject.layer)) == 0) return;

            // Roping yourself: both as a collider under the player, and as a target that resolves
            // up to the player root through some rigging that lives outside their hierarchy.
            if (owner != null && hit.collider.transform.IsChildOf(owner.transform)) return;

            var body = hit.collider.GetComponentInParent<Rigidbody>();
            GameObject root = body != null ? body.gameObject : hit.collider.gameObject;
            if (root == owner) return;

            arg = arg.With(root);

            // The knot in the TARGET's local space, not in the world.
            //
            // Measured here because this is the only machine whose copy of a moving target is the
            // one the player actually clicked. A world point re-projected on each machine is
            // measured against that machine's interpolated pose a relay later, which on anything
            // moving puts the knot on a different part of the animal — and the rope then has a
            // different shape, a different standing stretch and a different break verdict on every
            // machine, permanently, because both are fixed once tied.
            arg.P = root.transform.InverseTransformPoint(hit.point);

            // Paid out ONCE, here, on the machine that can see both ends now. Zero when this click
            // starts a rope rather than finishing one, which leaves the authored length alone.
            float paidOut = held == null
                ? 0f
                : Mathf.Min(Vector3.Distance(held.A.Position, hit.point) + payOutMargin,
                            maxPaidOutLength);

            // A rope to something with no networked identity cannot be shared — see HitLocal. The
            // id is minted by With() above, so this is the first point at which we can tell.
            bool shareable = arg.Target != 0 || !Network.IsNetworked;

            arg.B = Encode(shareable ? Hit : HitLocal, paidOut);
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

            // Named relative to one of the rope's own ANCHORS, so the point rides whatever the rope
            // is tied to. A bare world point names nothing once that thing starts moving: the click
            // travels for a relay, and a rope on an animal running at 8 m/s has left the tolerance
            // by the time a peer looks — so the rope came off on the clicking machine alone, and
            // the server went on constraining a creature nobody could see a rope on.
            //
            // The NEARER end, because that is the one the player was looking at, and because a
            // rope's two ends can be on objects moving in different directions.
            Transform anchor = Vector3.SqrMagnitude(point - rope.A.Position) <=
                               Vector3.SqrMagnitude(point - rope.B.Position)
                ? rope.A.Anchor
                : rope.B.Anchor;

            arg = arg.With(anchor != null ? anchor.gameObject : null);
            arg.P = anchor != null ? anchor.InverseTransformPoint(point) : point;
            arg.B = Encode(Untie, 0f);
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
            int verb = VerbOf(arg.B);
            float paidOut = LengthOf(arg.B);

            if (verb == Untie)
            {
                GameObject anchorObject = arg.Resolve();
                UntieAt(anchorObject != null ? anchorObject.transform : null, arg.P);
                return;
            }

            // A rope to an unnetworked prop is the clicking machine's business alone. Every other
            // machine has no identity to resolve and would pin it to thin air — see HitLocal.
            if (verb == HitLocal)
            {
                if (!OwnerIsLocal()) return;

                GameObject local = arg.Resolve();
                if (local != null) TieTo(local, arg.P, paidOut);
                return;
            }

            // A click at nothing lets go of what you are holding. It is the same gesture as
            // throwing a rope away and needs no second key.
            if (verb != Hit)
            {
                DropHeld();
                return;
            }

            GameObject root = arg.Resolve();

            // No id resolved: either we are offline, where the local reference survives in the arg
            // and Resolve has already answered, or the endpoint is bare geometry, which has no
            // NetworkObject and needs none — the point is the anchor and it is the same point on
            // every machine.
            //
            // Note the two carry different things in P: a resolved root gets a LOCAL offset, bare
            // geometry a world point. Which is right — geometry has no local space to speak of, and
            // its point is identical everywhere by definition.
            if (root != null) TieTo(root, arg.P, paidOut);
            else PinTo(arg.P, paidOut);
        }

        // ── The two clicks ─────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="localOffset"/> is the knot in <paramref name="root"/>'s own space, and
        /// <paramref name="paidOutLength"/> the length the clicking machine settled on. Neither is
        /// measured here — see <see cref="OnRequestUse"/> for why both have to travel.
        /// </summary>
        private void TieTo(GameObject root, Vector3 localOffset, float paidOutLength)
        {
            if (held == null)
            {
                Hook(leash => leash.TieEndTo(true, root, localOffset));
                return;
            }

            // Tying a rope to the thing already on its other end would be a loop that does nothing.
            if (held.ReferencesObject(root)) return;

            held.TieHandEndOnto(root, localOffset, paidOutLength);
            held = null;

            // Read from the object rather than from `held`, which was just nulled.
            Sfx.Play(SfxId.InteractLever, root.transform.TransformPoint(localOffset));
        }

        private void PinTo(Vector3 point, float paidOutLength)
        {
            if (held == null)
            {
                Hook(leash => leash.PinEndTo(true, point));
                return;
            }

            held.PinHandEndAt(point, paidOutLength);
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
        private void UntieAt(Transform anchor, Vector3 point)
        {
            Leash rope = Leash.Nearest(anchor, point, untieTolerance);
            if (rope == null) return;

            // Read before the rope goes: Dispose releases both ends, so A.Position answers zero
            // afterwards and the sound would play at the world origin.
            Vector3 heard = rope.A.Position;

            // If this was the rope in our hand, the hand is empty now.
            if (rope == held) held = null;

            rope.Dispose();
            Sfx.Play(SfxId.InteractDrop, heard);
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
            resistSeconds = resistSeconds,
            strainDecay = strainDecay,
            wrapLayers = wrapLayers,
            wrapRadius = wrapRadius,
            wrapClearance = wrapClearance,
            maxWrapPoints = maxWrapPoints,
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

            // wrapLayers deliberately 0 on this path: with nothing to bend around the rope is a
            // straight chord, which is what it was before collision existed. A guessed mask here
            // would be a guess about which layers are static, and getting that wrong does not lose
            // the feature — it makes the rope a different shape on every machine.
            settings = new Leash.Settings
            {
                length = 8f, correction = 0.35f, maxCorrectionSpeed = 25f, maxCorrectionStep = 0.5f,
                resistSeconds = 2f, strainDecay = 0.5f,
                wrapLayers = 0, wrapRadius = 0.05f, wrapClearance = 0.06f, maxWrapPoints = 8,
                rope = new LeashRope(),
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
