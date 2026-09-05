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
    /// The ship's oxygen plant: a wall-mounted machine with two receptacles. Plug a power cell into
    /// the rectangular slot and the machine wakes up; plug a bottle into the round collar above it
    /// and the machine spends <see cref="fillSeconds"/> filling it.
    ///
    /// <para>
    /// <b>Two docks, two colliders, one state.</b> Each receptacle is aimed at and pressed
    /// separately (<see cref="OxygenGeneratorDock"/>), because a receptacle IS the signifier for the
    /// verb it offers and a machine with one prompt for both would need the player to guess which
    /// (<c>GDC-L1-UX-0004</c>). Everything they decide is decided here, so the two can never
    /// disagree about whether the machine has power.
    /// </para>
    /// <para>
    /// <b>Oxygen is unlimited and the cell never drains.</b> There is no consumption of either, by
    /// design: the plant is a station the player comes back to, not a resource to ration. What the
    /// fill produces is a CHARGED BOTTLE, and nothing in the game spends one yet — the loop this
    /// closes is find a bottle, power the plant, fill the bottle.
    /// </para>
    /// <para>
    /// <b>A bottle's charge is its identity.</b> <see cref="drainedTank"/> and
    /// <see cref="chargedTank"/> are two <see cref="InventoryItem"/> assets rather than one item
    /// with a number on it, because the hotbar replicates item IDs and <c>ItemState</c> does not
    /// replicate at all — a charge kept in a bag would be a value only the server could see (see
    /// Inventory.md). Filling therefore hands back a different item from the one that went in.
    /// </para>
    /// <para>
    /// Nested under <c>PlayerShip.prefab</c> this has no <c>NetworkObject</c> of its own and
    /// inherits the hull's, which is what makes the <c>NetworkVariable</c> below replicate — the
    /// same arrangement as the repair station. <see cref="IPersistentEntity"/> because a docked cell
    /// is world state and this component has none of the things <c>SaveablePolicy</c> otherwise
    /// infers saving from.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class OxygenGenerator : NetworkBehaviour, IPersistentEntity
    {
        /// <summary>Which receptacle a press came from.</summary>
        public enum DockKind
        {
            /// <summary>The round collar. Takes an oxygen bottle, base first.</summary>
            Tank,

            /// <summary>The rectangular slot. Takes a slab power cell, lying on its back.</summary>
            Cell,
        }

        /// <summary>
        /// What is standing in the bottle dock. The numbers are written into save files — never
        /// renumber, never reuse.
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
        /// One <c>NetworkVariable</c> rather than three because the three are read together and
        /// only ever change together: a client that had the cell but not yet the fill deadline
        /// would light the lamp and stand silent. One value means one callback and one write path.
        /// </para>
        /// </summary>
        private struct Plant : INetworkSerializable, System.IEquatable<Plant>
        {
            /// <summary>Is a power cell plugged in?</summary>
            public bool Cell;

            /// <summary>A <see cref="DockedTank"/>, as an int so the struct stays blittable.</summary>
            public int Tank;

            /// <summary>
            /// When the fill lands, on the SERVER's clock, or 0 when nothing is filling.
            ///
            /// A deadline rather than a progress float: a progress value would have to be written
            /// every frame — a network write a frame, for five seconds, per machine — while a
            /// deadline is written twice and every machine reads its own clock against it. It is
            /// also what lets a player who joins mid-fill see the rest of it.
            /// </summary>
            public double FillEndsAt;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Cell);
                serializer.SerializeValue(ref Tank);
                serializer.SerializeValue(ref FillEndsAt);
            }

            public bool Equals(Plant other) =>
                Cell == other.Cell && Tank == other.Tank && FillEndsAt.Equals(other.FillEndsAt);
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
        [Tooltip("The empty bottle. What the machine accepts, and what taking one back mid-fill returns.")]
        [SerializeField] private InventoryItem drainedTank;

        [Tooltip("The full bottle. What a completed fill turns the docked one into.")]
        [SerializeField] private InventoryItem chargedTank;

        [Tooltip("The battery the machine runs on. Accepted by the rectangular slot only.")]
        [SerializeField] private InventoryItem powerCell;

        [Header("Docks")]
        [Tooltip("Where a docked bottle is drawn. Its pose IS the docked pose — see the builder.")]
        [SerializeField] private Transform tankSeat;

        [Tooltip("Where a docked power cell is drawn.")]
        [SerializeField] private Transform cellSeat;

        [Header("Filling")]
        [Tooltip("Seconds one bottle takes. Long enough to be an event, short enough to wait out.")]
        [SerializeField, Min(0.1f)] private float fillSeconds = 5f;

        [Header("Power")]
        [Tooltip("Lamps and readouts lit only while a power cell is in.")]
        [SerializeField] private Lamp[] lamps = new Lamp[0];

        [Tooltip("The real light the machine casts on the bulkhead. SWITCHED, never dimmed to zero " +
                 "— a URP light at zero intensity is still a light the renderer sorts.")]
        [SerializeField] private Light powerLight;

        [SerializeField] private Color litColour = new Color(1f, 0.72f, 0.25f);

        [Tooltip("Unpowered. Dark, not black: an unlit lamp is dark glass, and a black one reads " +
                 "as a hole in the machine.")]
        [SerializeField] private Color darkColour = new Color(0.10f, 0.08f, 0.06f);

        [Header("Audio")]
        [Tooltip("Sustained while a bottle fills. A loop, so it is owned by an emitter and stopped " +
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
        private Plant plant;
        private bool spawned;

        // The inert copies standing in the two docks, and what is needed to drive the bottle's
        // gauge while it fills — resolved when a copy is built, because DisplayCopy.Strip takes the
        // item's own DockableSupply off the copy along with every other script.
        private GameObject tankCopy;
        private GameObject cellCopy;
        private Renderer tankGauge;
        private int tankGaugeIndex = EmissiveLamp.WholeRenderer;
        private Color tankGaugeEmpty;
        private Color tankGaugeFull;

        /// <summary>Is a power cell in? Nothing else about the machine works without one.</summary>
        public bool Powered => plant.Cell;

        /// <summary>What is standing in the bottle dock.</summary>
        public DockedTank Tank => (DockedTank)plant.Tank;

        /// <summary>Is a bottle filling right now?</summary>
        public bool IsFilling => plant.FillEndsAt > 0d && Now < plant.FillEndsAt;

        /// <summary>How far through the current fill, 0..1. Zero when nothing is filling.</summary>
        public float FillProgress01 =>
            plant.FillEndsAt <= 0d
                ? 0f
                : Mathf.Clamp01(1f - (float)((plant.FillEndsAt - Now) / Mathf.Max(0.1f, fillSeconds)));

        /// <summary>Seconds one bottle takes. Read by the tests that pin the timing.</summary>
        public float FillSeconds => fillSeconds;

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

        private void Awake()
        {
            // Said once, loudly, at the earliest moment it can be known. A fixture whose item
            // references were dropped by a rebuild refuses every press in silence otherwise, which
            // reads as a broken interaction rather than as a broken prefab.
            if (!IsWired)
                Debug.LogError(name + ": OxygenGenerator has no drained tank, charged tank or " +
                               "power cell item assigned — rebuild it with " +
                               "Tools/SpaceGame/Build Oxygen System.", this);

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
            kind == DockKind.Cell ? "Power cell dock" : "Oxygen filler";

        /// <summary>What pressing here would do, and why it might not.</summary>
        public string PromptFor(DockKind kind)
        {
            if (kind == DockKind.Cell)
                return plant.Cell ? "RMB: take the power cell" : "RMB: fit a power cell";

            switch (Tank)
            {
                case DockedTank.Charged: return "RMB: take the filled tank";
                case DockedTank.Drained:
                    return IsFilling ? "filling…   RMB: take the tank" : "RMB: take the tank — no power";
                default:
                    return plant.Cell ? "RMB: dock an oxygen tank" : "RMB: dock a tank — no power";
            }
        }

        /// <summary>Where this receptacle sits, 0..1, or null for one with nothing to show.</summary>
        public float? Value01(DockKind kind)
        {
            if (kind == DockKind.Cell) return plant.Cell ? 1f : (float?)null;

            switch (Tank)
            {
                case DockedTank.Charged: return 1f;
                case DockedTank.Drained: return IsFilling ? FillProgress01 : 0f;
                default: return null;
            }
        }

        /// <summary>The same value in words.</summary>
        public string ValueText(DockKind kind)
        {
            if (kind == DockKind.Cell) return plant.Cell ? "charged" : string.Empty;

            switch (Tank)
            {
                case DockedTank.Charged: return "full";
                case DockedTank.Drained: return Mathf.RoundToInt(FillProgress01 * 100f) + "%";
                default: return string.Empty;
            }
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
            if (kind == DockKind.Cell)
                return plant.Cell || Holds(interactor, powerCell);

            if (Tank != DockedTank.None) return true;

            return Holds(interactor, drainedTank) || Holds(interactor, chargedTank);
        }

        /// <summary>Server-side (or offline) authority.</summary>
        private void Resolve(DockKind kind, Interactor interactor)
        {
            if (interactor == null) return;

            IPlayerInventory inventory = interactor.GetComponentInParent<IPlayerInventory>();
            if (inventory == null) return;

            if (!IsWired) return;

            if (kind == DockKind.Cell)
            {
                if (plant.Cell) TakeFromDock(inventory, interactor, powerCell, cell: true);
                else FitIntoDock(inventory, powerCell, cell: true);
            }
            else if (Tank != DockedTank.None)
            {
                TakeFromDock(inventory, interactor,
                             Tank == DockedTank.Charged ? chargedTank : drainedTank, cell: false);
            }
            else
            {
                // A charged bottle is allowed in as well: the collar is a shelf as much as a filler,
                // and refusing a full one would be a rule the player has to learn for no gain.
                if (Holds(interactor, chargedTank)) FitIntoDock(inventory, chargedTank, cell: false);
                else FitIntoDock(inventory, drainedTank, cell: false);
            }
        }

        private void FitIntoDock(IPlayerInventory inventory, InventoryItem wanted, bool cell)
        {
            InventorySlot slot = inventory.GetSelectedSlot();
            if (slot == null || slot.IsEmpty || !Same(slot.Item, wanted)) return;

            if (!inventory.TryRemoveItem(inventory.SelectedSlotIndex)) return;

            Plant next = plant;
            if (cell) next.Cell = true;
            else next.Tank = (int)(wanted == chargedTank ? DockedTank.Charged : DockedTank.Drained);

            Commit(next);
            RefreshFill();
        }

        private void TakeFromDock(IPlayerInventory inventory, Interactor interactor,
                                  InventoryItem given, bool cell)
        {
            // Hotbar first, then the pack — the same overflow a world pickup takes, and for the
            // same reason: a three-slot hotbar would otherwise make a full hand mean "this is
            // yours forever".
            if (!Give(inventory, interactor, given)) return;

            Plant next = plant;
            if (cell) next.Cell = false;
            else next.Tank = (int)DockedTank.None;

            Commit(next);
            RefreshFill();
        }

        private bool Give(IPlayerInventory inventory, Interactor interactor, InventoryItem item)
        {
            if (item == null) return false;
            if (inventory.TryAddItem(item)) return true;

            BackpackController backpack = interactor.GetComponentInParent<BackpackController>();
            return backpack != null && backpack.Pack != null && backpack.Pack.TryStow(item);
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

        /// <summary>Everything this machine cannot work without. Reported once, in Awake.</summary>
        private bool IsWired => drainedTank != null && chargedTank != null && powerCell != null;

        // ── Filling ────────────────────────────────────────────────────────────

        /// <summary>
        /// Start, stop or leave the fill alone, from whatever is docked right now.
        ///
        /// Server only, and called after every change — including a restore — so the machine needs
        /// no separate "start filling" verb: pulling the cell out stops the fill, putting it back
        /// starts one, and a save reloaded with a drained bottle in a powered machine resumes.
        /// </summary>
        private void RefreshFill()
        {
            if (!Authoritative) return;

            bool wanted = plant.Cell && Tank == DockedTank.Drained;
            bool running = plant.FillEndsAt > 0d;

            if (wanted == running) return;

            Plant next = plant;
            next.FillEndsAt = wanted ? Now + fillSeconds : 0d;
            Commit(next);
        }

        private void FinishFill()
        {
            Plant next = plant;
            next.Tank = (int)DockedTank.Charged;
            next.FillEndsAt = 0d;
            Commit(next);
        }

        private void Update()
        {
            DrawFill();

            if (!Authoritative) return;
            if (plant.FillEndsAt > 0d && Now >= plant.FillEndsAt) FinishFill();
        }

        /// <summary>Per machine, per frame: the loop and the bottle's own gauge climbing.</summary>
        private void DrawFill()
        {
            bool filling = IsFilling;

            if (filling && !fillLoop.IsPlaying) fillLoop.Play(fillLoopId, gameObject, fillLoopSound);
            else if (!filling && fillLoop.IsPlaying) fillLoop.Stop();

            if (!filling || tankGauge == null) return;

            EmissiveLamp.Paint(tankGauge, tankGaugeIndex,
                               Color.Lerp(tankGaugeEmpty, tankGaugeFull, FillProgress01));
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

            plant = current;

            // Each dock redraws on its OWN change, on the frame the press lands
            // (`GDC-L1-FEEL-0002`). One method rebuilding both, reached only from the tank's line,
            // is what made a fitted cell light the lamps and draw nothing — and then put the cell
            // in the slot at the unrelated moment a bottle was docked or a fill landed.
            if (previous.Cell != current.Cell)
            {
                ApplyPower();
                RefreshCellVisual();
            }

            if (previous.Tank != current.Tank) RefreshTankVisual();

            if (silent) return;

            if (previous.Cell != current.Cell)
                Play(current.Cell ? dockedId : undockedId);

            if (previous.Tank == (int)DockedTank.None && current.Tank != (int)DockedTank.None)
                Play(dockedId);
            else if (previous.Tank != (int)DockedTank.None && current.Tank == (int)DockedTank.None)
                Play(undockedId);
            else if (previous.Tank == (int)DockedTank.Drained &&
                     current.Tank == (int)DockedTank.Charged)
                Play(filledId);
        }

        private void Play(SfxId id) => Sfx.Play(id, transform.position, default, GetInstanceID());

        // ── Presentation ───────────────────────────────────────────────────────

        private void ApplyPower()
        {
            Color colour = plant.Cell ? litColour : darkColour;

            foreach (Lamp lamp in lamps)
                EmissiveLamp.Paint(lamp.Part, lamp.MaterialIndex, colour);

            // Switched, not dimmed: a light at zero intensity still costs the renderer a light.
            if (powerLight != null) powerLight.enabled = plant.Cell;
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
            Rebuild(ref cellCopy, cellSeat, plant.Cell ? powerCell : null);

        /// <summary>
        /// Redraw the collar, and re-bind the gauge the fill paints to the copy now standing in it.
        ///
        /// Separate from the cell's half so a cell press leaves this one alone: fitting the cell is
        /// the press that STARTS a fill, and rebuilding the bottle would throw away the renderer
        /// <see cref="DrawFill"/> is about to paint.
        /// </summary>
        private void RefreshTankVisual()
        {
            InventoryItem tankItem = Tank == DockedTank.Charged ? chargedTank
                                   : Tank == DockedTank.Drained ? drainedTank
                                   : null;

            tankGauge = null;
            Rebuild(ref tankCopy, tankSeat, tankItem);
            if (tankCopy != null) BindTankGauge(tankItem);
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
        /// The fill deadline is deliberately not restored. It is an instant on a clock that no
        /// longer exists; <see cref="RefreshFill"/> starts a fresh one, so a world saved with a
        /// half-filled bottle in a powered machine reloads and fills it again.
        /// </para>
        /// </summary>
        public void RestoreDock(bool cellIn, DockedTank tank)
        {
            var next = new Plant { Cell = cellIn, Tank = (int)tank, FillEndsAt = 0d };

            Adopt(plant, next, silent: true);

            if (spawned && IsServer) networkPlant.Value = next;

            RefreshFill();
        }
    }
}
