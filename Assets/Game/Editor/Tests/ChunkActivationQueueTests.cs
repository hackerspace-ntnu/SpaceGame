using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.World.Tests
{
    /// <summary>
    /// Covers the budget contract of <see cref="ChunkActivationQueue"/>, which is the whole point of
    /// it: a chunk's construction has to be spread over frames without ever stalling one, and it
    /// has to finish.
    ///
    /// Uses its own queue instance rather than <see cref="ChunkActivationQueue.Shared"/> so no
    /// runner GameObject is created and the tests cannot interfere with each other.
    /// </summary>
    public class ChunkActivationQueueTests
    {
        private static void Burn(float ms)
        {
            float until = Time.realtimeSinceStartup + ms * 0.001f;
            while (Time.realtimeSinceStartup < until) { }
        }

        [Test]
        public void EnqueuedWorkRunsOnTick()
        {
            var queue = new ChunkActivationQueue();
            bool ran = false;

            queue.Enqueue(() => ran = true, "work");

            Assert.AreEqual(1, queue.PendingCount, "work should wait for a tick");
            queue.Tick(10f);

            Assert.IsTrue(ran);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void TickStopsOnceTheBudgetIsSpent()
        {
            var queue = new ChunkActivationQueue();
            int ran = 0;

            for (int i = 0; i < 10; i++)
                queue.Enqueue(() => { ran++; Burn(4f); }, $"slow {i}");

            // 4 ms of work per task against a 5 ms budget: the first task alone overruns half of it,
            // so the tick must stop well short of draining ten of them.
            queue.Tick(5f);

            Assert.Less(ran, 10, "a single tick must not run the whole queue");
            Assert.Greater(queue.PendingCount, 0);
        }

        [Test]
        public void ATaskLongerThanTheBudgetStillRuns()
        {
            var queue = new ChunkActivationQueue();
            bool ran = false;

            queue.Enqueue(() => { ran = true; Burn(5f); }, "overrunning");

            // A zero budget must not deadlock. One task per tick is the floor, otherwise a queue of
            // tasks that each cost more than the budget would never drain at all.
            queue.Tick(0f);

            Assert.IsTrue(ran, "the queue must always make progress");
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void RepeatedTicksDrainTheQueue()
        {
            var queue = new ChunkActivationQueue();
            var order = new List<int>();

            for (int i = 0; i < 20; i++)
            {
                int captured = i;
                queue.Enqueue(() => { order.Add(captured); Burn(1f); }, $"task {i}");
            }

            for (int frame = 0; frame < 100 && queue.PendingCount > 0; frame++)
                queue.Tick(2f);

            Assert.AreEqual(0, queue.PendingCount, "queue should have drained");
            Assert.AreEqual(20, order.Count);
            CollectionAssert.IsOrdered(order, "tasks must run in the order they were enqueued");
        }

        [Test]
        public void AThrowingTaskDoesNotStopTheQueue()
        {
            var queue = new ChunkActivationQueue();
            bool laterRan = false;

            queue.Enqueue(() => throw new System.InvalidOperationException("boom"), "exploding");
            queue.Enqueue(() => laterRan = true, "after");

            // The error is logged, not swallowed silently — a chunk quietly missing its geometry is
            // exactly the failure this must not become.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("exploding"));

            while (queue.PendingCount > 0) queue.Tick(10f);

            Assert.IsTrue(laterRan, "work queued after a failure must still run");
        }

        [Test]
        public void ClearDropsPendingWork()
        {
            var queue = new ChunkActivationQueue();
            bool ran = false;

            queue.Enqueue(() => ran = true, "work");
            queue.Clear();
            queue.Tick(10f);

            Assert.IsFalse(ran);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void NullWorkIsIgnored()
        {
            var queue = new ChunkActivationQueue();

            queue.Enqueue(null, "nothing");

            Assert.AreEqual(0, queue.PendingCount);
        }
    }
}
