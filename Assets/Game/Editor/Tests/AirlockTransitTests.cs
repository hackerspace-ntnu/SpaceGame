using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How colonists cross a colony airlock, with no scene: a real <see cref="AirlockCycle"/> behind a fake port, travellers that
    /// walk their waypoints in a fixed time, and the same interlock assertions as <see cref="AirlockCycleTests"/>.
    /// </summary>
    public class AirlockTransitTests
    {
        private const float Step = 0.25f;
        private const float GroupWindow = 1f;
        private const float HopAfter = 30f;

        private sealed class Port : IAirlockPort
        {
            public readonly AirlockCycle Cycle = new();
            public bool InnerDoorway, OuterDoorway;
            public bool Unseen = true;

            public AirlockState State => Cycle.State;
            public bool IsFullyOpen(AirlockSide side) => State.IsOpen(side);
            public bool IsShut(AirlockSide side) => !State.IsOpen(side);
            public bool Unobserved(int traveller) => Unseen;

            public AirlockRefusal Operate(AirlockSide side, bool fromChamber) =>
                Cycle.Operate(side, fromChamber, InnerDoorway, OuterDoorway);
        }

        private sealed class World
        {
            public readonly Port Port = new();
            public readonly AirlockTransit Transit = new(GroupWindow, HopAfter);
            public readonly Dictionary<int, AirlockDirection> Heading = new();
            public readonly HashSet<int> Landed = new();
            public readonly HashSet<int> Hopped = new();
            public float Now;

            public void Add(int traveller, AirlockDirection heading)
            {
                Heading[traveller] = heading;
                Transit.Arrive(traveller, heading, Now);
            }

            // Every walk takes one step; a traveller in the doorways is "in" them for the player-zone check.
            public void Run(float seconds)
            {
                for (float t = 0f; t < seconds; t += Step)
                {
                    Now += Step;
                    Port.Cycle.Tick(Step, !Port.State.InnerOpen, !Port.State.OuterOpen);
                    Transit.Tick(Now, Port);
                    foreach (int id in new List<int>(Heading.Keys))
                    {
                        if (Landed.Contains(id) || Hopped.Contains(id)) continue;
                        if (Transit.ShouldHop(id, Now, Port)) { Hopped.Add(id); Transit.Leave(id); continue; }

                        TransitOrder order = Transit.OrderFor(id);
                        if (order.Step == TransitOrder.Kind.Walk) Transit.Reached(id);
                        else if (order.Step == TransitOrder.Kind.Done) { Landed.Add(id); Transit.Leave(id); }
                    }
                    Assert.IsFalse(Port.State.InnerOpen && Port.State.OuterOpen, "both hatches were open at once");
                }
            }
        }

        [Test]
        public void ALoneColonistComesInThroughBothHatchesAndLeavesBothShut()
        {
            var w = new World();
            w.Add(1, AirlockDirection.Inward);
            w.Run(40f);

            Assert.IsTrue(w.Landed.Contains(1), "the colonist never got across");
            Assert.IsFalse(w.Port.State.InnerOpen);
            Assert.IsFalse(w.Port.State.OuterOpen);
            Assert.AreEqual(AirlockTransit.Stage.Idle, w.Transit.Current);
            Assert.IsFalse(w.Port.State.Vented, "the chamber should hold the room's air once the inner hatch has been used");
        }

        [Test]
        public void ALoneColonistGoesOutThroughTheInnerHatchFirstAndLeavesTheChamberVented()
        {
            var w = new World();
            w.Add(1, AirlockDirection.Outward);
            w.Run(40f);

            Assert.IsTrue(w.Landed.Contains(1));
            Assert.IsFalse(w.Port.State.InnerOpen);
            Assert.IsFalse(w.Port.State.OuterOpen);
            Assert.IsTrue(w.Port.State.Vented);
        }

        [Test]
        public void ColonistsWhoArriveWithinTheWindowCrossOnOneCycle()
        {
            var w = new World();
            w.Add(1, AirlockDirection.Inward);
            w.Run(0.5f);
            w.Add(2, AirlockDirection.Inward);
            w.Add(3, AirlockDirection.Inward);

            int cycles = 0;
            AirlockPhase last = AirlockPhase.Settled;
            for (int i = 0; i < 160; i++)
            {
                w.Run(Step);
                if (w.Port.State.Phase != last && w.Port.State.Phase == AirlockPhase.Pressurising) cycles++;
                last = w.Port.State.Phase;
            }

            Assert.AreEqual(3, w.Landed.Count, "all three should have crossed");
            Assert.AreEqual(1, cycles, "a group crosses on one pressurise");
        }

        [Test]
        public void SomeoneBoundTheOtherWayWaitsForTheGroupAndThenGoesAlone()
        {
            var w = new World();
            w.Add(1, AirlockDirection.Inward);
            w.Add(2, AirlockDirection.Outward);
            w.Run(80f);

            Assert.IsTrue(w.Landed.Contains(1) && w.Landed.Contains(2), "both directions should be served in turn");
            Assert.IsFalse(w.Port.State.InnerOpen || w.Port.State.OuterOpen);
        }

        [Test]
        public void AHatchIsNeverShutWhileAnyoneStandsInItsDoorway()
        {
            var w = new World();
            w.Port.OuterDoorway = true;   // a player stands in the outer doorway the whole time
            w.Add(1, AirlockDirection.Inward);
            w.Run(20f);

            // Admitting opens the outer hatch; it must still be open, because the group cannot shut it on the player.
            Assert.IsTrue(w.Port.State.OuterOpen, "the outer hatch was shut on someone in its doorway");
            Assert.IsFalse(w.Landed.Contains(1));

            w.Port.OuterDoorway = false;
            w.Run(40f);
            Assert.IsTrue(w.Landed.Contains(1), "once the doorway cleared the crossing should finish");
        }

        [Test]
        public void ABusyAirlockQueuesTheColonistInsteadOfFailingIt()
        {
            var w = new World();
            // A player already opened the inner hatch to leave it open: the group's near hatch needs it sealed first.
            w.Port.Cycle.Operate(AirlockSide.Inner, false, false, false);
            w.Run(1f);
            Assert.IsTrue(w.Port.State.InnerOpen);

            w.Add(1, AirlockDirection.Inward);
            w.Run(60f);
            Assert.IsTrue(w.Landed.Contains(1));
        }

        [Test]
        public void AColonistNobodyCanSeeHopsAcrossOnceItsTurnHasNotComeInTime()
        {
            var w = new World();
            w.Port.InnerDoorway = true;
            w.Port.OuterDoorway = true;   // the airlock is held shut for it, as a player holding it would
            w.Port.Cycle.Operate(AirlockSide.Inner, false, false, false);
            w.Run(1f);

            w.Add(1, AirlockDirection.Inward);
            w.Run(HopAfter + 2f);
            Assert.IsTrue(w.Hopped.Contains(1), "an unseen colonist whose turn never came should have hopped");
        }

        [Test]
        public void AWatchedColonistKeepsWaitingInsteadOfHopping()
        {
            var w = new World();
            w.Port.Unseen = false;
            w.Add(1, AirlockDirection.Inward);

            Assert.IsFalse(w.Transit.ShouldHop(1, w.Now + HopAfter * 2f, w.Port));
        }

        [Test]
        public void ANearHatchShutBySomebodyElseMidBoardingSendsTheGroupBackToOpenIt()
        {
            var w = new World();
            w.Add(1, AirlockDirection.Inward);
            for (int i = 0; i < 100 && w.Transit.Current != AirlockTransit.Stage.Board; i++) w.Run(Step);
            Assert.AreEqual(AirlockTransit.Stage.Board, w.Transit.Current);

            w.Port.Cycle.Operate(AirlockSide.Outer, false, false, false);   // a player shuts it
            w.Run(Step);
            Assert.AreEqual(AirlockTransit.Stage.Admit, w.Transit.Current);
        }
    }
}
