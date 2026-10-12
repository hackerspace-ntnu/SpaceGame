// A gate locked by a broken gear train. Its leaves carry pegs; one fixed gear always turns (the drive) and one fixed
// gear works the lock (the output). Gears lie in a pile on the ground in front of it. A player carries one to the
// gate and puts it on a peg (the gear item's own use, GearPegPlacement); whatever meshes with a turning gear turns.
// When the output turns, the lock lets go and the leaves swing open.
//
// What is where is SLOTS: one per peg, then one per spot in the pile, each holding a gear kind or nothing. The server
// decides every change and announces it slot by slot (GearDoorState); everything a slot change means (which gears
// turn, whether the gate opens) follows from the slots on every machine alike, so nothing else crosses the wire. The
// gears' turning is presentation: each machine runs its own clock, and two machines may show a gear at different
// angles, never at different speeds.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// A gear-train lock on a gate: pegs on its leaves, a pile of loose gears, and leaves that swing open once the
    /// train turns the lock. Built by <c>GearDoorBuilder</c>; nothing here is typed by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearDoor : MonoBehaviour
    {
        /// <summary>A gear that can be carried: the item that stands for it and the model drawn on a peg or the ground.</summary>
        [System.Serializable]
        public sealed class Kind
        {
            public InventoryItem item;
            public int teeth;

            public GameObject model;

            [Tooltip("Where the model has a tooth tip, in degrees about its axis, measured by the builder.")]
            public float toothDegrees;
        }

        /// <summary>
        /// Where a gear stands. Its frame: X along the gate to the right as seen from the front, Y up, Z into the gate.
        /// <c>plane</c> is the same point in the gate's own flat coordinates, which is what the train is solved in.
        /// </summary>
        [System.Serializable]
        public sealed class Peg
        {
            public Transform seat;
            public Vector2 plane;

            [Tooltip("The gear kind on this peg at the start (part of the train still standing), or -1 for a bare peg.")]
            public int startKind = -1;
        }

        /// <summary>A gear bolted to the gate: the drive and the lock. Never carried away.</summary>
        [System.Serializable]
        public sealed class FixedGear
        {
            public Transform spinner;
            public Vector2 plane;
            public int teeth;
            public float toothDegrees;
        }

        /// <summary>A spot on the ground where a loose gear lies at the start. Its frame's Z points down into the ground.</summary>
        [System.Serializable]
        public sealed class PileSpot
        {
            public Transform seat;
            public int kind;
        }

        /// <summary>A leaf of the gate. Saved shut; it opens by turning <c>openDegrees</c> about its own <c>localAxis</c>.</summary>
        [System.Serializable]
        public sealed class Leaf
        {
            public Transform node;
            public Vector3 localAxis;
            public float openDegrees;
        }

        private const int InstantBit = 1 << 8;
        private const int KindMask = InstantBit - 1;
        private const int DoorShift = 9;
        private const int AskSlot = -1;
        private const int Empty = -1;
        private const int Driver = 0;
        private const int Output = 1;
        private const int FirstPeg = 2;
        private const string SpinnerName = "Spinner";

        [Tooltip("Names this door's slots in a save, before the slot's own name: empty for the workshop's gate, whose " +
                 "saves predate a second door. Written by the builder.")]
        [SerializeField] private string saveId = string.Empty;

        [Header("Gears")]
        [SerializeField] private GearGeometry geometry;
        [Tooltip("How big every gear model is drawn. The geometry above is already at this scale.")]
        [SerializeField] private float modelScale = 1f;
        [SerializeField] private Kind[] kinds = new Kind[0];
        [SerializeField] private FixedGear drive;
        [SerializeField] private FixedGear output;
        [SerializeField] private Peg[] pegs = new Peg[0];
        [SerializeField] private PileSpot[] pile = new PileSpot[0];
        [SerializeField] private Leaf[] leaves = new Leaf[0];

        [Header("Motion")]
        [Tooltip("How fast the drive gear turns, in degrees a second. Every other gear follows by its tooth ratio.")]
        [SerializeField] private float driveDegreesPerSecond = 24f;
        [Tooltip("Seconds the lock takes to let go once the train turns it, before the leaves start to move.")]
        [SerializeField] private float unlockSeconds = 1.5f;
        [Tooltip("Seconds the leaves take to swing open.")]
        [SerializeField] private float swingSeconds = 8f;

        [Header("Handling")]
        [Tooltip("How far from where the player aims a peg may be, in metres, to put a gear on it.")]
        [SerializeField] private float pegSearchRadius = 1.2f;
        [Tooltip("How thick the box a gear is picked up by is, in metres.")]
        [SerializeField] private float gripDepth = 0.35f;
        [Tooltip("How far above a pile spot to look down for the ground, and how far down.")]
        [SerializeField] private float groundProbeHeight = 3f;
        [SerializeField] private float groundProbeDepth = 8f;
        [SerializeField] private float groundRetrySeconds = 1f;

        [Header("Sound")]
        [SerializeField] private SfxId seatSound = SfxId.ImpactMetal;
        [SerializeField] private SfxId takeSound = SfxId.InteractPickupMetal;
        [SerializeField] private SfxId jamSound = SfxId.InteractDenied;
        [SerializeField] private SfxId unlockSound = SfxId.InteractLever;
        [SerializeField] private SfxId swingSound = SfxId.InteractDoorOpen;

        private static readonly List<GearDoor> Live = new();

        private int[] slots;
        private int[] shownKind;
        private GearDoorGrip[] grips;
        private bool[] grounded;
        private float nextGroundTry;

        // The train: 0 the drive, 1 the output, then every peg (FirstPeg + peg index), seated or not.
        private GearSpec[] specs;
        private float[] speeds;
        private float[] angles;
        private bool[] turning;
        private bool locked;

        private Quaternion[] shut;
        private bool solved;
        private float solvedAt = float.NegativeInfinity;
        private bool swingHeard;
        private Coroutine askRoutine;

        // The holder's preview of where the gear in their hands would go (GearPegPlacement.Preview). Local only.
        private Transform ghost;
        private Material ghostTint;
        private int ghostPeg = Empty, ghostKind = Empty;
        private bool ghostLegal;

        /// <summary>Every slot: the pegs, then the pile.</summary>
        public int SlotCount => pegs.Length + pile.Length;

        public bool IsPeg(int slot) => slot >= 0 && slot < pegs.Length;

        /// <summary>The train turns the lock: the gate is open or opening, and nothing on a peg can be taken off.</summary>
        public bool IsSolved => solved;

        /// <summary>Solved, and the leaves have finished swinging: the lane through the gate is clear. At once after a restore.</summary>
        public bool StandsOpen => solved && Time.time >= solvedAt + unlockSeconds + swingSeconds;

        /// <summary>How many gears still lie in the pile in front of the gate.</summary>
        public int LooseGears
        {
            get
            {
                int loose = 0;
                for (int slot = pegs.Length; slot < SlotCount; slot++)
                    if (slots[slot] != Empty) loose++;
                return loose;
            }
        }

        /// <summary>The gear kind in a slot, or -1.</summary>
        public int KindIn(int slot) => slots[slot];

        public Kind KindAt(int kind) => kinds[kind];

        /// <summary>The kind a slot holds at the authored start: the peg's own start gear, or the pile spot's.</summary>
        public int StartKind(int slot) => IsPeg(slot) ? pegs[slot].startKind : pile[slot - pegs.Length].kind;

        /// <summary>What a save calls a slot. Stable for as long as the builder lays the gate out the same way.</summary>
        public string SlotName(int slot)
        {
            string own = IsPeg(slot) ? $"Peg_{slot:00}" : $"Pile_{slot - pegs.Length:00}";
            return string.IsNullOrEmpty(saveId) ? own : $"{saveId}/{own}";
        }

        /// <summary>The workshop's front gate, as opposed to another door on the same building (the vault's).</summary>
        public bool IsGate => string.IsNullOrEmpty(saveId);

        /// <summary>Whether <paramref name="part"/> is, or hangs from, one of this door's leaves.</summary>
        public bool IsLeafPart(Transform part)
        {
            foreach (Leaf leaf in leaves)
                if (leaf.node != null && part.IsChildOf(leaf.node)) return true;
            return false;
        }

        /// <summary>The front gate among the gear doors under <paramref name="root"/>, or null.</summary>
        public static GearDoor GateOf(GameObject root)
        {
            foreach (GearDoor door in root.GetComponentsInChildren<GearDoor>(true))
                if (door.IsGate) return door;
            return null;
        }

        /// <summary>
        /// Which gear door on this entity this is: every message names it, as several share the building's channel.
        /// Positional, the way a VehicleStation's number is: every machine runs the same prefab.
        /// </summary>
        private int DoorIndex => NetChannel.IndexOf(this);

        /// <summary>Which kind stands for <paramref name="item"/>, or -1.</summary>
        public int KindOf(InventoryItem item)
        {
            for (int k = 0; k < kinds.Length; k++)
                if (kinds[k].item == item) return k;
            return Empty;
        }

        /// <summary>Which kind's item has the id <paramref name="itemId"/>, or -1.</summary>
        public int KindById(string itemId)
        {
            for (int k = 0; k < kinds.Length; k++)
                if (kinds[k].item != null && kinds[k].item.ID == itemId) return k;
            return Empty;
        }

#if UNITY_EDITOR
        /// <summary>Editor, for <c>GearDoorBuilder</c>: everything the gate is made of, in one call.</summary>
        public void Author(string doorSaveId, GearGeometry trainGeometry, float scale, Kind[] gearKinds, FixedGear driveGear,
                           FixedGear outputGear, Peg[] gatePegs, PileSpot[] pileSpots, Leaf[] gateLeaves)
        {
            saveId = doorSaveId;
            geometry = trainGeometry;
            modelScale = scale;
            kinds = gearKinds;
            drive = driveGear;
            output = outputGear;
            pegs = gatePegs;
            pile = pileSpots;
            leaves = gateLeaves;
        }
#endif

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (drive?.spinner == null || output?.spinner == null || pegs.Length == 0 || kinds.Length == 0)
                Debug.LogError($"[GearDoor] '{name}' has no drive, lock, pegs or gear kinds. Rebuild it with " +
                               "Tools/SpaceGame/Workshop/Rebuild Workshop Prefab.", this);

            slots = new int[SlotCount];
            shownKind = new int[SlotCount];
            grips = new GearDoorGrip[SlotCount];
            for (int slot = 0; slot < SlotCount; slot++)
            {
                slots[slot] = StartKind(slot);
                Transform holder = SpinnerOf(slot);
                // The builder lays the pile's gears down already, so the gate looks right in the editor.
                shownKind[slot] = holder != null && holder.childCount > 0 ? slots[slot] : Empty;
                grips[slot] = MakeGrip(slot);
            }
            grounded = new bool[pile.Length];

            specs = new GearSpec[FirstPeg + pegs.Length];
            speeds = new float[specs.Length];
            angles = new float[specs.Length];
            turning = new bool[specs.Length];

            shut = new Quaternion[leaves.Length];
            for (int i = 0; i < leaves.Length; i++) shut[i] = leaves[i].node.localRotation;

            for (int slot = 0; slot < SlotCount; slot++) ShowSlot(slot);
            Solve(instant: true, seated: Empty);
        }

        private void OnEnable()
        {
            Live.Add(this);
            this.NetOn(NetMsg.GearDoorTake, OnTakeRequested);
            this.NetOn(NetMsg.GearDoorState, OnSlotAnnounced);

            // A late joiner, or a client streaming the gate in, asks what has changed since the start.
            if (Network.IsNetworked && !Network.Server)
                askRoutine = StartCoroutine(Fault.Coroutine(this, "GearDoor.Ask",
                    this.NetToServerWhenSpawned(NetMsg.GearDoorTake, new NetArg { A = AskSlot, B = DoorIndex })));
        }

        private void OnDisable()
        {
            HideGhost();
            Live.Remove(this);
            this.NetOff(NetMsg.GearDoorTake, OnTakeRequested);
            this.NetOff(NetMsg.GearDoorState, OnSlotAnnounced);

            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
        }

        private void Update()
        {
            float step = driveDegreesPerSecond * Time.deltaTime;
            for (int i = 0; i < angles.Length; i++)
                if (speeds[i] != 0f) angles[i] = Mathf.Repeat(angles[i] + speeds[i] * step, 360f);

            Turn(drive.spinner, angles[Driver]);
            Turn(output.spinner, angles[Output]);
            for (int i = 0; i < pegs.Length; i++)
                if (slots[i] != Empty) Turn(SpinnerOf(i), angles[FirstPeg + i]);

            if (ghost != null) Turn(ghost, GhostAngle());
            SwingLeaves();
            if (Time.time >= nextGroundTry) GroundPile();
        }

        // ── Placing (server, through the gear item's rule) ───────────────────

        /// <summary>
        /// The free peg nearest <paramref name="point"/> on any gate that a gear of <paramref name="item"/> could go on,
        /// or why not. Asked on the placer's machine before anything is sent, and again on the server.
        /// </summary>
        public static bool TryFindPeg(Vector3 point, InventoryItem item, out GearDoor door, out int peg, out string refusal)
        {
            door = null;
            peg = Empty;
            refusal = null;
            float best = float.MaxValue;
            foreach (GearDoor candidate in Live)
            {
                int nearest = candidate.NearestPeg(point, out float distance);
                if (nearest == Empty || distance >= best) continue;
                best = distance;
                door = candidate;
                peg = nearest;
            }

            if (door == null) return false;
            refusal = door.Refusal(peg, door.KindOf(item));
            return refusal == null;
        }

        /// <summary>Server, or the only machine: put a gear of <paramref name="kind"/> on a peg. False if it may not go there.</summary>
        public bool ServerSeat(int peg, int kind)
        {
            if (!Network.Simulates(this) || Refusal(peg, kind) != null) return false;

            Change(peg, kind);
            return true;
        }

        private int NearestPeg(Vector3 point, out float distance)
        {
            distance = float.MaxValue;
            int nearest = Empty;
            for (int i = 0; i < pegs.Length; i++)
            {
                float d = Vector3.Distance(point, pegs[i].seat.position);
                if (d >= distance || d > pegSearchRadius) continue;
                distance = d;
                nearest = i;
            }
            return nearest;
        }

        /// <summary>Why a gear of <paramref name="kind"/> cannot go on <paramref name="peg"/>, or null if it can.</summary>
        private string Refusal(int peg, int kind)
        {
            if (kind == Empty) return "That does not fit a peg";
            if (solved) return "The gate is already open";
            if (!IsPeg(peg)) return "No peg there";
            if (slots[peg] != Empty) return "A gear is already on that peg";

            var candidate = new GearSpec(pegs[peg].plane, kinds[kind].teeth);
            for (int i = 0; i < specs.Length; i++)
            {
                if (!Stands(i) || i == FirstPeg + peg) continue;
                if (GearTrain.Between(candidate, specs[i], geometry) == GearContact.Hit)
                    return "It would hit the gear beside it";
            }
            return null;
        }

        // ── Taking (asked by whoever pressed, decided by the server) ────────

        /// <summary>A press on a gear lying in a slot: ask the server to put it in the presser's inventory.</summary>
        public void RequestTake(int slot, Interactor interactor)
        {
            if (!CanTake(slot) || interactor == null) return;
            this.NetToServer(NetMsg.GearDoorTake, new NetArg { A = slot, B = DoorIndex }.With(interactor.gameObject));
        }

        /// <summary>Whether the gear in <paramref name="slot"/> may be picked up: one lies there, and it is not locked in a running gate.</summary>
        public bool CanTake(int slot) => slot >= 0 && slot < SlotCount && slots[slot] != Empty && !(solved && IsPeg(slot));

        private void OnTakeRequested(in NetArg arg, ulong sender)
        {
            if (arg.B != DoorIndex || !Network.Simulates(this)) return;

            if (arg.A == AskSlot)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                    if (slots[slot] != StartKind(slot)) Announce(slot, instant: true);
                return;
            }

            // Re-checked here: somebody else may have taken it while this request was on the wire.
            int taken = arg.A;
            if (!CanTake(taken)) return;

            GameObject actor = arg.Resolve();
            if (actor == null || !Network.MayActFor(actor, sender)) return;

            // Upward, as PickupableItem does: the Interactor sits on a child of the player, and only the interface
            // resolves on a networked player.
            IPlayerInventory inventory = actor.GetComponentInParent<IPlayerInventory>();
            if (inventory == null || !inventory.TryAddItem(kinds[slots[taken]].item)) return;

            Change(taken, Empty);
        }

        // ── Saving ───────────────────────────────────────────────────────────

        /// <summary>Server, or the only machine: land every slot as a save left it. <paramref name="kindBySlot"/> holds -1 for an empty one.</summary>
        public void Restore(int[] kindBySlot)
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                int kind = kindBySlot[slot];
                if (slots[slot] == kind) continue;

                slots[slot] = kind;
                ShowSlot(slot);
                if (Network.Simulates(this)) Announce(slot, instant: true);
            }
            Solve(instant: true, seated: Empty);
        }

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>Server side: set a slot, show it here, tell everyone.</summary>
        private void Change(int slot, int kind)
        {
            Apply(slot, kind, instant: false);
            Announce(slot, instant: false);
        }

        private void Announce(int slot, bool instant) =>
            this.NetToAll(NetMsg.GearDoorState, new NetArg
            {
                A = slot, B = (slots[slot] + 1) | (instant ? InstantBit : 0) | (DoorIndex << DoorShift),
            });

        /// <summary>The server's word. Idempotent: the host hears its own announcement back.</summary>
        private void OnSlotAnnounced(in NetArg arg, ulong sender)
        {
            int slot = arg.A;
            if (arg.B >> DoorShift != DoorIndex || slot < 0 || slot >= SlotCount) return;

            int kind = (arg.B & KindMask) - 1;
            if (kind < Empty || kind >= kinds.Length || slots[slot] == kind) return;
            Apply(slot, kind, (arg.B & InstantBit) != 0);
        }

        private void Apply(int slot, int kind, bool instant)
        {
            int before = slots[slot];
            slots[slot] = kind;
            ShowSlot(slot);

            if (!instant) Sfx.Play(kind == Empty ? takeSound : seatSound, SeatOf(slot).position, GetInstanceID());
            Solve(instant, before == Empty && kind != Empty ? slot : Empty);
        }

        /// <summary>Work out which gears turn now, set any newly turning gear into its neighbour's teeth, and react to the lock.</summary>
        private void Solve(bool instant, int seated)
        {
            specs[Driver] = new GearSpec(drive.plane, drive.teeth);
            specs[Output] = new GearSpec(output.plane, output.teeth);
            for (int i = 0; i < pegs.Length; i++)
            {
                int kind = slots[i];
                specs[FirstPeg + i] = new GearSpec(pegs[i].plane, kind == Empty ? 0 : kinds[kind].teeth);
            }

            var standing = new List<GearSpec>(specs.Length);
            var index = new List<int>(specs.Length);
            for (int i = 0; i < specs.Length; i++)
            {
                if (!Stands(i)) continue;
                standing.Add(specs[i]);
                index.Add(i);
            }

            var solvedSpeeds = new float[standing.Count];
            bool nowLocked = GearTrain.Drive(standing, 0, geometry, solvedSpeeds);
            System.Array.Clear(speeds, 0, speeds.Length);
            for (int n = 0; n < standing.Count; n++) speeds[index[n]] = solvedSpeeds[n];

            Align();

            if (!instant && nowLocked && !locked && seated != Empty) Sfx.Play(jamSound, SeatOf(seated).position, GetInstanceID());
            locked = nowLocked;

            bool nowSolved = speeds[Output] != 0f;
            if (nowSolved && !solved)
            {
                solvedAt = instant ? float.NegativeInfinity : Time.time;
                swingHeard = instant;
                if (!instant) Sfx.Play(unlockSound, output.spinner.position, GetInstanceID());
            }
            solved = nowSolved;
        }

        /// <summary>
        /// A gear that has just started turning is set into the teeth of a neighbour that was already turning, so the
        /// two run as one; gears that were turning keep the angle they had.
        /// </summary>
        private void Align()
        {
            var aligned = new bool[specs.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < specs.Length; i++)
            {
                bool was = turning[i];
                turning[i] = speeds[i] != 0f;
                if (turning[i] && (was || i == Driver))
                {
                    aligned[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                for (int other = 0; other < specs.Length; other++)
                {
                    if (aligned[other] || !turning[other] || !Stands(other)) continue;
                    if (GearTrain.Between(specs[at], specs[other], geometry) != GearContact.Mesh) continue;

                    angles[other] = GearTrain.MeshedAngle(specs[at], angles[at], ToothOf(at), specs[other], ToothOf(other));
                    aligned[other] = true;
                    queue.Enqueue(other);
                }
            }
        }

        private bool Stands(int gear) => gear < FirstPeg || slots[gear - FirstPeg] != Empty;

        private float ToothOf(int gear) => gear switch
        {
            Driver => drive.toothDegrees,
            Output => output.toothDegrees,
            _ => kinds[slots[gear - FirstPeg]].toothDegrees,
        };

        private void SwingLeaves()
        {
            float t = solved ? Mathf.Clamp01((Time.time - solvedAt - unlockSeconds) / swingSeconds) : 0f;
            if (solved && !swingHeard && t > 0f)
            {
                swingHeard = true;
                Sfx.Play(swingSound, transform.position, GetInstanceID());
            }

            float eased = Mathf.SmoothStep(0f, 1f, t);
            for (int i = 0; i < leaves.Length; i++)
                leaves[i].node.localRotation = shut[i] * Quaternion.AngleAxis(leaves[i].openDegrees * eased, leaves[i].localAxis);
        }

        // ── What a slot looks like ───────────────────────────────────────────

        private Transform SeatOf(int slot) => IsPeg(slot) ? pegs[slot].seat : pile[slot - pegs.Length].seat;

        private Transform SpinnerOf(int slot)
        {
            Transform seat = SeatOf(slot);
            return seat != null ? seat.Find(SpinnerName) : null;
        }

        // ── The holder's ghost ───────────────────────────────────────────────

        /// <summary>
        /// Show the holder a see-through copy of a gear of <paramref name=kind/> on <paramref name=peg/>: green if
        /// it would go there, red if not, in the colours the backpack and the hull say yes and no in. Purely local:
        /// never sent, never saved. Nothing over a peg that already holds a gear, or once the gate is open.
        /// </summary>
        public void ShowGhost(int peg, int kind, bool legal)
        {
            if (!IsPeg(peg) || kind < 0 || kind >= kinds.Length || slots[peg] != Empty || solved)
            {
                HideGhost();
                return;
            }
            if (ghost != null && peg == ghostPeg && kind == ghostKind && legal == ghostLegal) return;

            HideGhost();
            ghost = new GameObject("PlacementGhost").transform;
            ghost.SetParent(pegs[peg].seat, false);
            GameObject copy = PlaceModel(kinds[kind].model, ghost, modelScale);

            Color body = legal ? PlacementTint.Legal : PlacementTint.Refused;
            ghostTint = TintMaterials.Translucent("GearPlacementGhost", body, new Color(body.r, body.g, body.b, 1f),
                                                  OutlineShell.WidthFor(copy, 1f));
            foreach (Renderer drawn in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, drawn.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = ghostTint;
                drawn.sharedMaterials = materials;
                // A promise, not a gear: it throws no shadow on the gate.
                drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                drawn.receiveShadows = false;
            }

            (ghostPeg, ghostKind, ghostLegal) = (peg, kind, legal);
            Turn(ghost, GhostAngle());
        }

        public void HideGhost()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            if (ghostTint != null) Destroy(ghostTint);
            ghost = null;
            ghostTint = null;
            (ghostPeg, ghostKind) = (Empty, Empty);
        }

        /// <summary>The ghost's teeth set into the first standing gear it would mesh with, so it shows how it would run.</summary>
        private float GhostAngle()
        {
            var candidate = new GearSpec(pegs[ghostPeg].plane, kinds[ghostKind].teeth);
            for (int i = 0; i < specs.Length; i++)
            {
                if (!Stands(i) || i == FirstPeg + ghostPeg) continue;
                if (GearTrain.Between(specs[i], candidate, geometry) == GearContact.Mesh)
                    return GearTrain.MeshedAngle(specs[i], angles[i], ToothOf(i), candidate, kinds[ghostKind].toothDegrees);
            }
            return 0f;
        }

        private static void Turn(Transform spinner, float degrees)
        {
            if (spinner != null) spinner.localRotation = Quaternion.AngleAxis(degrees, Vector3.forward);
        }

        /// <summary>Draw the gear a slot holds, and size the box it is picked up by.</summary>
        private void ShowSlot(int slot)
        {
            int kind = slots[slot];
            Transform spinner = SpinnerOf(slot);
            if (spinner == null) return;

            if (shownKind[slot] != kind)
            {
                for (int c = spinner.childCount - 1; c >= 0; c--) Destroy(spinner.GetChild(c).gameObject);
                if (kind != Empty) PlaceModel(kinds[kind].model, spinner, modelScale);
                shownKind[slot] = kind;
            }

            GearDoorGrip grip = grips[slot];
            if (grip == null) return;
            grip.Collider.enabled = kind != Empty;
            if (kind == Empty) return;

            float across = 2f * geometry.OuterRadius(kinds[kind].teeth);
            grip.Collider.size = new Vector3(across, across, gripDepth);
            grip.Collider.center = new Vector3(0f, 0f, -0.5f * gripDepth);
        }

        /// <summary>
        /// A gear model under its spinner, facing out of the seat (the seat's -Z). Shared with the builder, which lays
        /// the drive, the lock and the pile down the same way.
        /// </summary>
        public static GameObject PlaceModel(GameObject model, Transform spinner, float scale)
        {
            GameObject drawn = Instantiate(model, spinner, false);
            drawn.name = model.name;
            drawn.transform.localPosition = Vector3.zero;
            drawn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * drawn.transform.localRotation;
            drawn.transform.localScale = drawn.transform.localScale * scale;
            return drawn;
        }

        private GearDoorGrip MakeGrip(int slot)
        {
            Transform seat = SeatOf(slot);
            if (seat == null) return null;

            var grip = new GameObject("Grip").AddComponent<GearDoorGrip>();
            grip.transform.SetParent(seat, false);
            grip.Bind(this, slot);
            return grip;
        }

        /// <summary>Lay the pile on whatever ground is under it, wherever the gate was placed. Tried again until every spot lands.</summary>
        private void GroundPile()
        {
            nextGroundTry = Time.time + groundRetrySeconds;
            for (int i = 0; i < pile.Length; i++)
            {
                if (grounded[i]) continue;

                Transform seat = pile[i].seat;
                Vector3 from = seat.position + Vector3.up * groundProbeHeight;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, groundProbeDepth,
                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;

                seat.position = hit.point;
                grounded[i] = true;
            }
        }
    }
}
