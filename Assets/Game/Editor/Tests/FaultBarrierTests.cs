using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Diagnostics;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The barrier contract: a throwing body is contained, reported loudly, and — once it has proved
    /// it is not a one-off — switched off. Loudly matters as much as contained: a guard that hides
    /// the bug it caught is worse than no guard, because the bug then ships.
    /// </summary>
    public class FaultBarrierTests
    {
        private sealed class Thrower : MonoBehaviour
        {
            public int Calls;
            public void Boom() { Calls++; throw new InvalidOperationException("boom"); }
        }

        private sealed class Shedder : MonoBehaviour, IQuarantinable
        {
            public int Shed;
            public void OnQuarantined() => Shed++;
        }

        private readonly List<GameObject> spawned = new();

        private T Make<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        [SetUp]
        public void Reset() => Fault.ResetForPlaySession();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            Fault.ResetForPlaySession();
        }

        [Test]
        public void ABodyThatDoesNotThrowReportsSuccess()
        {
            bool ran = false;
            Assert.IsTrue(Fault.Run(Make<Thrower>(), "site", () => ran = true));
            Assert.IsTrue(ran);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }

        [Test]
        public void AThrowIsContainedAndReported()
        {
            Thrower t = Make<Thrower>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\].*site"));
            Assert.IsFalse(Fault.Run(t, "site", t.Boom), "a contained throw reports failure to the caller");

            Assert.AreEqual(1, t.Calls);
            Assert.AreEqual(1, FaultLedger.TotalFaults, "and it lands in the ledger");
        }

        [Test]
        public void RepeatedThrowsQuarantineTheBehaviour()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "site", t.Boom);
            }

            Assert.IsTrue(Fault.IsQuarantined(t, "site"));
            Assert.IsFalse(t.enabled, "a quarantined Behaviour is switched off so it stops being ticked");
        }

        [Test]
        public void AQuarantinedSiteIsNotRunAgain()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "site", t.Boom);
            }

            int callsAtQuarantine = t.Calls;
            Assert.IsFalse(Fault.Run(t, "site", t.Boom));
            Assert.AreEqual(callsAtQuarantine, t.Calls, "the body must not be entered once quarantined");
        }

        [Test]
        public void AQuarantinableShedsItsOwnPartInsteadOfBeingDisabled()
        {
            Shedder s = Make<Shedder>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(s, "site", () => throw new InvalidOperationException("boom"));
            }

            Assert.AreEqual(1, s.Shed, "OnQuarantined runs exactly once");
            Assert.IsTrue(s.enabled, "a component that sheds its own part keeps running the rest");
        }

        [Test]
        public void QuarantineRaisesTheEventOnce()
        {
            Thrower t = Make<Thrower>();
            int raised = 0;
            Fault.Quarantined += _ => raised++;

            try
            {
                for (int i = 0; i < Fault.MaxFaultsPerWindow + 3; i++)
                {
                    if (Fault.IsQuarantined(t, "site")) { Fault.Run(t, "site", t.Boom); continue; }
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                    Fault.Run(t, "site", t.Boom);
                }
            }
            finally
            {
                Fault.ResetForPlaySession();
            }

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void TwoSitesOnOneComponentAreIndependent()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "one", t.Boom);
            }

            Assert.IsTrue(Fault.IsQuarantined(t, "one"));
            Assert.IsFalse(Fault.IsQuarantined(t, "two"),
                           "a broken Present must not take Use down with it");
        }

        [Test]
        public void ADestroyedOwnerIsNotTouched()
        {
            Thrower t = Make<Thrower>();
            UnityEngine.Object.DestroyImmediate(t.gameObject);

            Assert.IsFalse(Fault.Run(t, "site", () => { }), "nothing to run a body on");
            Assert.AreEqual(0, FaultLedger.TotalFaults, "and a dead owner is not a fault");
        }
    }
}
