using System;
using System.Collections.Generic;

namespace SpaceGame.World
{
    /// <summary>Which way a colonist crosses an airlock.</summary>
    public enum AirlockDirection : byte
    {
        /// <summary>From outside to the room: through the outer hatch first.</summary>
        Inward,

        /// <summary>From the room to outside: through the inner hatch first.</summary>
        Outward,
    }

    /// <summary>What <see cref="AirlockTransit"/> needs of an airlock, so it can be run against a fake one.</summary>
    public interface IAirlockPort
    {
        AirlockState State { get; }
        bool IsFullyOpen(AirlockSide side);
        bool IsShut(AirlockSide side);

        /// <summary>The airlock's own decision for a hatch, refused when a cycle runs or a doorway is occupied.</summary>
        AirlockRefusal Operate(AirlockSide side, bool fromChamber);

        /// <summary>Nobody (no player) could see this traveller or the point it would hop to.</summary>
        bool Unobserved(int traveller);
    }

    /// <summary>What a traveller does this tick: stand, walk to <see cref="Waypoint"/>, hop across unseen, or it is across.</summary>
    public readonly struct TransitOrder
    {
        public enum Kind : byte { Wait, Walk, Hop, Done }

        public readonly Kind Step;
        public readonly int Waypoint;

        public TransitOrder(Kind step, int waypoint = -1)
        {
            Step = step;
            Waypoint = waypoint;
        }
    }

    /// <summary>
    /// A crossing of one airlock by colonists, with no scene in it: the airlock's state and the clock go in, hatch requests and
    /// one order per traveller come out. <see cref="AirlockPassage"/> feeds it and carries the bodies.
    ///
    /// <para>
    /// <b>One group at a time.</b> The first traveller to arrive waits <c>groupWindowSeconds</c> for company, then every waiting
    /// traveller bound the same way crosses together, on one cycle; anyone bound the other way waits for the next. A group's
    /// stages follow the airlock's own interlock, each decided from the state it reports and never from memory of what was
    /// asked, so a click from a player in the middle only sends the stage back a step: <b>Admit</b> (open the near hatch),
    /// <b>Board</b> (walk to the chamber's middle), <b>Seal</b> (shut the near hatch), <b>Cycle</b> (open the far one),
    /// <b>Unload</b> (walk out), <b>Settle</b> (shut whatever is still open).
    /// </para>
    /// <para>
    /// A hatch is only ever asked to shut through <see cref="IAirlockPort.Operate"/>, which the airlock refuses while anyone stands in
    /// that doorway, so a request is simply repeated until it is allowed.
    /// </para>
    /// <para>
    /// The waypoints a traveller walks are numbered: 0 the near doorway, 1 the chamber's middle, 2 the far doorway, 3 where it
    /// lands. A traveller still waiting for its turn after <c>hopAfterSeconds</c>, and unseen, hops across instead.
    /// </para>
    /// </summary>
    public sealed class AirlockTransit
    {
        public const int NearDoorway = 0, ChamberMiddle = 1, FarDoorway = 2, Landing = 3;
        private const int Across = 4;

        public enum Stage : byte { Idle, Admit, Board, Seal, Cycle, Unload, Settle }

        private sealed class Traveller
        {
            public AirlockDirection direction;
            public float arrivedAt;
            public bool admitted;
            public int reached;   // waypoints reached so far; Across when it is over
        }

        private readonly float groupWindowSeconds;
        private readonly float hopAfterSeconds;
        private readonly Dictionary<int, Traveller> travellers = new();
        private AirlockDirection direction;

        public AirlockTransit(float groupWindowSeconds, float hopAfterSeconds)
        {
            this.groupWindowSeconds = groupWindowSeconds;
            this.hopAfterSeconds = hopAfterSeconds;
        }

        public Stage Current { get; private set; }

        public int Count => travellers.Count;

        private AirlockSide Near => direction == AirlockDirection.Inward ? AirlockSide.Outer : AirlockSide.Inner;
        private AirlockSide Far => direction == AirlockDirection.Inward ? AirlockSide.Inner : AirlockSide.Outer;

        /// <summary>A traveller stands at one end of the link. Repeated calls change nothing.</summary>
        public void Arrive(int traveller, AirlockDirection heading, float now)
        {
            if (!travellers.ContainsKey(traveller))
                travellers[traveller] = new Traveller { direction = heading, arrivedAt = now };
        }

        /// <summary>The traveller is gone from the crossing (landed, hopped, teleported, destroyed).</summary>
        public void Leave(int traveller) => travellers.Remove(traveller);

