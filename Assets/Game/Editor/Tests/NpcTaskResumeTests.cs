// A caravan leader spawned from its NpcWorldSim record resumes the record's errand in the phase the
// record was in -- see NpcTaskModule.ResumeTask. It used to start in Choosing, and the planner avoids
// the current index, so the first tick picked a different task and overwrote the spawn goal.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NpcTaskResumeTests
    {
        private static readonly Vector3 Destination = new Vector3(100f, 0f, 0f);

        private GameObject leader;
        private NpcTaskModule tasks;
        private AgentGoal goal;
        private NpcTask[] errands;

        [SetUp]
        public void SetUp()
        {
            leader = new GameObject("NpcTaskResumeTestLeader");
            tasks = leader.AddComponent<NpcTaskModule>();
            typeof(NpcTaskModule).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                 .Invoke(tasks, null);
            goal = leader.GetComponent<AgentGoal>();
            tasks.SetHome(Vector3.zero);

            // Two equal weights: a re-roll that avoids the current index is guaranteed to change it.
            errands = new[] { new NpcTask { label = "trade" }, new NpcTask { label = "scavenge" } };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(leader);

        private void Tick(float deltaTime) => tasks.Tick(new AgentContext { Self = leader.transform }, deltaTime);

        [Test]
        public void ResumingATravellingRecordKeepsTheTaskAndTheGoal()
        {
            goal.Set(Destination, 2f);
            tasks.ResumeTask(errands, 0, travelling: true, dwellRemaining: 0f, siteId: "camp-7");

            Tick(0.1f);

            Assert.AreEqual(0, tasks.CurrentTaskIndex, "the leader must keep the task its record was on");
            Assert.AreEqual(NpcTaskModule.Phase.Travelling, tasks.CurrentPhase);
            Assert.IsTrue(goal.HasGoal);
            Assert.AreEqual(Destination, goal.Position, "the spawn goal must not be overwritten by a re-roll");
            Assert.AreEqual("camp-7", tasks.LastSiteId);
        }

        [Test]
        public void ResumingARecordNamesTheSiteItIsBoundFor()
        {
            WorldSiteRegistry.Register(SiteKind.Camp, Destination, 6f, "Ash Camp", id: "npc-task-resume-camp");
            try
            {
                tasks.ResumeTask(errands, 0, travelling: true, dwellRemaining: 0f, siteId: "npc-task-resume-camp");

                Assert.AreEqual("Ash Camp", tasks.CurrentDestinationName,
                                "the {destination} token must not go blank for the resumed leg");
            }
            finally
            {
                WorldSiteRegistry.Unregister("npc-task-resume-camp");
            }
        }

        [Test]
        public void ResumingADwellingRecordFinishesTheDwell()
        {
            tasks.ResumeTask(errands, 1, travelling: false, dwellRemaining: 5f, siteId: "well-2");

            Tick(1f);

            Assert.AreEqual(NpcTaskModule.Phase.Dwelling, tasks.CurrentPhase);
            Assert.AreEqual(1, tasks.CurrentTaskIndex);
            Assert.AreEqual(4f, tasks.PhaseTimer, 1e-4f, "the record's remaining dwell, counting down");
            Assert.IsFalse(goal.HasGoal, "a dwelling NPC has no goal, so wander takes the frame");

            Tick(4.5f);

            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase, "the dwell ends on schedule");
        }

        [Test]
        public void ResumingAnIdleRecordChooses()
        {
            tasks.ResumeTask(errands, -1, travelling: false, dwellRemaining: 0f, siteId: null);

            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
            Assert.AreEqual(string.Empty, tasks.LastSiteId);
        }

        [Test]
        public void FoldingADwellingLeaderKeepsTheDwellAndDropsTheStaleGoal()
        {
            tasks.ResumeTask(errands, 1, travelling: false, dwellRemaining: 30f, siteId: "well-2");
            var record = new NpcGroup { HasGoal = true, GoalPosition = Destination };

            NpcWorldSim.ReadTaskBack(record, tasks);

            Assert.AreEqual(1, record.TaskIndex);
            Assert.AreEqual(30f, record.DwellRemaining, 1e-4f);
            Assert.AreEqual("well-2", record.LastSiteId);
            Assert.IsFalse(record.HasGoal, "a folded group with its old goal walks back to a site it already reached");
        }

        [Test]
        public void FoldingATravellingLeaderKeepsTheGoal()
        {
            goal.Set(Destination, 2f);
            tasks.ResumeTask(errands, 0, travelling: true, dwellRemaining: 0f, siteId: "camp-7");
            var record = new NpcGroup { HasGoal = true, GoalPosition = Destination, DwellRemaining = 12f };

            NpcWorldSim.ReadTaskBack(record, tasks);

            Assert.IsTrue(record.HasGoal);
            Assert.AreEqual(0f, record.DwellRemaining, "a travelling group is not also dwelling");
            Assert.AreEqual(0, record.TaskIndex);
        }

        [Test]
        public void FoldingALeaderWithoutTasksLeavesTheDirectorsGoal()
        {
            tasks.ResumeTask(null, -1, travelling: false, dwellRemaining: 0f, siteId: null);
            var record = new NpcGroup { HasGoal = true, GoalPosition = Destination };

            NpcWorldSim.ReadTaskBack(record, tasks);

            Assert.IsTrue(record.HasGoal, "a war party's goal is the director's, not an errand's");
        }
    }
}
