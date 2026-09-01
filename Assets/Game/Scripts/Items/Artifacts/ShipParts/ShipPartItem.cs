using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Vehicles;

namespace SpaceGame.Items
{
    /// <summary>
    /// A hull module in the player's hands: an engine, a reactor core, an intake. Carried out of
    /// the desert, hauled home, and fitted into the hole it came from.
    ///
    /// <para>
    /// Server authority, because a socket is shared world state: two players pointing the same
    /// kind of motor at the same empty mount must produce one fitted motor and one player still
    /// holding theirs.
    /// </para>
    /// <para>
    /// The fitted module is not spawned. It already exists, hidden, inside the ship prefab — see
    /// <see cref="ShipPartSocket"/> — so nothing here goes in the network prefab list on that
    /// account, and the change reaches peers and late joiners alike through
    /// <see cref="ShipPartRack"/>'s replicated mask rather than through this use.
    /// </para>
    /// </summary>
    public class ShipPartItem : ToolItem
    {
        /// <summary>"Nothing was aimed at." Not a socket index, and never treated as one.</summary>
        private const int NoSocket = -1;

        [Header("Module")]
        [Tooltip("Which mount this fits. Mirrored sockets share a kind, so one motor fits either side.")]
        [SerializeField] private ShipPartKind kind;

        [Header("Fitting")]
        [Tooltip("How far away a socket can be fitted. Generous on purpose: hull modules sit high " +
                 "on a 30 m ship and some of them cannot be reached on foot at all.")]
        [SerializeField, Min(1f)] private float installRange = 30f;

        [Tooltip("How far a socket's own volume is grown for the aim test, in metres. Forgiveness, " +
                 "not assistance — it is fixed rather than adaptive so the aim stays learnable.")]
        [SerializeField, Min(0f)] private float aimMargin = 0.5f;

        [Tooltip("How far away empty sockets are lit red. Wide enough to read a wrecked hull from " +
                 "across the dune you found the part on.")]
        [SerializeField, Min(1f)] private float ghostRange = 120f;

        [Tooltip("Played on every machine when a module actually goes in. Silence on a miss is " +
                 "deliberate — a clunk for pointing at the sky reads as a bug.")]
        [SerializeField] private SfxId fittedSoundId = SfxId.InteractWorkstationRepair;

        private readonly ShipPartHighlighter highlighter = new();

        private bool highlighting;

        public ShipPartKind Kind => kind;

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            // Only the machine whose camera this is may paint. A peer's copy of a remote player's
            // module would otherwise light up that peer's own view of the hull.
            highlighting = OwnerIsLocal();
        }

        public override void OnUnequipped(GameObject holder)
        {
            StopHighlighting();
            base.OnUnequipped(holder);
        }

        /// <summary>
        /// The item outlives no scene, but it does get destroyed by paths that never call
        /// <see cref="OnUnequipped"/> — a scene load, a death teardown. The sockets it painted
        /// belong to a ship that survives all of those, so they would stay red forever.
        /// </summary>
        private void OnDisable() => StopHighlighting();

        private void Update()
        {
            if (!highlighting) return;

            Transform aim = aimProvider != null ? aimProvider.AimTransform : null;
            if (aim == null) return;

            highlighter.Refresh(kind, new Ray(aim.position, aim.forward),
                                ghostRange, installRange, aimMargin);
        }

        /// <summary>
        /// Owner-side, before the request leaves — the only machine with an honest aim, since a
        /// peer's copy of this player has an AimProvider with no live camera behind it.
        ///
        /// <para>
        /// The socket index rides <c>B</c>. <c>A</c> is left exactly as EquipmentController set
        /// it: it is the hotbar slot, and the server's stale-slot guard reads it back.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = NoSocket;

            if (!highlighting || highlighter.AimedRack == null) return;

            arg.B = highlighter.AimedIndex;
            arg = arg.With(highlighter.AimedRack);
        }

        /// <summary>
        /// Refuse a use that would fit nothing, so a shot at the sky never costs the module.
        ///
        /// <para>
        /// This is the guard that matters: <see cref="UsableItem.TryUse"/> counts the use whether
        /// or not <see cref="Use"/> found anything to do, and at <c>maxUses = 1</c> that count is
        /// what deletes the module from the hotbar. Deciding it here means a missed press is a
        /// no-op rather than a part quietly thrown away.
        /// </para>
        /// </summary>
        protected override bool CanUse() => base.CanUse() && Target() != null;

        /// <summary>
        /// Authority only: fit the module.
        ///
        /// <para>
        /// <see cref="CanUse"/> has already asked whether there is a target, but it is asked again
        /// through <see cref="Target"/> here rather than cached, because a re-entrant host dispatch
        /// can land two uses in one frame and <see cref="ShipPartRack.TryInstall"/> refusing the
        /// second is what stops one socket eating two modules.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            ShipPartRack rack = Target();
            if (rack != null) rack.TryInstall(UseArg.B, kind);
        }

        /// <summary>
        /// Every machine. The hull change itself arrives through the rack's replicated mask, so
        /// all this owes the moment is its noise — and only when something actually went in.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.B == NoSocket) return;

            Sfx.Play(fittedSoundId, transform.position, default, GetInstanceID());
        }

        /// <summary>
        /// The rack this use names, if it will still take the module. Re-asked rather than
        /// remembered: between the owner pressing and the server reading, another player may have
        /// fitted the very socket this one is aimed at.
        /// </summary>
        private ShipPartRack Target()
        {
            if (UseArg.B == NoSocket) return null;

            GameObject subject = UseArg.Resolve();
            if (subject == null) return null;

            var rack = subject.GetComponentInParent<ShipPartRack>();

            return rack != null && rack.Accepts(UseArg.B, kind) ? rack : null;
        }

        private void StopHighlighting()
        {
            if (!highlighting) return;

            highlighter.Clear();
            highlighting = false;
        }
    }
}
