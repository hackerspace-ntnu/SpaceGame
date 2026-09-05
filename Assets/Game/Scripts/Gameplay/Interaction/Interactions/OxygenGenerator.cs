using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The ship's oxygen plant: a wall-mounted machine with two receptacles. Fit a battery into the
    /// rectangular slot and the machine wakes up; plug a tank into the round collar above it and
    /// the machine spends watt-hours filling the tank.
    ///
    /// <para>
    /// <b>Two docks, two colliders, one state.</b> Each receptacle is aimed at and pressed
    /// separately (<see cref="OxygenGeneratorDock"/>), because a receptacle IS the signifier for the
    /// verb it offers and a machine with one prompt for both would need the player to guess which
    /// (<c>GDC-L1-UX-0004</c>). Everything they decide is decided here, so the two can never
    /// disagree about whether the machine has power.
    /// </para>
    /// <para>
    /// <b>Both reservoirs are real, and the fill is PROPORTIONAL.</b> Filling a tank from <i>f</i>
    /// to full takes <c>fillSeconds x (1 - f)</c> and costs <c>fillCostPerTank x (1 - f)</c> of the
    /// battery — so topping up a nearly full tank is quick and nearly free, and a full battery is
    /// worth a fixed number of tanks however the player chooses to take them. A flat cost per press
    /// would teach players to run every tank to zero before coming back, which is the opposite of
    /// the behaviour the plant exists to encourage (<c>GDC-L1-SYS-0006</c>: the rule has to be
    /// legible, and "you pay for what you take" is the only one that is).
    /// </para>
    /// <para>
    /// <b>Nothing recharges a battery.</b> Power is a terminal resource in this build: batteries are
    /// found, spent, and gone. That is a deliberate, temporary state — the charger is the next piece
    /// of work — and it is recorded here rather than in a comment somewhere else because a sink with
    /// no source is a flow problem that will not announce itself (<c>GDC-L1-SYS-0008</c>).
    /// </para>
    /// <para>
    /// <b>A tank's charge is a number on the instance</b>, not an item identity. It used to be an
    /// identity — a drained tank and a full one were two assets — and <see cref="SupplyCharge"/>
    /// records both why that was right and why a tank the player reads to a percent cannot work
    /// that way. So the collar hands back the SAME item it took, holding more.
    /// </para>
    /// <para>
    /// Nested under <c>PlayerShip.prefab</c> this has no <c>NetworkObject</c> of its own and
    /// inherits the hull's, which is what makes the <c>NetworkVariable</c> below replicate — the
    /// same arrangement as the repair station. <see cref="IPersistentEntity"/> because a fitted
    /// battery is world state and this component has none of the things <c>SaveablePolicy</c>
    /// otherwise infers saving from.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class OxygenGenerator : NetworkBehaviour, IPersistentEntity
    {
        /// <summary>Which receptacle a press came from.</summary>
        public enum DockKind
        {
            /// <summary>The round collar. Takes an oxygen tank, base first.</summary>
            Tank,

            /// <summary>The rectangular slot. Takes a slab battery, lying on its back.</summary>
            Cell,
        }

        /// <summary>
        /// What used to stand in the bottle dock, in the days when a tank's charge was its identity.
        ///
        /// <para>
        /// <b>Legacy save format only.</b> Nothing in the running machine uses it any more — the
        /// dock holds a fraction now — and it survives solely so
        /// <c>OxygenGeneratorSaveable</c> can read a world written before 2026-09-04. The numbers
        /// are in save files: never renumber, never reuse.
        /// </para>
        /// </summary>
        public enum DockedTank
        {
            None = 0,
            Drained = 1,
            Charged = 2,
        }

        /// <summary>
        /// Everything the machine is, in one replicated value.
        ///
        /// <para>
        /// One <c>NetworkVariable</c> rather than four because they are read together and only ever
        /// change together: a client that had the battery but not yet the fill deadline would light
        /// the lamp and stand silent. One value means one callback and one write path.
        /// </para>
        /// <para>
        /// <b>Both charges are frozen at the moment a fill starts, and neither is written again
        /// until it ends.</b> What each one reads RIGHT NOW is derived from the clock
        /// (<see cref="TankCharge"/>, <see cref="BatteryCharge"/>), exactly as the fill's own
        /// progress always was. That is what keeps a fill at two network writes rather than one a
        /// frame, and it is what lets a player who joins mid-fill see the rest of it.
        /// </para>
        /// </summary>
        private struct Plant : INetworkSerializable, System.IEquatable<Plant>
        {
            /// <summary>Battery charge 0..1, or <see cref="Empty"/> for an empty slot.</summary>
            public float Battery;

            /// <summary>Tank charge 0..1 as it was at <see cref="FillStartedAt"/>, or <see cref="Empty"/>.</summary>
            public float Tank;

            /// <summary>When the running fill began, on the SERVER's clock.</summary>
            public double FillStartedAt;

            /// <summary>
            /// When the running fill lands, on the SERVER's clock, or 0 when nothing is filling.
            ///
            /// A deadline rather than a progress float: a progress value would have to be written
            /// every frame — a network write a frame, per machine — while a deadline is written
            /// twice and every machine reads its own clock against it.
            /// </summary>
            public double FillEndsAt;

            /// <summary>No receptacle. Negative, because a fraction never legitimately is.</summary>
            public const float Empty = -1f;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Battery);
                serializer.SerializeValue(ref Tank);
                serializer.SerializeValue(ref FillStartedAt);
                serializer.SerializeValue(ref FillEndsAt);
            }

            public bool Equals(Plant other) =>
                Battery.Equals(other.Battery)
                && Tank.Equals(other.Tank)
                && FillStartedAt.Equals(other.FillStartedAt)
                && FillEndsAt.Equals(other.FillEndsAt);
        }

        /// <summary>One emissive submesh that says whether the machine has power.</summary>
        [System.Serializable]
        private struct Lamp
        {
            [SerializeField] private Renderer part;

            [Tooltip("Which submesh of that renderer is the emissive one. -1 paints all of them.")]
            [SerializeField] private int materialIndex;

            public Renderer Part => part;
            public int MaterialIndex => materialIndex;
        }

        [Header("Items")]
        [Tooltip("The oxygen tank. One item at every fill level — its charge is a number on the " +
                 "instance, not a second asset.")]
        [SerializeField] private InventoryItem tankItem;

        [Tooltip("The battery the machine runs on. Accepted by the rectangular slot only.")]
        [SerializeField] private InventoryItem batteryItem;

        [Header("Docks")]
        [Tooltip("Where a docked tank is drawn. Its pose IS the docked pose — see the builder.")]
        [SerializeField] private Transform tankSeat;

        [Tooltip("Where a docked battery is drawn.")]
        [SerializeField] private Transform cellSeat;

        [Header("Filling")]
        [Tooltip("Seconds a WHOLE tank takes, from empty. A partial fill is proportionally " +
                 "quicker. Long enough to be an event, short enough to wait out.")]
        [SerializeField, Min(0.1f)] private float fillSeconds = 5f;

        [Tooltip("Fraction of a battery one WHOLE tank costs. At the default a battery is worth " +
                 "twenty-five tanks; a partial fill costs proportionally less.")]
        [SerializeField, Range(0.001f, 1f)] private float fillCostPerTank = 0.04f;

        [Header("Power")]
        [Tooltip("Lamps and readouts lit only while a battery with charge left is in.")]
        [SerializeField] private Lamp[] lamps = new Lamp[0];

        [Tooltip("The real light the machine casts on the bulkhead. SWITCHED, never dimmed to zero " +
                 "— a URP light at zero intensity is still a light the renderer sorts.")]
        [SerializeField] private Light powerLight;

        [SerializeField] private Color litColour = new Color(1f, 0.72f, 0.25f);

        [Tooltip("Unpowered. Dark, not black: an unlit lamp is dark glass, and a black one reads " +
                 "as a hole in the machine.")]
        [SerializeField] private Color darkColour = new Color(0.10f, 0.08f, 0.06f);

        [Header("Audio")]
        [Tooltip("Sustained while a tank fills. A loop, so it is owned by an emitter and stopped " +
                 "on both teardown paths.")]
        [SerializeField] private SfxId fillLoopId = SfxId.InteractOxygenFillLoop;
        [SerializeField] private EventReference fillLoopSound;

        [SerializeField] private SfxId filledId = SfxId.InteractOxygenFilled;
        [SerializeField] private SfxId dockedId = SfxId.InteractPickupMetal;
        [SerializeField] private SfxId undockedId = SfxId.InteractDrop;
        [SerializeField] private SfxId deniedId = SfxId.InteractDenied;

        private readonly NetworkVariable<Plant> networkPlant = new();

        private readonly LoopingEmitter fillLoop = new();

        // Mirrors networkPlant, and is the sole truth when there is no session.
        private Plant plant = new() { Battery = Plant.Empty, Tank = Plant.Empty };
        private bool spawned;

        // The inert copies standing in the two docks, and what is needed to drive the tank's gauge
        // while it fills — resolved when a copy is built, because DisplayCopy.Strip takes the
        // item's own DockableSupply off the copy along with every other script.
        private GameObject tankCopy;
        private GameObject cellCopy;
        private Renderer tankGauge;
        private int tankGaugeIndex = EmissiveLamp.WholeRenderer;
        private Color tankGaugeEmpty;
        private Color tankGaugeFull;

        /// <summary>Is a battery fitted at all? A FLAT one still counts as fitted.</summary>
        public bool HasBattery => plant.Battery >= 0f;

        /// <summary>Is a tank standing in the collar at all? An EMPTY one still counts.</summary>
        public bool HasTank => plant.Tank >= 0f;

        /// <summary>
        /// Can the machine do anything? A fitted battery with charge left in it.
        ///
        /// <para>
        /// This, not merely "a battery is in", is what lights the lamps: a machine that looked
        /// powered and refused to fill would be the least explainable state it could be in.
        /// </para>
        /// </summary>
        public bool Powered => BatteryCharge > 0f;

        /// <summary>How full the fitted battery is right now, 0..1. Negative with no battery.</summary>
        public float BatteryCharge =>
            plant.Battery < 0f ? Plant.Empty : Mathf.Clamp01(plant.Battery - (Transferable * fillCostPerTank * FillProgress01));

        /// <summary>How full the docked tank is right now, 0..1. Negative with no tank.</summary>
        public float TankCharge =>
            plant.Tank < 0f ? Plant.Empty : Mathf.Clamp01(plant.Tank + (Transferable * FillProgress01));

        /// <summary>Is a tank filling right now?</summary>
        public bool IsFilling => plant.FillEndsAt > 0d && Now < plant.FillEndsAt;

        /// <summary>How far through the current fill, 0..1. Zero when nothing is filling.</summary>
        public float FillProgress01
        {
            get
            {
                if (plant.FillEndsAt <= 0d) return 0f;

                double span = plant.FillEndsAt - plant.FillStartedAt;
                if (span <= 0d) return 1f;

                return Mathf.Clamp01((float)((Now - plant.FillStartedAt) / span));
            }
        }

        /// <summary>
        /// How much of a tank the CURRENT fill can actually move, as a fraction of a whole tank.
        ///
        /// <para>
        /// Bounded by both ends: the room left in the tank, and what the battery can pay for. A
        /// battery with 2% left fills half a tank's worth of the 4% a whole one costs and then
        /// stops — the machine going quiet with a part-filled tank in it is the honest outcome, and
        /// the lamps going dark at the same moment is what says why.
        /// </para>
        /// <para>
        /// Derived, never stored. It depends only on values that are frozen for the whole of a
        /// fill, so every machine computes the same answer from the same replicated struct — and a
        /// stored copy is one more thing that can disagree.
        /// </para>
        /// </summary>
        private float Transferable
        {
            get
            {
                if (plant.Tank < 0f || plant.Battery < 0f) return 0f;

                float room = Mathf.Clamp01(1f - plant.Tank);
                float affordable = fillCostPerTank > 0f ? plant.Battery / fillCostPerTank : room;

                return Mathf.Max(0f, Mathf.Min(room, affordable));
            }
        }

        /// <summary>Seconds a whole tank takes. Read by the tests that pin the timing.</summary>
        public float FillSeconds => fillSeconds;

        /// <summary>
        /// How long the running fill still has to go, in seconds, or 0 when nothing is filling.
        ///
        /// <para>
        /// The whole span rather than what is left when the fill has not started ticking yet, which
        /// is what makes it testable: the deadline is set from a clock a test cannot advance, so
        /// asserting the SPAN is the only way to pin "a quarter-tank top-up takes a quarter of the
        /// time" without waiting five seconds of real time to find out.
        /// </para>
        /// </summary>
        public float SecondsUntilFilled =>
            plant.FillEndsAt <= 0d ? 0f : Mathf.Max(0f, (float)(plant.FillEndsAt - Now));

        /// <summary>Fraction of a battery a whole tank costs. Read by the tests that pin the economy.</summary>
        public float FillCostPerTank => fillCostPerTank;

        /// <summary>
        /// The clock both halves of the fill are measured on.
        ///
        /// The server's, so a deadline set on one machine means the same instant on every other —
        /// and the local clock when there is no session at all, where the two are the same thing.
        /// </summary>
        private double Now =>
            spawned && NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.timeAsDouble;

        /// <summary>
        /// May this machine decide? The server, or the only machine there is.
        ///
        /// Asked of <see cref="Network"/> rather than of <c>spawned</c>: a client's copy is spawned
        /// and must never decide, and a copy that has not spawned YET would answer the other way.
        /// </summary>
        private static bool Authoritative => !Network.IsNetworked || Network.Server;

        /// <summary>Everything this machine cannot work without. Reported once, in Awake.</summary>
        private bool IsWired => tankItem != null && batteryItem != null;

        private void Awake()
        {
            // Said once, loudly, at the earliest moment it can be known. A fixture whose item
            // references were dropped by a rebuild refuses every press in silence otherwise, which
            // reads as a broken interaction rather than as a broken prefab.
            if (!IsWired)
                Debug.LogError(name + ": OxygenGenerator has no tank or battery item assigned — " +
                               "rebuild it with Tools/SpaceGame/Build Oxygen System.", this);

            // Offline and pre-spawn, the mirror is the whole truth, so the machine has to look like
            // whatever it holds before anything replicates.
            ApplyPower();
            RefreshDockedVisuals();
            RefreshFill();
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkPlant.OnValueChanged += HandlePlantChanged;

            if (IsServer)
            {
                // Publish whatever we already hold: the authored default, or a restored save.
                networkPlant.Value = plant;
                RefreshFill();
            }
            else
            {
                // Catching up on a machine that was already running is not an event to announce.
                Adopt(plant, networkPlant.Value, silent: true);
            }
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkPlant.OnValueChanged -= HandlePlantChanged;
        }

        // Two different exits — a scene unload and a despawn — and a loop leaks on whichever one
        // is not handled.
        private void OnDisable() => fillLoop.Stop(allowFadeOut: false);

        /// <summary>
        /// <c>override</c>, not a plain method. <see cref="NetworkBehaviour"/> declares its own
        /// <c>OnDestroy</c> and does real teardown in it; hiding it compiles with a warning and
        /// leaks the behaviour's netcode registration for the rest of the session.
        /// </summary>
        public override void OnDestroy()
        {
            fillLoop.Stop(allowFadeOut: false);
            base.OnDestroy();
        }

        // ── What the docks ask ─────────────────────────────────────────────────

        /// <summary>What this receptacle is, for the HUD.</summary>
        public string LabelFor(DockKind kind) =>
            kind == DockKind.Cell ? "Battery dock" : "Oxygen filler";

        /// <summary>What pressing here would do, and why it might not.</summary>
        public string PromptFor(DockKind kind)
        {
            if (kind == DockKind.Cell)
            {
                if (!HasBattery) return "RMB: fit a battery";
                return Powered ? "RMB: take the battery" : "RMB: take the flat battery";
            }

            if (!HasTank) return Powered ? "RMB: dock an oxygen tank" : "RMB: dock a tank — no power";

            if (IsFilling) return "filling…   RMB: take the tank";
            if (TankCharge >= 1f) return "RMB: take the full tank";

            // A tank that is neither full nor filling: either the machine is flat, or it has just
            // spent the last of the battery part way through. Both are the same sentence.
            return Powered ? "RMB: take the tank" : "RMB: take the tank — no power";
        }

        /// <summary>Where this receptacle sits, 0..1, or null for one with nothing to show.</summary>
        public float? Value01(DockKind kind)
        {
            float charge = kind == DockKind.Cell ? BatteryCharge : TankCharge;

            return charge < 0f ? null : charge;
        }

        /// <summary>
        /// The same value in words — a whole percent for both receptacles, which is the number the
        /// item's own gauge and the visor show too (see <see cref="SupplyCharge.Describe"/>).
        /// </summary>
        public string ValueText(DockKind kind)
        {
            float charge = kind == DockKind.Cell ? BatteryCharge : TankCharge;

            return charge < 0f ? string.Empty : SupplyCharge.Describe(charge);
        }

        // ── Pressing ───────────────────────────────────────────────────────────

        /// <summary>
        /// A press on one of the two receptacles. Runs on the presser's machine only — getting the
        /// consequence onto the others is this component's own job, and it does it with the
        /// replicated <see cref="Plant"/> rather than with a feedback message: every sound and
        /// every visual below is derived from a state TRANSITION, so all of them happen on every
        /// machine for free.
        /// </summary>
        public void Interact(DockKind kind, Interactor interactor)
        {
            if (interactor == null) return;

            // Local and immediate. A refusal is the presser's own business and their machine can
            // answer it honestly — hotbar contents replicate — so it needs no round trip, and a
            // click for pointing an empty hand at the machine would be heard by the whole crew.
            if (!WouldAct(kind, interactor))
            {
                Sfx.Play(deniedId, transform.position, default, GetInstanceID());
                return;
            }

            Network.Execute(
                local: () => Resolve(kind, interactor),
                client: () => InteractorRelay.RequestFrom(
                    interactor, body => ResolveServerRpc(body, (int)kind)));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResolveServerRpc(NetworkObjectReference interactorRef, int kind)
        {
            if (!InteractorRelay.TryResolve(interactorRef, out Interactor interactor)) return;

            Resolve((DockKind)kind, interactor);
        }

        /// <summary>
        /// Would a press here do anything for this player? Asked twice — once on the presser's
        /// machine to decide whether to make a refusal noise, once on the server as the real
        /// gate — which is the point: the local answer is advice, the server's is the decision.
        /// </summary>
        private bool WouldAct(DockKind kind, Interactor interactor)
        {
            if (kind == DockKind.Cell) return HasBattery || Holds(interactor, batteryItem);

            return HasTank || Holds(interactor, tankItem);
        }

        /// <summary>Server-side (or offline) authority.</summary>
        private void Resolve(DockKind kind, Interactor interactor)
        {
            if (interactor == null) return;

            IPlayerInventory inventory = interactor.GetComponentInParent<IPlayerInventory>();
            if (inventory == null) return;

            if (!IsWired) return;

            bool cell = kind == DockKind.Cell;
            bool occupied = cell ? HasBattery : HasTank;

            if (occupied) TakeFromDock(inventory, interactor, cell);
            else FitIntoDock(inventory, cell);
        }

        /// <summary>
        /// Put the selected item into a receptacle, carrying its charge in with it.
        ///
        /// <para>
        /// A running fill is settled FIRST (<see cref="SettleFill"/>). Fitting a battery while one
        /// is running cannot happen — the slot is occupied — but fitting a TANK while the machine
        /// is mid-anything can, and committing a new dock state without first banking what the
        /// clock had already transferred would rewrite the frozen start values under a live
        /// deadline.
        /// </para>
        /// </summary>
        private void FitIntoDock(IPlayerInventory inventory, bool cell)
        {
            InventoryItem wanted = cell ? batteryItem : tankItem;

            InventorySlot slot = inventory.GetSelectedSlot();
            if (slot == null || slot.IsEmpty || !Same(slot.Item, wanted)) return;

            // Read before the removal, which takes the bag with the item.
            float charge = SupplyCharge.Read(slot.State);
            if (charge < 0f) charge = SupplyCharge.StartingChargeOf(wanted);

            if (!inventory.TryRemoveItem(inventory.SelectedSlotIndex)) return;

            Plant next = SettleFill();
            if (cell) next.Battery = Mathf.Clamp01(charge);
            else next.Tank = Mathf.Clamp01(charge);

            Commit(next);
            RefreshFill();
        }

        /// <summary>Hand a receptacle's contents back, holding exactly what the clock says.</summary>
        private void TakeFromDock(IPlayerInventory inventory, Interactor interactor, bool cell)
        {
            InventoryItem given = cell ? batteryItem : tankItem;

            // Read against the LIVE clock, before the fill is settled, so a tank pulled half way
            // through leaves with the half it was given and the battery keeps the rest. There is no
            // partial-charge penalty and no rounding to a whole press: the player paid for the
            // seconds that elapsed and got the oxygen those seconds bought.
            float charge = cell ? BatteryCharge : TankCharge;

            if (!Give(inventory, interactor, given, charge)) return;

            Plant next = SettleFill();
            if (cell) next.Battery = Plant.Empty;
            else next.Tank = Plant.Empty;

            Commit(next);
            RefreshFill();
        }

        /// <summary>
        /// The plant with any running fill banked into its two frozen charges and the clock
        /// cleared — what the state WOULD be if the fill stopped this instant.
        ///
        /// <para>
        /// Every change to what is docked goes through this. Without it the frozen start values and
        /// the running deadline describe two different machines, and the next
        /// <see cref="TankCharge"/> read would add a second fill's worth of progress on top of a
        /// charge that had already been banked.
        /// </para>
        /// </summary>
        private Plant SettleFill()
        {
            Plant next = plant;

            if (plant.FillEndsAt > 0d)
            {
                next.Tank = TankCharge;
                next.Battery = BatteryCharge;
            }

            next.FillStartedAt = 0d;
            next.FillEndsAt = 0d;

            return next;
        }

        private bool Give(IPlayerInventory inventory, Interactor interactor, InventoryItem item,
                          float charge)
        {
            if (item == null) return false;

            // Hotbar first, then the pack — the same overflow a world pickup takes, and for the
            // same reason: a three-slot hotbar would otherwise make a full hand mean "this is
            // yours forever".
            if (inventory.TryAddItem(item, out int landed))
            {
                if (landed >= 0 && charge >= 0f)
                {
                    InventorySlot slot = inventory.GetSlot(landed);
                    if (slot != null && !slot.IsEmpty)
                    {
                        slot.State ??= new ItemState();
                        SupplyCharge.Write(slot.State, charge);
                        inventory.PublishSlotCharges();
                    }
                }

                return true;
            }

            BackpackController backpack = interactor.GetComponentInParent<BackpackController>();
            return backpack != null && backpack.Pack != null && backpack.Pack.TryStow(item, charge);
        }

        /// <summary>
        /// Reference equality first — every slot resolves through the registry to the same Resources
        /// asset the inspector field points at — with the ID as the fallback for a runtime copy.
        /// </summary>
        private static bool Same(InventoryItem a, InventoryItem b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;

            return !string.IsNullOrEmpty(a.ID) && a.ID == b.ID;
        }

        private static bool Holds(Interactor interactor, InventoryItem wanted)
        {
            IPlayerInventory inventory = interactor.GetComponentInParent<IPlayerInventory>();
            InventorySlot slot = inventory?.GetSelectedSlot();

            return slot != null && !slot.IsEmpty && Same(slot.Item, wanted);
        }

        // ── Filling ────────────────────────────────────────────────────────────

        /// <summary>
        /// Start, stop or leave the fill alone, from whatever is docked right now.
        ///
        /// Server only, and called after every change — including a restore — so the machine needs
        /// no separate "start filling" verb: pulling the battery out stops the fill, putting one
        /// back starts one, and a save reloaded with a part-filled tank in a powered machine
        /// resumes from where it stood.
        /// </summary>
        private void RefreshFill()
        {
            if (!Authoritative) return;

            bool running = plant.FillEndsAt > 0d;

            // Asked of the SETTLED state, because "is there anything left to do" has to be answered
            // about the charges as they will be once the running fill is banked — otherwise a fill
            // that has just finished its own work looks like one that still has work to do.
            Plant settled = SettleFill();
            float moves = TransferableOf(settled);

            if (moves > 0f == running) return;

            if (moves <= 0f)
            {
                Commit(settled);
                return;
            }

            settled.FillStartedAt = Now;
            settled.FillEndsAt = Now + (fillSeconds * moves);
            Commit(settled);
        }

        /// <summary>
        /// <see cref="Transferable"/> for an arbitrary state rather than the live one, so
        /// <see cref="RefreshFill"/> can ask the question of a settled plant it has not committed
        /// yet.
        /// </summary>
        private float TransferableOf(Plant state)
        {
            if (state.Tank < 0f || state.Battery < 0f) return 0f;

            float room = Mathf.Clamp01(1f - state.Tank);
            float affordable = fillCostPerTank > 0f ? state.Battery / fillCostPerTank : room;

            return Mathf.Max(0f, Mathf.Min(room, affordable));
        }

        private void FinishFill()
        {
            // Committed from the same derivation every machine has been drawing all along, at a
            // moment when FillProgress01 has reached exactly 1 — so the number that lands is the
            // number the gauge was already showing.
            Commit(SettleFill());
            RefreshFill();
        }

        private void Update()
        {
            DrawFill();

            if (!Authoritative) return;
            if (plant.FillEndsAt > 0d && Now >= plant.FillEndsAt) FinishFill();
        }

        /// <summary>Per machine, per frame: the loop and the tank's own gauge climbing.</summary>
        private void DrawFill()
        {
            bool filling = IsFilling;

            if (filling && !fillLoop.IsPlaying) fillLoop.Play(fillLoopId, gameObject, fillLoopSound);
            else if (!filling && fillLoop.IsPlaying) fillLoop.Stop();

            if (!filling || tankGauge == null) return;

            EmissiveLamp.Paint(tankGauge, tankGaugeIndex,
                               Color.Lerp(tankGaugeEmpty, tankGaugeFull, TankCharge));
        }

        // ── State ──────────────────────────────────────────────────────────────

        private void Commit(Plant next)
        {
            if (spawned && IsServer)
            {
                // The callback lands on the host too, so the local half runs there exactly once.
                networkPlant.Value = next;
                return;
            }

            Adopt(plant, next, silent: false);
        }

        private void HandlePlantChanged(Plant previous, Plant current) =>
            Adopt(previous, current, silent: false);

        private void Adopt(Plant previous, Plant current, bool silent)
        {
            if (current.Equals(plant)) return;

            bool hadBattery = previous.Battery >= 0f;
            bool hasBattery = current.Battery >= 0f;
            bool hadTank = previous.Tank >= 0f;
            bool hasTank = current.Tank >= 0f;

            // Whether the machine can WORK, which is what the lamps say — a battery going flat
            // darkens them exactly as pulling it out does.
            bool wasPowered = previous.Battery > 0f;

            plant = current;

            // Each dock redraws on its OWN change, on the frame the press lands
            // (`GDC-L1-FEEL-0002`). One method rebuilding both, reached only from the tank's line,
            // is what made a fitted battery light the lamps and draw nothing — and then put the
            // battery in the slot at the unrelated moment a tank was docked or a fill landed.
            if (hadBattery != hasBattery) RefreshCellVisual();

            // Separate from the copy, because a battery that merely EMPTIED is the same object in
            // the same slot with different lamps.
            if (wasPowered != Powered) ApplyPower();

            if (hadTank != hasTank) RefreshTankVisual();
            else if (hasTank && tankGauge != null && !IsFilling)
                EmissiveLamp.Paint(tankGauge, tankGaugeIndex,
                                   Color.Lerp(tankGaugeEmpty, tankGaugeFull, TankCharge));

            if (silent) return;

            if (hadBattery != hasBattery) Play(hasBattery ? dockedId : undockedId);

            if (hadTank != hasTank) Play(hasTank ? dockedId : undockedId);
            else if (hasTank && previous.Tank < 1f && current.Tank >= 1f) Play(filledId);
        }

        private void Play(SfxId id) => Sfx.Play(id, transform.position, default, GetInstanceID());

        // ── Presentation ───────────────────────────────────────────────────────

        private void ApplyPower()
        {
            Color colour = Powered ? litColour : darkColour;

            foreach (Lamp lamp in lamps)
                EmissiveLamp.Paint(lamp.Part, lamp.MaterialIndex, colour);

            // Switched, not dimmed: a light at zero intensity still costs the renderer a light.
            if (powerLight != null) powerLight.enabled = Powered;
        }

        /// <summary>
        /// Rebuild both inert copies. For the paths that know nothing about what changed — only
        /// <c>Awake</c>, where neither dock has a previous value to compare against.
        ///
        /// <para>
        /// Copies rather than spawned items: what is in a dock is scenery that the machine's own
        /// replicated state already describes, so spawning a NetworkObject for it would be a second
        /// account of the same fact — and one that could disagree.
        /// </para>
        /// </summary>
        private void RefreshDockedVisuals()
        {
            RefreshCellVisual();
            RefreshTankVisual();
        }

        private void RefreshCellVisual() =>
            Rebuild(ref cellCopy, cellSeat, HasBattery ? batteryItem : null);

        /// <summary>
        /// Redraw the collar, and re-bind the gauge the fill paints to the copy now standing in it.
        ///
        /// Separate from the battery's half so a battery press leaves this one alone: fitting the
        /// battery is the press that STARTS a fill, and rebuilding the tank would throw away the
        /// renderer <see cref="DrawFill"/> is about to paint.
        /// </summary>
        private void RefreshTankVisual()
        {
            tankGauge = null;
            Rebuild(ref tankCopy, tankSeat, HasTank ? tankItem : null);

            if (tankCopy == null) return;

            BindTankGauge(tankItem);

            // The copy is built from the prefab, so it is painted at the prefab's starting charge
            // until something says otherwise. A tank docked at 40% would show full for as long as
            // nothing was filling.
            if (tankGauge != null)
                EmissiveLamp.Paint(tankGauge, tankGaugeIndex,
                                   Color.Lerp(tankGaugeEmpty, tankGaugeFull, TankCharge));
        }

        private static void Rebuild(ref GameObject copy, Transform seat, InventoryItem item)
        {
            if (copy != null)
            {
                // `Destroy` is deferred and is refused outright outside play mode, which an EditMode
                // test driving a restore hits immediately — and a deferred destroy would leave the
                // old copy standing for the rest of the frame beside the new one anyway.
                if (Application.isPlaying) Destroy(copy);
                else DestroyImmediate(copy);

                copy = null;
            }

            if (seat == null || item == null || item.itemPrefab == null) return;

            copy = DisplayCopy.Make(item.itemPrefab, seat);
        }

        /// <summary>
        /// Find the gauge on the copy that was just built, and the two colours to lerp it between.
        ///
        /// <para>
        /// By NAME, off the item's own prefab, because <c>DisplayCopy.Strip</c> takes the copy's
        /// <see cref="DockableSupply"/> off with every other script — the copy is scenery and has no
        /// business running gameplay code. So the prefab is asked which part its gauge is and the
        /// copy is searched for the same part.
        /// </para>
        /// </summary>
        private void BindTankGauge(InventoryItem item)
        {
            var supply = item.itemPrefab.GetComponent<DockableSupply>();
            if (supply == null || supply.Readout == null) return;

            tankGaugeIndex = supply.ReadoutMaterialIndex;
            tankGaugeEmpty = supply.EmptyColour;
            tankGaugeFull = supply.ChargedColour;

            string wanted = supply.Readout.name;
            foreach (Renderer candidate in tankCopy.GetComponentsInChildren<Renderer>(true))
            {
                if (candidate.name != wanted) continue;

                tankGauge = candidate;
                return;
            }
        }

        // ── Persistence ────────────────────────────────────────────────────────

        /// <summary>
        /// Restore-only. Called by the save system; never from gameplay.
        ///
        /// <para>
        /// The mirror is written FIRST and <see cref="Adopt"/> early-outs on an unchanged value, so
        /// the <c>NetworkVariable</c> write below re-enters and does nothing — which is what keeps a
        /// reloaded world from clunking and hissing its way through work that was already done.
        /// </para>
        /// <para>
        /// The fill CLOCK is deliberately not restored — it is an instant on a clock that no longer
        /// exists — but the two charges are, so <see cref="RefreshFill"/> resumes from exactly
        /// where the machine stood rather than starting the tank again from where it was docked.
        /// That is the one behaviour this rework changed here: a partial charge is now expressible,
        /// so throwing it away on a reload would be a real loss rather than a rounding.
        /// </para>
        /// </summary>
        /// <param name="battery">Battery charge 0..1, or negative for an empty slot.</param>
        /// <param name="tank">Tank charge 0..1, or negative for an empty collar.</param>
        public void RestoreDock(float battery, float tank)
        {
            var next = new Plant
            {
                Battery = battery < 0f ? Plant.Empty : Mathf.Clamp01(battery),
                Tank = tank < 0f ? Plant.Empty : Mathf.Clamp01(tank),
                FillStartedAt = 0d,
                FillEndsAt = 0d,
            };

            Adopt(plant, next, silent: true);

            if (spawned && IsServer) networkPlant.Value = next;

            RefreshFill();
        }
    }
}
