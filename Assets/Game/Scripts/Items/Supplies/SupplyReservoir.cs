using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How full a thing is: the tank inside an item, as a fraction of its own capacity, with the
    /// drain-and-refill policy that empties and restores it and the bar that draws it.
    ///
    /// <para>
    /// <b>A reservoir is state, not a verb.</b> That separation is the whole reason this component
    /// exists. It used to be inseparable from <see cref="DockableSupply"/>, which is a
    /// <see cref="UsableItem"/> — and <c>EquipmentController.Equip</c>,
    /// <c>BodyEquipmentController</c> and <c>PickupableItem</c> all resolve the held item with a
    /// single <c>GetComponent&lt;UsableItem&gt;</c>, so a tank and a tool could not sit on one
    /// prefab root: whichever component was added first won, arbitrarily. Six artifacts carry a
    /// tank, so the answer is composition rather than a deeper class tree
    /// (<c>GDC-L1-ARCH-0002</c>): the reservoir is a part an item <i>has</i>, and the verb — a
    /// sprayer's trigger, or a dockable supply's deliberate absence of one — is a separate part
    /// beside it.
    /// </para>
    /// <para>
    /// <b>The value is a FRACTION 0..1, never a quantity</b>, quantised to a byte on the wire and
    /// saved under one key. <see cref="SupplyCharge"/> owns all three and says why.
    /// </para>
    /// <para>
    /// <b>Every machine runs this same clock.</b> The drain is a pure function of the hold stream,
    /// which reaches the owner, the server and every peer, so all of them reach the same fraction
    /// within a frame of each other — the argument <c>LaserStaffArtifact</c> already makes for its
    /// recharge, and the reason no charge has to travel per tick. What does travel is the value
    /// itself, once per change, through the hotbar slot's charge byte: an item whose prefab carries
    /// a reservoir is one <see cref="SupplyCharge.Carries"/> answers true for, so a client's copy
    /// equips at the server's fill rather than at the authored one.
    /// </para>
    /// <para>
    /// <b>Running dry is the item's only real cost</b>, so the shape of that cost is player-facing
    /// and lives here rather than in each consumer: a drain rate, a refill rate, a delay before the
    /// refill starts, and a restart threshold. Without the last of those a tank that has just run
    /// dry refills by a frame's worth, allows a frame of use, empties again, and the effect strobes
    /// for as long as the button is down — which is the frustrating end of the scarcity dial rather
    /// than the tense one (<c>GDC-L1-ECON-0002</c>). The reading is on the object, in the player's
    /// hand, so the cost can be watched rather than guessed at (<c>GDC-L1-SYS-0006</c>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SupplyReservoir : MonoBehaviour, IItemStateCarrier
    {
        [Header("Reservoir")]
        [Tooltip("What this holds. A receptacle built for another kind refuses it.")]
        [SerializeField] private SupplyKind kind = SupplyKind.Oxygen;

        [Tooltip("A full one, in this kind's own unit: SECONDS of breathing for oxygen, " +
                 "WATT-HOURS for power, SECONDS of continuous use for a reagent. The player never " +
                 "sees this number — every readout is a percentage of it — so it is free to differ " +
                 "between item types.")]
        [SerializeField, Min(1f)] private float capacity = 1800f;

        [Tooltip("How full one of these is when it first enters the world, 0..1. A battery is " +
                 "stocked full and a tank empty, because an empty tank is what the plant is for.")]
        [SerializeField, Range(0f, 1f)] private float startingCharge = 1f;

        [Header("Gauge")]
        [Tooltip("The mesh carrying the gauge. Not painted directly -- the fill bar is built over " +
                 "it -- but it is still what says WHERE the gauge is, which is how a dock derives " +
                 "the roll that turns the readable face towards the room. Leave empty on an item " +
                 "that docks nowhere.")]
        [SerializeField] private Renderer readout;

        [Header("Drain")]
        [Tooltip("Fraction of the reservoir spent per second of continuous use. The reciprocal is " +
                 "the seconds of use a full one buys, which is the number to tune against — 0.15 " +
                 "is about six and a half seconds. Zero for a reservoir nothing draws from by " +
                 "holding it: a bottle and a battery are emptied by the machine they are fitted " +
                 "to, not by being carried.")]
        [SerializeField, Min(0f)] private float drainPerSecond;

        [Header("Refill")]
        [Tooltip("Fraction regained per second once the refill has started. 0.06 is a full tank " +
                 "in about eighteen seconds of not firing. Zero for a reservoir the world refills " +
                 "rather than the item itself.")]
        [SerializeField, Min(0f)] private float refillPerSecond;

        [Tooltip("Seconds of not drawing before the refill starts. Without a delay the tank tops " +
                 "up between two hold ticks and the drain never reads as a cost at all.")]
        [SerializeField, Min(0f)] private float refillDelay;

        [Tooltip("How full this must be before a NEW draw may start, 0..1.\n\n" +
                 "This is hysteresis and it is not decoration. Without it a tank that has just run " +
                 "dry refills by a frame's worth, allows a frame of use, empties again, and the " +
                 "effect strobes on and off for as long as the button is down. It is also the " +
                 "smallest burst worth handing back: running out is the item's only real cost, so " +
                 "it has to read as an interruption rather than as a stutter (GDC-L1-ECON-0002). " +
                 "Zero means no hysteresis, for an item that sputters back to life instead.")]
        [SerializeField, Range(0f, 1f)] private float restartFraction;

        /// <summary>
        /// How full THIS instance is, 0..1. Lives here only while the item exists as an object; the
        /// container it came from holds the truth between equips (see <see cref="SupplyCharge"/>).
        /// </summary>
        private float charge01;

        /// <summary>Seconds since the last frame this was drawn from. Gates the refill.</summary>
        private float idleSeconds;

        /// <summary>
        /// The fill the bar was last painted at, quantised. Repainting is a transform write and a
        /// material property block, and the player reads the gauge to a whole percent — so a
        /// repaint that cannot change a pixel is skipped. The byte is
        /// <see cref="SupplyCharge.ToByte"/>'s, which is already finer than the readout.
        /// </summary>
        private byte painted;
        private bool everPainted;

        private SupplyGauge gauge;
        private bool bound;

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

        /// <summary>
        /// Which mesh carries the gauge, so a dock can work out which way round to seat this item.
        /// The bar itself is found by name through <see cref="SupplyGauge"/>, not through here.
        /// </summary>
        public Renderer Readout => readout;

        /// <summary>
        /// May a new draw start? False when empty, and false while one that ran dry is still below
        /// <see cref="restartFraction"/>. Ask before igniting, never while running.
        /// </summary>
        public bool CanStart => charge01 > 0f && charge01 >= Mathf.Min(restartFraction, 1f);

        /// <summary>
        /// The reservoir on <paramref name="root"/>, or null. The ONE rule for finding one, shared
        /// by everything that asks — the prefab lookup behind <see cref="SupplyCharge.Carries"/>,
        /// the held item's state bag, the world pickup and the drop. Two call sites answering this
        /// differently is a charge written into a bag the wire then refuses to carry, which is the
        /// exact failure this component was extracted to end.
        /// </summary>
        public static SupplyReservoir On(GameObject root) =>
            root != null ? root.GetComponentInChildren<SupplyReservoir>(true) : null;

        private void Awake() => SetCharge(startingCharge);

        /// <summary>
        /// Set how full this one is and repaint its gauge. Clamped, because every caller is either
        /// draining or filling by a delta and an unclamped one would let a tank read 103%.
        /// </summary>
        public void SetCharge(float charge)
        {
            charge01 = Mathf.Clamp01(charge);

            byte quantised = SupplyCharge.ToByte(charge01);
            if (everPainted && quantised == painted) return;

            painted = quantised;
            everPainted = true;
            Gauge.Paint(charge01);
        }

        /// <summary>
        /// One frame of the reservoir: drain while <paramref name="drawing"/>, refill after
        /// <see cref="refillDelay"/> of not drawing.
        ///
        /// <para>
        /// Called every frame whether or not the item is in use, because the refill is the half
        /// that runs when nothing is happening. Returns whether it actually delivered — a false
        /// answer while <paramref name="drawing"/> is the moment the item has to switch itself off.
        /// </para>
        /// <para>
        /// A held trigger on an empty reservoir neither drains nor refills. Refilling under one
        /// would hand back a sliver every frame and spend it on the same frame, so the item would
        /// sputter for ever instead of running out.
        /// </para>
        /// </summary>
        public bool Tick(float deltaTime, bool drawing)
        {
            if (drawing)
            {
                idleSeconds = 0f;

                if (charge01 <= 0f) return false;

                SetCharge(charge01 - drainPerSecond * deltaTime);
                return true;
            }

            idleSeconds += deltaTime;

            if (idleSeconds >= refillDelay && charge01 < 1f)
                SetCharge(charge01 + refillPerSecond * deltaTime);

            return false;
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // Under SupplyCharge.StateKey, the one key a charge has ever been written under. A second
        // key for "the same number, on an item that also has a verb" is what the old ArtifactTank
        // carried, and it cost exactly what a second key always costs: the hotbar wire, the pack
        // placement and the save codec all gate on SupplyCharge, so the artifacts writing the other
        // key replicated nothing and lost their fill on a stow. One key, one gate, one meaning
        // (GDC-L1-ARCH-0006 — a serialized id is permanent, so there had better be only one).
        //
        // Nothing misreads an older save: a bag belongs to one slot holding one item, Inventory.SetItem
        // clears it when that item changes, and the only items that have ever written this key are
        // reservoirs. A bottle's saved 43% is read back by a bottle's reservoir, exactly as before.

        /// <inheritdoc/>
        public void CaptureItemState(ItemState state) => SupplyCharge.Write(state, charge01);

        /// <inheritdoc/>
        public void RestoreItemState(ItemState state)
        {
            // An item arriving in the hand has not just been fired, whatever the last instance was
            // doing when it was destroyed. Starting the refill delay spent rather than fresh is
            // what stops a hotbar scroll from costing a second and a half of trickle.
            idleSeconds = refillDelay;

            // A bag with no charge in it is an item that has never been through a container which
            // knows about charges — a fresh spawn, or a save written before this system existed.
            // Both read as the authored starting charge rather than as empty, because an item that
            // silently arrives at 0% is indistinguishable from one the player drained.
            float stored = SupplyCharge.Read(state);
            SetCharge(stored < 0f ? startingCharge : stored);
        }

        /// <summary>
        /// The bar on this instance's model, bound once.
        ///
        /// Lazily rather than in <see cref="Awake"/> because Unity raises no Awake for an
        /// <c>AddComponent</c> outside play mode — which is how the editor builders make one — and
        /// <see cref="SupplyGauge.Bind"/> walks the hierarchy while <see cref="SupplyGauge.Paint"/>
        /// does not.
        /// </summary>
        private SupplyGauge Gauge
        {
            get
            {
                if (bound) return gauge;

                gauge = SupplyGauge.Bind(transform);
                bound = true;
                return gauge;
            }
        }

        // No OnValidate repaint. The bar's length IS serialized — OxygenGearBuilder bakes the anchor
        // at the starting charge — so the prefab, its icon and every display copy already read
        // correctly on disk, and an OnValidate here would write to the asset behind the builder's
        // back.
    }
}
