using NUnit.Framework;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// The budget decides when a repeatedly-throwing thing gets switched off. Its whole contract is
    /// "loud but bounded": every fault is counted, but only one of them ever trips quarantine, and a
    /// thing that throws once an hour is never quarantined at all.
    /// </summary>
    public class FaultBudgetTests
    {
        [Test]
        public void FirstFaultDoesNotQuarantine()
        {
            var budget = new FaultBudget(maxFaults: 3, windowSeconds: 10f);

            Assert.IsFalse(budget.Record("a", 0f), "one fault is not a pattern");
            Assert.IsFalse(budget.IsQuarantined("a"));
        }

        [Test]
        public void QuarantinesOnTheNthFaultInsideTheWindow()
        {
            var budget = new FaultBudget(maxFaults: 3, windowSeconds: 10f);

            Assert.IsFalse(budget.Record("a", 0f));
            Assert.IsFalse(budget.Record("a", 1f));
            Assert.IsTrue(budget.Record("a", 2f), "the third fault inside the window trips it");
            Assert.IsTrue(budget.IsQuarantined("a"));
        }

        [Test]
        public void QuarantineTripsExactlyOnce()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            Assert.IsTrue(budget.Record("a", 1f), "trips here");
            Assert.IsFalse(budget.Record("a", 2f), "and never again, or every frame logs a new quarantine");
            Assert.IsFalse(budget.Record("a", 3f));
        }

        [Test]
        public void AnOldFaultDoesNotCountTowardsANewWindow()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);

            // 30 s later. A thing that throws once every half minute is annoying, not broken, and
            // switching it off would be a worse outcome than the fault itself.
            Assert.IsFalse(budget.Record("a", 30f));
            Assert.IsFalse(budget.IsQuarantined("a"));
        }

        [Test]
        public void SitesAreCountedSeparately()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            Assert.IsFalse(budget.Record("b", 1f), "b has thrown once, not twice");
            Assert.IsFalse(budget.IsQuarantined("b"));
        }

        [Test]
        public void CountIsReportedForTheCurrentWindow()
        {
            var budget = new FaultBudget(maxFaults: 5, windowSeconds: 10f);

            budget.Record("a", 0f);
            budget.Record("a", 1f);

            Assert.AreEqual(2, budget.CountFor("a"));

            budget.Record("a", 100f);
            Assert.AreEqual(1, budget.CountFor("a"), "a new window starts a new count");
        }

        [Test]
        public void ClearForgetsEverything()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            budget.Record("a", 1f);
            Assert.IsTrue(budget.IsQuarantined("a"));

            budget.Clear();

            Assert.IsFalse(budget.IsQuarantined("a"));
            Assert.AreEqual(0, budget.CountFor("a"));
        }
    }
}
