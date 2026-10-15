using NUnit.Framework;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The colony airlock's rules, with no scene: clicks on hatches in, which hatch is open and what the air does out.
    /// </summary>
    public class AirlockCycleTests
    {
        private const float Step = 0.25f;

        private sealed class Lock
        {
            public readonly AirlockCycle Cycle = new();
            public bool InnerDoorway, OuterDoorway;

            /// <summary>
            /// A leaf that takes this long to swing shut after being told to. Zero: hatches report shut the moment
            /// they are told shut.
            /// </summary>
            public float InnerShutLag;

            /// <summary>Seconds the inner leaves have been told shut; they start long shut.</summary>
            private float innerShutFor = float.PositiveInfinity;

            public AirlockState State => Cycle.State;

            public AirlockRefusal Click(AirlockSide hatch, bool fromChamber) =>
                Cycle.Operate(hatch, fromChamber, InnerDoorway, OuterDoorway);

            /// <summary>Ticks for <paramref name="seconds"/>, asserting the interlock on every step.</summary>
            public void Run(float seconds)
            {
                for (float t = 0f; t < seconds; t += Step)
                {
                    innerShutFor = State.InnerOpen ? 0f : innerShutFor + Step;
                    Cycle.Tick(Step, innerShutFor >= InnerShutLag, !State.OuterOpen);
                    if (State.InnerOpen) innerShutFor = 0f;
                    Assert.IsFalse(State.InnerOpen && State.OuterOpen, "both hatches were open at once");
                }
            }
        }

        [Test]
        public void ComingInFromOutsideVentsOpensAndMustBeShutBehindYouBeforeTheInnerHatchPressurises()
        {
            var l = new Lock();
            Assert.AreEqual(AirlockRefusal.None, l.Click(AirlockSide.Outer, fromChamber: false));
            l.Run(Step);
            Assert.AreEqual(AirlockPhase.Venting, l.State.Phase);
            Assert.IsFalse(l.State.OuterOpen, "the outer hatch opened before the chamber vented");

            l.Run(l.Cycle.CycleSeconds);
            Assert.IsTrue(l.State.OuterOpen);
            Assert.IsTrue(l.State.Vented);

            // In the chamber, the outer hatch still standing open behind them.
            Assert.AreEqual(AirlockRefusal.CloseOuterFirst, l.Click(AirlockSide.Inner, fromChamber: true));
            l.Run(1f);
            Assert.IsTrue(l.State.OuterOpen, "a refused click changed something");
            Assert.IsFalse(l.State.InnerOpen);

            Assert.AreEqual(AirlockRefusal.None, l.Click(AirlockSide.Outer, fromChamber: true));
            Assert.IsFalse(l.State.OuterOpen);

            Assert.AreEqual(AirlockRefusal.None, l.Click(AirlockSide.Inner, fromChamber: true));
            l.Run(Step);
            Assert.AreEqual(AirlockPhase.Pressurising, l.State.Phase);
            l.Run(l.Cycle.CycleSeconds);
            Assert.IsTrue(l.State.InnerOpen);
            Assert.IsFalse(l.State.Vented);
            Assert.AreEqual(AirlockPhase.Settled, l.State.Phase);
        }

        [Test]
        public void TheEqualisedSideOpensAtOnceAndTheOtherWaitsOutACycleThatRefusesEveryClick()
        {
            var l = new Lock();
            l.Click(AirlockSide.Inner, fromChamber: false);
            l.Run(Step);
            Assert.IsTrue(l.State.InnerOpen, "a pressurised chamber made the room wait");

            Assert.AreEqual(AirlockRefusal.CloseInnerFirst, l.Click(AirlockSide.Outer, fromChamber: true));
            l.Click(AirlockSide.Inner, fromChamber: true);
            l.Click(AirlockSide.Outer, fromChamber: true);
            l.Run(Step);

            Assert.IsTrue(l.State.Busy);
            Assert.AreEqual(AirlockRefusal.Cycling, l.Click(AirlockSide.Inner, fromChamber: true));
            Assert.IsFalse(l.State.InnerOpen || l.State.OuterOpen, "a hatch was open mid-cycle");

            l.Run(l.Cycle.CycleSeconds);
            Assert.IsTrue(l.State.OuterOpen);
            Assert.IsFalse(l.State.Busy);
        }

        [Test]
        public void AHatchLeftOpenOnTheFarSideIsSealedForSomeoneWhoCannotReachIt()
        {
            var l = new Lock { InnerShutLag = 1f };
            l.Click(AirlockSide.Inner, fromChamber: false);
            l.Run(Step);
            Assert.IsTrue(l.State.InnerOpen);

            // Someone walked out of the room, left the inner hatch open, and a second player outside wants in.
            Assert.AreEqual(AirlockRefusal.None, l.Click(AirlockSide.Outer, fromChamber: false));
            Assert.IsFalse(l.State.InnerOpen, "the far hatch was not sealed");

            l.Run(Step);
            Assert.AreEqual(AirlockPhase.Settled, l.State.Phase, "the cycle began before the inner leaves were shut");

            l.Run(l.InnerShutLag + l.Cycle.CycleSeconds + Step);
            Assert.IsTrue(l.State.OuterOpen);
        }

        [Test]
        public void AHatchIsNeverToldToShutOnSomeoneStandingInItsDoorway()
        {
            var l = new Lock();
            l.Click(AirlockSide.Inner, fromChamber: false);
            l.Run(Step);

            l.InnerDoorway = true;
            Assert.AreEqual(AirlockRefusal.DoorwayOccupied, l.Click(AirlockSide.Inner, fromChamber: true));
            Assert.AreEqual(AirlockRefusal.FarDoorwayOccupied, l.Click(AirlockSide.Outer, fromChamber: false));
            l.Run(5f);
            Assert.IsTrue(l.State.InnerOpen);

            l.InnerDoorway = false;
            Assert.AreEqual(AirlockRefusal.None, l.Click(AirlockSide.Inner, fromChamber: true));
            Assert.IsFalse(l.State.InnerOpen);
        }

        [Test]
        public void TheWholeStateSurvivesTheWire()
        {
            var l = new Lock();
            l.Click(AirlockSide.Outer, fromChamber: false);
            l.Run(Step);

            AirlockState sent = l.State;
            AirlockState received = AirlockState.FromWire(sent.ToWire());
            Assert.AreEqual(sent, received);
            Assert.AreEqual(AirlockSide.Outer, received.Pending);
            Assert.AreEqual(AirlockPhase.Venting, received.Phase);

            var client = new AirlockCycle();
            client.Adopt(received);
            Assert.AreEqual(AirlockRefusal.Cycling, client.Check(AirlockSide.Inner, false, false, false),
                            "a client that adopted a cycling state would let a click through");
        }
    }
}
