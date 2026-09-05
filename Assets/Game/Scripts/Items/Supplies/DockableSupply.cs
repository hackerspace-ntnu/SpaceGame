using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// A reservoir the player carries in order to plug it into something: an oxygen tank, a
    /// battery.
    ///
    /// <para>
    /// <b>It has no use verb, deliberately.</b> A tank does nothing in your hand and nothing
    /// anywhere on the pack except its socket; a battery does nothing until it is fitted to a
    /// machine. Both of their verbs belong to the receptacle they are put into, which is
    /// right-clicked (<c>OxygenGeneratorDock</c>, and the pack's own socket). Until 2026-09-04 a
    /// charged tank could also be breathed straight from the hand, which was a second path to the
    /// same outcome with a different and unexplainable waste rule — using a full tank on a suit
    /// that holds one minute threw away twenty-nine of them.
    /// </para>
    /// <para>
    /// The class would exist even with no verb at all, because <see cref="UsableItem.OnEquipped"/>
    /// is what gives an item its hold pose: an item with no <c>UsableItem</c> equips perfectly and
    /// then stands in the idle tree with a cylinder floating in a slack fist.
    /// </para>
    /// <para>
    /// <b>Its charge is a FRACTION on the instance, not an item identity.</b> It used to be an
    /// identity — <c>OxygenTank</c> and <c>OxygenTankEmpty</c> were two assets — and
    /// <see cref="SupplyCharge"/> records why that was right then and why it cannot survive a tank
    /// the player reads to a percent. The capacity the fraction is OF lives here, on the prefab, so
    /// a second tank type is a second prefab and no saved number changes meaning.
    /// </para>
    /// </summary>
    public class DockableSupply : UsableItem
    {
        [Header("Reservoir")]
        [Tooltip("What this holds. A receptacle built for the other kind refuses it.")]
        [SerializeField] private SupplyKind kind = SupplyKind.Oxygen;

        [Tooltip("A full one, in this kind's own unit: SECONDS of breathing for oxygen, " +
                 "WATT-HOURS for power. The player never sees this number — every readout is a " +
                 "percentage of it — so it is free to differ between tank types.")]
        [SerializeField, Min(1f)] private float capacity = 1800f;

        [Tooltip("How full one of these is when it first enters the world, 0..1. A battery is " +
                 "stocked full and a tank empty, because an empty tank is what the plant is for.")]
        [SerializeField, Range(0f, 1f)] private float startingCharge = 1f;

        [Header("Gauge")]
        [Tooltip("The emissive gauge or charge ladder. Optional.")]
        [SerializeField] private Renderer readout;

        [Tooltip("Which submesh of that renderer is the emissive one. -1 paints all of them.")]
        [SerializeField] private int readoutMaterialIndex = EmissiveLamp.WholeRenderer;

        [SerializeField] private Color chargedColour = new Color(0.35f, 1f, 0.45f);

        [Tooltip("What the gauge reads at empty. Not black: an unlit CRT is dark GLASS, and a " +
                 "black one reads as a hole in the object.")]
        [SerializeField] private Color emptyColour = new Color(0.06f, 0.09f, 0.07f);

        /// <summary>
        /// How full THIS instance is, 0..1. Lives here only while the item exists as an object;
        /// the container it came from holds the truth between equips (see <see cref="SupplyCharge"/>).
        /// </summary>
        private float charge01;

        /// <summary>What this holds.</summary>
        public SupplyKind Kind => kind;

        /// <summary>A full one, in this kind's own unit.</summary>
        public float Capacity => capacity;

        /// <summary>How full one of these enters the world.</summary>
        public float StartingCharge => startingCharge;

        /// <summary>How full this one is, 0..1.</summary>
        public float Charge => charge01;

        /// <summary>How full this one is, in the kind's own unit.</summary>
        public float Stored => charge01 * capacity;

        /// <summary>The emissive part, so a dock can paint the copy of this item standing in it.</summary>
        public Renderer Readout => readout;

        /// <summary>Which submesh of <see cref="Readout"/> is emissive.</summary>
        public int ReadoutMaterialIndex => readoutMaterialIndex;

        /// <summary>Gauge colour at full.</summary>
        public Color ChargedColour => chargedColour;

        /// <summary>Gauge colour at empty.</summary>
        public Color EmptyColour => emptyColour;

        /// <summary>The gauge colour for a charge in 0..1. Shared so a dock's animation matches.</summary>
        public Color ColourAt(float charge) =>
            Color.Lerp(emptyColour, chargedColour, Mathf.Clamp01(charge));

        private void Awake() => SetCharge(startingCharge);

        /// <summary>
        /// Set how full this one is and repaint its gauge. Clamped, because every caller is either
        /// draining or filling by a delta and an unclamped one would let a tank read 103%.
        /// </summary>
        public void SetCharge(float charge)
        {
            charge01 = Mathf.Clamp01(charge);
            PaintReadout(charge01);
        }

        /// <summary>
        /// Show <paramref name="charge"/> on the gauge without changing what this holds. Called by
        /// a dock on the inert display copy standing in it while a tank fills — that copy has no
        /// scripts of its own, so the generator paints it through the PREFAB's component.
        /// </summary>
        public void PaintReadout(float charge) =>
            EmissiveLamp.Paint(readout, readoutMaterialIndex, ColourAt(charge));

        /// <summary>
        /// Nothing. The reservoir's verb belongs to the receptacle it is plugged into — see the
        /// class summary. <c>Use</c> is abstract on <see cref="UsableItem"/>, so this is the
        /// shape "no verb" takes rather than an omission.
        /// </summary>
        protected override void Use() { }

        // ── Per-instance state ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            SupplyCharge.Write(state, charge01);
        }

        /// <inheritdoc/>
        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            // A bag with no charge in it is an item that has never been through a container which
            // knows about charges — a fresh spawn, or a save written before this system existed.
            // Both should read as the authored starting charge rather than as empty, because an
            // item that silently arrives at 0% is indistinguishable from one the player drained.
            float stored = SupplyCharge.Read(state);
            SetCharge(stored < 0f ? startingCharge : stored);
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) PaintReadout(startingCharge);
        }
    #endif
    }
}
