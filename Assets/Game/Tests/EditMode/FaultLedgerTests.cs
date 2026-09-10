using NUnit.Framework;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// The ledger is what a player's bug report is made of. It has to be bounded — a fault every
    /// frame for an hour must not grow without limit — and it has to keep the newest, because the
    /// thing that just broke is the thing being reported.
    /// </summary>
    public class FaultLedgerTests
    {
        [SetUp]
        public void Reset() => FaultLedger.Clear();

        [TearDown]
        public void Cleanup() => FaultLedger.Clear();

        [Test]
        public void RecordsArriveOldestFirst()
        {
            FaultLedger.Add(new FaultRecord("s", "a", "d", 1, false, 0f));
            FaultLedger.Add(new FaultRecord("s", "b", "d", 1, false, 1f));

            Assert.AreEqual(2, FaultLedger.Recent.Count);
            Assert.AreEqual("a", FaultLedger.Recent[0].Owner);
            Assert.AreEqual("b", FaultLedger.Recent[1].Owner);
        }

        [Test]
        public void DropsTheOldestPastCapacity()
        {
            for (int i = 0; i < FaultLedger.Capacity + 5; i++)
                FaultLedger.Add(new FaultRecord("s", $"owner{i}", "d", 1, false, i));

            Assert.AreEqual(FaultLedger.Capacity, FaultLedger.Recent.Count);
            Assert.AreEqual("owner5", FaultLedger.Recent[0].Owner, "the first five should have been dropped");
        }

        [Test]
        public void CountsTotalFaultsBeyondWhatItKeeps()
        {
            for (int i = 0; i < FaultLedger.Capacity + 5; i++)
                FaultLedger.Add(new FaultRecord("s", "o", "d", 1, false, i));

            Assert.AreEqual(FaultLedger.Capacity + 5, FaultLedger.TotalFaults,
                            "the count must not be the buffer length — that would hide how bad a session got");
        }

        [Test]
        public void ClearEmptiesBothTheBufferAndTheCount()
        {
            FaultLedger.Add(new FaultRecord("s", "o", "d", 1, false, 0f));
            FaultLedger.Clear();

            Assert.AreEqual(0, FaultLedger.Recent.Count);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }
    }
}
