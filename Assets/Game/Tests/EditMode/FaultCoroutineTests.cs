using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// Driven by hand with MoveNext rather than by StartCoroutine: this project has no play-mode
    /// tests, and the wrapper is a plain IEnumerator, so marching it in a loop tests exactly the
    /// thing Unity would run.
    /// </summary>
    public class FaultCoroutineTests
    {
        private GameObject go;
        private Transform owner;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("owner");
            owner = go.transform;
            Fault.ResetForPlaySession();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            Fault.ResetForPlaySession();
        }

        private static int Drain(IEnumerator routine)
        {
            int steps = 0;
            while (routine.MoveNext()) steps++;
            return steps;
        }

        private static IEnumerator Counts(List<int> into, int n)
        {
            for (int i = 0; i < n; i++) { into.Add(i); yield return null; }
        }

        private static IEnumerator ThrowsAfter(List<int> into, int n)
        {
            for (int i = 0; i < n; i++) { into.Add(i); yield return null; }
            throw new InvalidOperationException("boom");
        }

        [Test]
        public void APassingRoutineRunsToCompletionUnchanged()
        {
            var seen = new List<int>();
            Drain(Fault.Coroutine(owner, "site", Counts(seen, 3)));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, seen);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }

        [Test]
        public void AThrowEndsTheRoutineInsteadOfEscaping()
        {
            var seen = new List<int>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            Assert.DoesNotThrow(() => Drain(Fault.Coroutine(owner, "site", ThrowsAfter(seen, 2))));

            CollectionAssert.AreEqual(new[] { 0, 1 }, seen, "work before the throw still happened");
            Assert.AreEqual(1, FaultLedger.TotalFaults);
        }

        [Test]
        public void TheTeardownRunsWhenTheRoutineThrows()
        {
            bool tornDown = false;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            Drain(Fault.Coroutine(owner, "site", ThrowsAfter(new List<int>(), 1), () => tornDown = true));

            Assert.IsTrue(tornDown, "this is the whole point: a dead routine must still give back what it took");
        }

        [Test]
        public void TheTeardownDoesNotRunWhenTheRoutineFinishes()
        {
            bool tornDown = false;
            Drain(Fault.Coroutine(owner, "site", Counts(new List<int>(), 2), () => tornDown = true));

            Assert.IsFalse(tornDown, "a routine that ended normally has already cleaned up after itself");
        }

        [Test]
        public void ABrokenTeardownCannotEscapeEither()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));

            Assert.DoesNotThrow(() => Drain(Fault.Coroutine(
                owner, "site", ThrowsAfter(new List<int>(), 1),
                () => throw new InvalidOperationException("cleanup is broken too"))));
        }

        [Test]
        public void YieldedValuesArePassedThrough()
        {
            IEnumerator Yields() { yield return "first"; yield return "second"; }

            IEnumerator wrapped = Fault.Coroutine(owner, "site", Yields());

            Assert.IsTrue(wrapped.MoveNext());
            Assert.AreEqual("first", wrapped.Current, "a WaitForSeconds must reach Unity unchanged");
            Assert.IsTrue(wrapped.MoveNext());
            Assert.AreEqual("second", wrapped.Current);
            Assert.IsFalse(wrapped.MoveNext());
        }
    }
}
