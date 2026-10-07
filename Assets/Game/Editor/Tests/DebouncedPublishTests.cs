using System.Threading.Tasks;
using NUnit.Framework;
using SpaceGame.Core.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="DebouncedPublish{T}"/> in isolation. Every sender here is <c>Task.CompletedTask</c>,
    /// so awaiting it never yields and each <c>Tick</c> call finishes synchronously — no PlayMode
    /// loop or real async plumbing required to see what it sent.
    /// </summary>
    public class DebouncedPublishTests
    {
        [Test]
        public void Tick_SendsNothingBeforeTheDelayElapses()
        {
            var publisher = new DebouncedPublish<int>(1f);
            publisher.Request(5);

            bool sent = false;
            publisher.Tick(0.5f, _ => { sent = true; return Task.CompletedTask; });

            Assert.IsFalse(sent, "half the delay has passed; nothing should have gone out yet");
        }

        [Test]
        public void Tick_SendsOnceThePressesStop()
        {
            var publisher = new DebouncedPublish<int>(1f);
            publisher.Request(5);

            int? sent = null;
            publisher.Tick(0.6f, v => { sent = v; return Task.CompletedTask; });
            Assert.IsNull(sent, "still short of the delay");

            publisher.Tick(0.6f, v => { sent = v; return Task.CompletedTask; });
            Assert.AreEqual(5, sent, "the clock has now run out with no further press");
        }

        [Test]
        public void Request_ABurstOnlySendsTheLastValue()
        {
            var publisher = new DebouncedPublish<int>(1f);

            // Each Request restarts the clock, the same way a burst of arrow presses would.
            publisher.Request(1);
            publisher.Request(2);
            publisher.Request(3);

            int sentCount = 0;
            int lastSent = -1;
            publisher.Tick(2f, v => { sentCount++; lastSent = v; return Task.CompletedTask; });

            Assert.AreEqual(1, sentCount, "a burst is one send, not one per press");
            Assert.AreEqual(3, lastSent, "only the last requested value is worth publishing");
        }

        [Test]
        public void Tick_ASecondCallWithNothingPendingSendsNothing()
        {
            var publisher = new DebouncedPublish<int>(1f);
            publisher.Request(5);

            int sentCount = 0;
            publisher.Tick(2f, _ => { sentCount++; return Task.CompletedTask; });
            publisher.Tick(2f, _ => { sentCount++; return Task.CompletedTask; });

            Assert.AreEqual(1, sentCount, "the first tick already sent and cleared the pending value");
        }

        [Test]
        public void Cancel_ForgetsThePendingValue()
        {
            var publisher = new DebouncedPublish<int>(1f);
            publisher.Request(5);
            publisher.Cancel();

            bool sent = false;
            publisher.Tick(10f, _ => { sent = true; return Task.CompletedTask; });

            Assert.IsFalse(sent, "Cancel must drop the pending value outright");
        }

        [Test]
        public void TryPeek_ReportsThePendingValueWithoutConsumingIt()
        {
            var publisher = new DebouncedPublish<int>(1f);
            publisher.Request(7);

            Assert.IsTrue(publisher.TryPeek(out int value));
            Assert.AreEqual(7, value);

            // Peeking is read-only: a Tick afterwards must still see the same value pending.
            Assert.IsTrue(publisher.TryPeek(out int again));
            Assert.AreEqual(7, again);
        }

        [Test]
        public void TryPeek_ReportsFalseWhenNothingIsPending()
        {
            var publisher = new DebouncedPublish<int>(1f);
            Assert.IsFalse(publisher.TryPeek(out _));
        }
    }
}
