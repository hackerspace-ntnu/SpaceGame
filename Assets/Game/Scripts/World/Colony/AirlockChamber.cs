using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// A colony airlock: an outer and an inner set of <see cref="AirlockHatch"/> leaves, three
    /// <see cref="PresenceZone"/>s, and an <see cref="AirlockCycle"/> that decides between them.
    ///
    /// <para>
    /// <b>Clicked, and decided in one place.</b> Right-click the outer hatch: it opens (venting the chamber first if
    /// it holds the room's air). Step in, right-click it shut behind you, right-click the inner hatch: the chamber
    /// pressurises and the inner hatch slides open. A click the cycle refuses is answered on the clicker's machine
    /// with a visor message saying why ("Close the outer hatch first").
    /// </para>
    /// <para>
    /// <b>Networked over the building's channel.</b> A click is a request to the server
    /// (<see cref="NetMsg.AirlockOperate"/>); the server runs the cycle and tells everyone the whole state
    /// (<see cref="NetMsg.AirlockState"/>); a late joiner asks once its entity is spawned. The channel is the
    /// settlement wrapper's <c>NetworkObject</c>, which <c>SettlementNetworking.Wrap</c> puts round any building holding
    /// an airlock. A colony placed by hand has no wrapper: there every machine runs its own airlock, and
    /// <c>NetChannel.WarnUnrelayed</c> says so in the console the first time a hatch is clicked in a session.
    /// </para>
    /// <para>
    /// <b>Nothing is saved.</b> Every airlock starts, and comes back after a load or a chunk reload, with both hatches
    /// shut and the chamber holding the room's air.
    /// </para>
    /// </summary>
    public sealed class AirlockChamber : MonoBehaviour
    {
        // ── Wire. See the table above NetMsg.AirlockOperate. ──
        private const int AskVerb = -1;
        private const int OuterBit = 1 << 0;
        private const int FromChamberBit = 1 << 1;
        private const int InstantBit = 1 << 8;

        /// <summary>Every airlock posts its refusals under one id, so a new one replaces the last.</summary>
        private const string HintId = "airlock";

        [SerializeField] private AirlockCycle cycle = new();

        [Header("Hatches")]
        [Tooltip("The leaves between the chamber and the room.")]
        [SerializeField] private AirlockHatch[] inner;
        [Tooltip("The leaves between the chamber and the outside: the building's own door.")]
        [SerializeField] private AirlockHatch[] outer;

        [Header("Zones")]
        [Tooltip("The chamber between the hatches. A clicker standing in it can reach both hatches, so is told to " +
                 "close the other one rather than having it sealed for them.")]
        [SerializeField] private PresenceZone chamber;
        [Tooltip("The swept volume of each hatch's doorway; a hatch is never told to shut on someone standing in it.")]
        [SerializeField] private PresenceZone innerDoorway;
        [SerializeField] private PresenceZone outerDoorway;

        [Header("Refusals")]
        [Tooltip("Seconds a refusal stays on the clicker's visor.")]
        [SerializeField, Min(0.5f)] private float refusalSeconds = 3f;
        [SerializeField] private SfxId refusedSound = SfxId.InteractDenied;

        private AirlockState presented;
        private float phaseStartedAt;
        private int? index;
        private Coroutine askRoutine;

        /// <summary>The state this machine is showing: the server's word, or its own decision on the server.</summary>
        public AirlockState State => presented;

        /// <summary>How far through the running vent or pressurise, 0..1, or null while the air is settled.</summary>
        public float? CycleProgress
        {
            get
            {
                if (presented.Phase == AirlockPhase.Settled) return null;
                return cycle.CycleSeconds > 0f ? Mathf.Clamp01((Time.time - phaseStartedAt) / cycle.CycleSeconds) : 1f;
            }
        }

        /// <summary>What the chamber's air is, for the hatch readout.</summary>
        public string AirText => presented.Phase switch
        {
            AirlockPhase.Venting => "Venting",
            AirlockPhase.Pressurising => "Pressurising",
            _ => presented.Vented ? "Chamber vented" : "Chamber pressurised",
        };

        /// <summary>The middle of the chamber, where a crossing colonist waits for the cycle.</summary>
        public Vector3 ChamberCentre => chamber.Centre;

        /// <summary>The middle of one hatch's doorway.</summary>
        public Vector3 DoorwayCentre(AirlockSide side) => (side == AirlockSide.Inner ? innerDoorway : outerDoorway).Centre;

        /// <summary>Every leaf of this side is out of its doorway.</summary>
        public bool IsFullyOpen(AirlockSide side) => AllOpen(side == AirlockSide.Inner ? inner : outer);

        /// <summary>Every leaf of this side is in its doorway.</summary>
        public bool IsShut(AirlockSide side) => AllShut(side == AirlockSide.Inner ? inner : outer);

        /// <summary>Whoever stands in this side's doorway: a player, or a colonist mid-crossing.</summary>
        public bool DoorwayOccupied(AirlockSide side) => Occupied(side == AirlockSide.Inner ? innerDoorway : outerDoorway);

        /// <summary>Which chamber this is on its entity: the A of every message, so the others drop it.</summary>
        private int Index => index ??= NetChannel.IndexOf(this);

        private void Awake()
        {
            if (inner == null || inner.Length == 0 || outer == null || outer.Length == 0 ||
                chamber == null || innerDoorway == null || outerDoorway == null)
                Debug.LogError($"[Airlock] '{name}' is missing a hatch or a zone. Its hatches cannot be clicked " +
                               "and a missing doorway zone would let a hatch shut on a player.", this);

            Bind(inner, AirlockSide.Inner);
            Bind(outer, AirlockSide.Outer);
            presented = cycle.State;
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.AirlockOperate, OnOperateRequested);
            this.NetOn(NetMsg.AirlockState, OnStateAnnounced);

            // A late joiner, or a client streaming the chunk in, asks what the airlock is doing. No teardown: an
            // unanswered ask leaves the shut, pressurised start state, which is what the server most often holds.
            if (Network.IsNetworked && !Network.Server)
                askRoutine = StartCoroutine(Fault.Coroutine(this, "AirlockChamber.Ask",
                    this.NetToServerWhenSpawned(NetMsg.AirlockOperate, new NetArg { A = Index, B = AskVerb })));
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.AirlockOperate, OnOperateRequested);
            this.NetOff(NetMsg.AirlockState, OnStateAnnounced);

            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
        }

        private void Update()
        {
            if (!Network.Simulates(this)) return;

            cycle.Tick(Time.deltaTime, AllShut(inner), AllShut(outer));
            Publish();
        }

        // ── The clicker's machine ────────────────────────────────────────────

        /// <summary>
        /// A right-click on one of <paramref name="side"/>'s leaves. Refusals are explained here, at once, from the
        /// state this machine already holds; anything else is asked of the server, which checks again.
        /// </summary>
        public void Operate(AirlockSide side, Interactor interactor)
        {
            // Where the clicker stands is read here, on their own machine, where their body's position is the truth.
            bool fromChamber = InChamber(interactor);
            AirlockRefusal refusal = cycle.Check(side, fromChamber, Occupied(innerDoorway), Occupied(outerDoorway));
            if (refusal != AirlockRefusal.None)
            {
                PlayerHints.Show(HintId, RefusalText(side, refusal), refusalSeconds);
                Sfx.Play(refusedSound, HatchPosition(side), GetInstanceID());
                return;
            }

            int request = (side == AirlockSide.Outer ? OuterBit : 0) | (fromChamber ? FromChamberBit : 0);
            this.NetToServer(NetMsg.AirlockOperate, new NetArg { A = Index, B = request });
        }

        /// <summary>
        /// What a right-click on <paramref name="side"/> will do, for the crosshair. Asked about the local player,
        /// who is the only one who can press it here.
        /// </summary>
        public string PromptFor(AirlockSide side)
        {
            AirlockRefusal refusal = cycle.Check(side, InChamber(GameplayMenuScope.FindLocalPlayer()),
                                                 Occupied(innerDoorway), Occupied(outerDoorway));
            if (refusal != AirlockRefusal.None) return RefusalText(side, refusal);
            if (presented.IsOpen(side)) return "RMB: close";
            if (presented.EqualisedTo(side)) return "RMB: open";

            // Not equalised to this side, so the far hatch is either shut or about to be sealed from here.
            bool sealsFar = presented.IsOpen(side == AirlockSide.Inner ? AirlockSide.Outer : AirlockSide.Inner);
            if (side == AirlockSide.Outer)
                return sealsFar ? "RMB: seal the inner hatch, vent and open" : "RMB: vent and open";
            return sealsFar ? "RMB: seal the outer hatch, pressurise and open" : "RMB: pressurise and open";
        }

        private static string RefusalText(AirlockSide side, AirlockRefusal refusal) => refusal switch
        {
            AirlockRefusal.Cycling => "The airlock is cycling",
            AirlockRefusal.CloseInnerFirst => "Close the inner hatch first",
            AirlockRefusal.CloseOuterFirst => "Close the outer hatch first",
            AirlockRefusal.DoorwayOccupied => "Step clear of the hatch first",
            AirlockRefusal.FarDoorwayOccupied => side == AirlockSide.Inner
                ? "Someone is standing in the outer hatch"
                : "Someone is standing in the inner hatch",
            _ => string.Empty,
        };

        // ── The deciding machine ─────────────────────────────────────────────

        /// <summary>
        /// Server side, or the only machine there is. <c>Network.Simulates</c> and not a server test: a colony with no
        /// NetworkObject has no wire, the request was dispatched locally, and this machine is its only authority.
        /// </summary>
        private void OnOperateRequested(in NetArg arg, ulong sender)
        {
            if (arg.A != Index || !Network.Simulates(this)) return;

            if (arg.B == AskVerb)
            {
                this.NetToAll(NetMsg.AirlockState, new NetArg { A = Index, B = presented.ToWire() | InstantBit });
                return;
            }

            AirlockSide side = (arg.B & OuterBit) != 0 ? AirlockSide.Outer : AirlockSide.Inner;
            bool fromChamber = (arg.B & FromChamberBit) != 0;

            // Re-checked here rather than trusted: somebody else may have clicked, or stepped into a doorway, while
            // this request was on the wire. A refusal here says nothing; the clicker saw the state that allowed it.
            ServerOperate(side, fromChamber);
        }

        /// <summary>
        /// A hatch operated by the world rather than a click: a colonist crossing (<see cref="AirlockPassage"/>). The same
        /// decision a click gets, so an occupied doorway or a running cycle refuses it; the refusal says nothing, the caller
        /// asks again. Server (or the only machine) only.
        /// </summary>
        public AirlockRefusal ServerOperate(AirlockSide side, bool fromChamber)
        {
            if (!Network.Simulates(this)) return AirlockRefusal.Cycling;

            AirlockRefusal refusal = cycle.Operate(side, fromChamber, Occupied(innerDoorway), Occupied(outerDoorway));
            Publish();
            return refusal;
        }

        /// <summary>Show the cycle's state here and tell everyone, when it changed.</summary>
        private void Publish()
        {
            AirlockState now = cycle.State;
            if (now.Equals(presented)) return;

            Present(now, instant: false);
            this.NetToAll(NetMsg.AirlockState, new NetArg { A = Index, B = now.ToWire() });
        }

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>
        /// The server's word. Idempotent: the host hears its own announcement back, and a joiner's answer goes to
        /// everyone, so only a state that differs from the one shown is acted on.
        /// </summary>
        private void OnStateAnnounced(in NetArg arg, ulong sender)
        {
            if (arg.A != Index) return;

            AirlockState state = AirlockState.FromWire(arg.B & ~InstantBit);
            if (state.Equals(presented)) return;

            if (!Network.Simulates(this)) cycle.Adopt(state);
            Present(state, instant: (arg.B & InstantBit) != 0);
        }

        private void Present(AirlockState state, bool instant)
        {
            if (state.Phase != presented.Phase) phaseStartedAt = Time.time;
            presented = state;

            Set(inner, state.InnerOpen, instant);
            Set(outer, state.OuterOpen, instant);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void Bind(AirlockHatch[] leaves, AirlockSide side)
        {
            if (leaves == null) return;
            foreach (AirlockHatch leaf in leaves)
                if (leaf != null) leaf.Bind(this, side);
        }

        private static bool AllShut(AirlockHatch[] leaves)
        {
            if (leaves == null) return true;
            foreach (AirlockHatch leaf in leaves)
                if (leaf != null && !leaf.IsShut) return false;
            return true;
        }

        private static bool AllOpen(AirlockHatch[] leaves)
        {
            if (leaves == null) return false;
            foreach (AirlockHatch leaf in leaves)
                if (leaf != null && !leaf.IsFullyOpen) return false;
            return true;
        }

        private static void Set(AirlockHatch[] leaves, bool open, bool instant)
        {
            if (leaves == null) return;
            foreach (AirlockHatch leaf in leaves)
                if (leaf != null) leaf.Set(open, instant);
        }

        private static bool Occupied(PresenceZone zone) => zone != null && zone.Occupied;

        /// <summary>Whether the player <paramref name="someone"/> belongs to stands in the chamber.</summary>
        private bool InChamber(Component someone)
        {
            if (chamber == null || someone == null) return false;

            // The body, not the camera rig an Interactor may sit on: resolved the way the messaging layer resolves it.
            GameObject body = NetChannel.RootOf(someone);
            return body != null && chamber.Contains(body.GetComponentInChildren<SuitOxygen>(true));
        }

        private Vector3 HatchPosition(AirlockSide side)
        {
            AirlockHatch[] leaves = side == AirlockSide.Inner ? inner : outer;
            return leaves != null && leaves.Length > 0 && leaves[0] != null ? leaves[0].transform.position
                                                                             : transform.position;
        }
    }
}
