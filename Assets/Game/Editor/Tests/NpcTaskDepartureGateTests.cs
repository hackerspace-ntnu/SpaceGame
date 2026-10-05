// The departure gate: something outside the task loop (a walking city's crew) can keep the NPC
// where it is -- both at the end of a stay and before it picks its next destination.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NpcTaskDepartureGateTests
    {
        private GameObject go;
        private NpcTaskModule tasks;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Leader");
            tasks = go.AddComponent<NpcTaskModule>();
            // EditMode AddComponent does not run Awake, and Awake is what finds the AgentGoal.
            typeof(NpcTaskModule).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(tasks, null);
            tasks.ResumeTask(new[] { new NpcTask { targetSite = SiteKind.Ruin, dwellSeconds = new Vector2(1f, 1f) } }, -1, travelling: false, dwellRemaining: 0f, siteId: null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        private void Dwell(float timeLeft) =>
            tasks.RestoreTaskState(NpcTaskModule.Phase.Dwelling, 0, "site", "site-1", timeLeft, 0f, -1, true, Vector3.zero);

        private void Tick(float dt) => tasks.Tick(default, dt);

        [Test]
        public void AtStop_WhileDwellTimeRemains()
        {
            Dwell(5f);
            Assert.IsTrue(tasks.AtStop);
            Tick(6f);
            Assert.IsFalse(tasks.AtStop, "the stay is over even if the NPC has not left yet");
        }

        [Test]
        public void AClosedGate_HoldsTheDwellPastItsTime()
        {
            bool open = false;
            tasks.SetDepartureGate(() => open);
            Dwell(1f);

            Tick(5f);
            Assert.AreEqual(NpcTaskModule.Phase.Dwelling, tasks.CurrentPhase, "held while the gate is shut");

            open = true;
            Tick(0.1f);
            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
        }

        [Test]
        public void AClosedGate_HoldsChoosing_SoARespawnedLeaderWaitsForItsCrew()
        {
            tasks.SetDepartureGate(() => false);
            tasks.ResumeTask(new[] { new NpcTask { targetSite = SiteKind.Ruin } }, -1, travelling: false, dwellRemaining: 0f, siteId: null);

            Tick(1f);

            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
            Assert.IsFalse(go.GetComponent<AgentGoal>().HasGoal, "no destination while held");
        }

        [Test]
        public void NoGate_BehavesAsBefore()
        {
            Dwell(1f);
            Tick(2f);
            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
        }
    }
}
