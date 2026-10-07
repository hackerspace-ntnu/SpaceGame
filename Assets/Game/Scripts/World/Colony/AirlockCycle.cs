using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>Which of an airlock's two hatches: between the chamber and the room, or the outside.</summary>
    public enum AirlockSide : byte
    {
        Inner = 0,
        Outer = 1,
    }

    /// <summary>What the chamber's air is doing right now.</summary>
    public enum AirlockPhase : byte
    {
        Settled = 0,
        Venting = 1,
        Pressurising = 2,
    }

    /// <summary>Why a click on a hatch did nothing, so the clicker can be told.</summary>
    public enum AirlockRefusal : byte
    {
        None = 0,

        /// <summary>A cycle is running, or a hatch is still shutting before one.</summary>
        Cycling = 1,

        /// <summary>The clicker stands in the chamber and can reach the open inner hatch themselves.</summary>
        CloseInnerFirst = 2,

        /// <summary>The clicker stands in the chamber and can reach the open outer hatch themselves.</summary>
        CloseOuterFirst = 3,

        /// <summary>Someone stands in the doorway of the hatch that was clicked to close.</summary>
        DoorwayOccupied = 4,

        /// <summary>The far hatch would have to be sealed remotely, and someone stands in its doorway.</summary>
        FarDoorwayOccupied = 5,
    }

    /// <summary>
    /// Everything the session agrees about one airlock: which hatches are open, which air the chamber holds, what the
    /// air is doing, and whether an open is waiting on a cycle. One int on the wire (<see cref="ToWire"/>).
    /// </summary>
    public readonly struct AirlockState : IEquatable<AirlockState>
    {
        private const int InnerOpenBit = 1 << 0;
        private const int OuterOpenBit = 1 << 1;
        private const int VentedBit = 1 << 2;
        private const int PendingBit = 1 << 3;
        private const int PendingOuterBit = 1 << 4;
        private const int PhaseShift = 5;
        private const int PhaseMask = 0b11;

        public readonly bool InnerOpen;
        public readonly bool OuterOpen;

        /// <summary>The chamber holds the outside's air (true) or the room's (false).</summary>
        public readonly bool Vented;

        public readonly AirlockPhase Phase;

        /// <summary>The hatch that opens once the chamber is equalised to its side, or null when none is waiting.</summary>
        public readonly AirlockSide? Pending;

        public AirlockState(bool innerOpen, bool outerOpen, bool vented, AirlockPhase phase, AirlockSide? pending)
        {
            InnerOpen = innerOpen;
            OuterOpen = outerOpen;
            Vented = vented;
            Phase = phase;
            Pending = pending;
        }

        /// <summary>Mid-cycle, or waiting for a hatch to shut before one: every click is refused.</summary>
        public bool Busy => Pending.HasValue;

        public bool IsOpen(AirlockSide side) => side == AirlockSide.Inner ? InnerOpen : OuterOpen;

        /// <summary>Whether the chamber already holds the air on <paramref name="side"/>, so that hatch opens at once.</summary>
        public bool EqualisedTo(AirlockSide side) => Vented == (side == AirlockSide.Outer);

        public int ToWire()
        {
            int bits = (int)Phase << PhaseShift;
            if (InnerOpen) bits |= InnerOpenBit;
            if (OuterOpen) bits |= OuterOpenBit;
            if (Vented) bits |= VentedBit;
            if (Pending.HasValue) bits |= PendingBit;
            if (Pending == AirlockSide.Outer) bits |= PendingOuterBit;
            return bits;
        }

        public static AirlockState FromWire(int bits)
        {
            AirlockSide? pending = (bits & PendingBit) == 0 ? null
                                 : (bits & PendingOuterBit) != 0 ? AirlockSide.Outer
                                 : AirlockSide.Inner;
            return new AirlockState((bits & InnerOpenBit) != 0, (bits & OuterOpenBit) != 0, (bits & VentedBit) != 0,
                                    (AirlockPhase)((bits >> PhaseShift) & PhaseMask), pending);
        }

        public bool Equals(AirlockState other) => ToWire() == other.ToWire();
        public override bool Equals(object obj) => obj is AirlockState other && Equals(other);
        public override int GetHashCode() => ToWire();
    }

    /// <summary>
    /// The airlock's decisions, with no scene in them: a click on a hatch goes in, which hatch is open and what the air
    /// is doing come out. <see cref="AirlockChamber"/> feeds it on the machine that decides and moves the hatches.
    ///
    /// <para>
    /// <b>Clicked, and interlocked.</b> A click on a hatch toggles it. A hatch never opens while the other is not
    /// fully shut: someone standing in the chamber is told to close the other one themselves (they can reach it), and
    /// someone on the far side of the hatch they clicked (they cannot) has the far hatch sealed for them, provided
    /// nobody stands in its doorway. No hatch is ever told to close on someone standing in its doorway.
    /// </para>
    /// <para>
    /// <b>The chamber holds one side's air at a time.</b> A hatch on that side opens at once. A hatch on the other side
    /// first waits for both hatches to report shut, then vents or pressurises the chamber for <see cref="CycleSeconds"/>,
    /// and only then opens. Nothing closes on its own: shutting the door behind you is the player's step.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AirlockCycle
    {
        [Tooltip("Seconds the chamber takes to vent or pressurise before the hatch on the other side opens.")]
        [SerializeField, Min(0f)] private float cycleSeconds = 3f;

        private bool innerOpen;
        private bool outerOpen;
        private bool vented;
        private AirlockPhase phase;
        private AirlockSide? pending;
        private float cycleLeft;

        public float CycleSeconds => cycleSeconds;

        /// <summary>
        /// The state as it stands. A fresh cycle is the state every airlock starts in, and comes back in after a load:
        /// both hatches shut, the chamber holding the room's air, nothing waiting.
        /// </summary>
        public AirlockState State => new(innerOpen, outerOpen, vented, phase, pending);

        /// <summary>Take the deciding machine's word for the state. On every machine that only presents.</summary>
        public void Adopt(AirlockState state)
        {
            innerOpen = state.InnerOpen;
            outerOpen = state.OuterOpen;
            vented = state.Vented;
            phase = state.Phase;
            pending = state.Pending;
            cycleLeft = 0f;
        }

        /// <summary>
        /// What a click on <paramref name="hatch"/> would be refused for, without changing anything. The clicker's
        /// machine asks this to explain a refusal; the deciding machine asks it again on arrival.
        /// </summary>
        /// <param name="fromChamber">The clicker stands inside the chamber, within reach of both hatches.</param>
        public AirlockRefusal Check(AirlockSide hatch, bool fromChamber, bool innerDoorwayOccupied,
                                    bool outerDoorwayOccupied)
        {
            if (pending.HasValue) return AirlockRefusal.Cycling;

            if (IsOpen(hatch))
                return DoorwayOccupied(hatch, innerDoorwayOccupied, outerDoorwayOccupied)
                    ? AirlockRefusal.DoorwayOccupied
                    : AirlockRefusal.None;

            AirlockSide far = Other(hatch);
            if (!IsOpen(far)) return AirlockRefusal.None;

            if (fromChamber)
                return far == AirlockSide.Inner ? AirlockRefusal.CloseInnerFirst : AirlockRefusal.CloseOuterFirst;

            return DoorwayOccupied(far, innerDoorwayOccupied, outerDoorwayOccupied)
                ? AirlockRefusal.FarDoorwayOccupied
                : AirlockRefusal.None;
        }

        /// <summary>
        /// A click on <paramref name="hatch"/>. An open hatch shuts; a shut one is queued to open once the chamber is
        /// equalised to its side, sealing the far hatch first when the clicker cannot reach it. Returns the refusal,
        /// or <see cref="AirlockRefusal.None"/> when the click was acted on.
        /// </summary>
        public AirlockRefusal Operate(AirlockSide hatch, bool fromChamber, bool innerDoorwayOccupied,
                                      bool outerDoorwayOccupied)
        {
            AirlockRefusal refusal = Check(hatch, fromChamber, innerDoorwayOccupied, outerDoorwayOccupied);
            if (refusal != AirlockRefusal.None) return refusal;

            if (IsOpen(hatch))
            {
                SetOpen(hatch, false);
                return AirlockRefusal.None;
            }

            SetOpen(Other(hatch), false);
            pending = hatch;
            return AirlockRefusal.None;
        }

        /// <summary>
        /// Advance a waiting open: once both hatches report fully shut, run the cycle if the chamber holds the other
        /// side's air, then open the hatch that was asked for.
        /// </summary>
        public void Tick(float dt, bool innerShut, bool outerShut)
        {
            if (!pending.HasValue || !innerShut || !outerShut) return;

            AirlockSide target = pending.Value;
            bool wantVented = target == AirlockSide.Outer;
            if (vented != wantVented)
            {
                if (phase == AirlockPhase.Settled)
                {
                    phase = wantVented ? AirlockPhase.Venting : AirlockPhase.Pressurising;
                    cycleLeft = cycleSeconds;
                }

                cycleLeft -= dt;
                if (cycleLeft > 0f) return;

                vented = wantVented;
                phase = AirlockPhase.Settled;
                cycleLeft = 0f;
            }

            SetOpen(target, true);
            pending = null;
        }

        private bool IsOpen(AirlockSide side) => side == AirlockSide.Inner ? innerOpen : outerOpen;

        private void SetOpen(AirlockSide side, bool open)
        {
            if (side == AirlockSide.Inner) innerOpen = open;
            else outerOpen = open;
        }

        private static AirlockSide Other(AirlockSide side) =>
            side == AirlockSide.Inner ? AirlockSide.Outer : AirlockSide.Inner;

        private static bool DoorwayOccupied(AirlockSide side, bool inner, bool outer) =>
            side == AirlockSide.Inner ? inner : outer;
    }
}