        /// <summary>The traveller reached the waypoint it was told to walk to.</summary>
        public void Reached(int traveller)
        {
            if (travellers.TryGetValue(traveller, out Traveller one)) one.reached++;
        }

        public TransitOrder OrderFor(int traveller)
        {
            if (!travellers.TryGetValue(traveller, out Traveller one) || !one.admitted) return new TransitOrder(TransitOrder.Kind.Wait);
            if (one.reached >= Across) return new TransitOrder(TransitOrder.Kind.Done);

            bool boarding = (Current is Stage.Board or Stage.Seal or Stage.Cycle) && one.reached < FarDoorway;
            bool unloading = Current == Stage.Unload && one.reached >= FarDoorway;
            return boarding || unloading ? new TransitOrder(TransitOrder.Kind.Walk, one.reached) : new TransitOrder(TransitOrder.Kind.Wait);
        }

        /// <summary>Whether this traveller has waited out its turn unseen, and has not yet set foot in the airlock.</summary>
        public bool ShouldHop(int traveller, float now, IAirlockPort port) =>
            travellers.TryGetValue(traveller, out Traveller one) && one.reached == 0 && now - one.arrivedAt >= hopAfterSeconds &&
            port.Unobserved(traveller);

        public void Tick(float now, IAirlockPort port)
        {
            // A few stages can follow one another in a tick; the bound is the number of stages, so a bug cannot spin here.
            for (int step = 0; step < 8 && Advance(now, port); step++) { }
        }

        private bool Advance(float now, IAirlockPort port)
        {
            AirlockState state = port.State;
            // Everyone who was crossing has landed, hopped or gone: nothing is left to wait for, only hatches to shut.
            if ((Current is Stage.Admit or Stage.Board or Stage.Seal or Stage.Cycle or Stage.Unload) && !AnyAdmitted())
                return Become(Stage.Settle);

            switch (Current)
            {
                case Stage.Idle:
                    return BeginGroup(now);

                case Stage.Admit:
                    if (!state.IsOpen(Near)) { port.Operate(Near, false); return false; }
                    return port.IsFullyOpen(Near) && Become(Stage.Board);

                case Stage.Board:
                    if (!state.IsOpen(Near)) return Become(Stage.Admit);
                    return AllReached(ChamberMiddle + 1) && Become(Stage.Seal);

                case Stage.Seal:
                    if (state.IsOpen(Near)) { port.Operate(Near, true); return false; }
                    return port.IsShut(Near) && Become(Stage.Cycle);

                case Stage.Cycle:
                    if (!state.IsOpen(Far))
                    {
                        if (port.IsShut(Near) && !state.Busy) port.Operate(Far, true);
                        return false;
                    }
                    return port.IsFullyOpen(Far) && Become(Stage.Unload);

                case Stage.Unload:
                    // Somebody shut the far hatch while the group was still in the chamber: back to opening it.
                    if (!state.IsOpen(Far) && AnyWaitingInChamber()) return Become(Stage.Cycle);
                    return AllReached(Across) && Become(Stage.Settle);

                default:
                    return Settle(port);
            }
        }

        // Travellers waiting on the group's way and past the window are admitted together.
        private bool BeginGroup(float now)
        {
            Traveller first = null;
            foreach (Traveller one in travellers.Values)
                if (first == null || one.arrivedAt < first.arrivedAt) first = one;
            if (first == null || now - first.arrivedAt < groupWindowSeconds) return false;

            direction = first.direction;
            foreach (Traveller one in travellers.Values)
                if (one.direction == direction) one.admitted = true;
            return Become(Stage.Admit);
        }

        private bool Settle(IAirlockPort port)
        {
            AirlockState state = port.State;
            if (state.IsOpen(AirlockSide.Outer)) port.Operate(AirlockSide.Outer, false);
            else if (state.IsOpen(AirlockSide.Inner)) port.Operate(AirlockSide.Inner, false);
            else if (port.IsShut(AirlockSide.Outer) && port.IsShut(AirlockSide.Inner)) return Become(Stage.Idle);
            return false;
        }

        private bool AnyAdmitted()
        {
            foreach (Traveller one in travellers.Values)
                if (one.admitted) return true;
            return false;
        }

        private bool AnyWaitingInChamber()
        {
            foreach (Traveller one in travellers.Values)
                if (one.admitted && one.reached == FarDoorway) return true;
            return false;
        }

        private bool AllReached(int waypoints)
        {
            foreach (Traveller one in travellers.Values)
                if (one.admitted && one.reached < waypoints) return false;
            return true;
        }

        private bool Become(Stage next)
        {
            Current = next;
            return true;
        }
    }
}
