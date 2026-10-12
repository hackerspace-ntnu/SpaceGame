using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using SpaceGame.Agents;
using SpaceGame.Diagnostics;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SpaceGame.Tests
{
    /// <summary>
    /// A creature whose highest-priority module is broken must still walk on its next one. Before
    /// the barrier, the throw escaped EvaluateModules and the whole agent stopped — every module
    /// below the broken one starved, so a bug in chasing also removed fleeing and wandering.
    ///
    /// Modules are exercised through <see cref="AgentController.RunModule"/> rather than through
    /// Update, because AddComponent outside play mode raises no Awake and the controller never
    /// resolves its motor.
    /// </summary>
    public class AgentModuleBarrierTests
    {
        private sealed class ThrowingModule : BehaviourModuleBase
        {
            public int Calls;
            public override MoveIntent? Tick(in AgentContext context, float deltaTime)
            {
                Calls++;
                throw new InvalidOperationException("module is broken");
            }
        }

        private sealed class WalkingModule : BehaviourModuleBase
        {
            public int Calls;
            public override MoveIntent? Tick(in AgentContext context, float deltaTime)
            {
                Calls++;
                return MoveIntent.Idle();
            }
        }

        private sealed class FacingModule : MonoBehaviour, IFacingModule
        {
            public bool Throws;
            public int FacingPriority => 0;
            public bool IsActive => enabled;

            public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
            {
                if (Throws) throw new InvalidOperationException("facing is broken");
                facePosition = Vector3.forward;
                return true;
            }
        }

        private GameObject agent;

        [SetUp]
        public void SetUp()
        {
            agent = new GameObject("agent");
            Fault.ResetForPlaySession();
        }

        [TearDown]
        public void TearDown()
        {
            if (agent != null) UnityEngine.Object.DestroyImmediate(agent);
            Fault.ResetForPlaySession();
        }

        [Test]
        public void AThrowingModuleReturnsNoIntentAndDoesNotEscape()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));

            MoveIntent? result = null;
            Assert.DoesNotThrow(() => result = AgentController.RunModule(broken, in context, 0.02f));

            Assert.IsNull(result, "a module that threw claimed nothing, so the next one must get the frame");
            Assert.AreEqual(1, broken.Calls);
        }

        [Test]
        public void ARepeatedlyThrowingModuleIsSwitchedOff()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                AgentController.RunModule(broken, in context, 0.02f);
            }

            Assert.IsFalse(broken.enabled);
            Assert.IsFalse(broken.IsActive, "IsActive reads enabled, so the controller stops ticking it");
        }

        [Test]
        public void AHealthyModuleIsUnaffectedByABrokenSibling()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var healthy = agent.AddComponent<WalkingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            AgentController.RunModule(broken, in context, 0.02f);

            MoveIntent? result = AgentController.RunModule(healthy, in context, 0.02f);

            Assert.IsNotNull(result, "the creature still moves");
            Assert.AreEqual(1, healthy.Calls);
        }

        private const int AllocationProbeCalls = 1000;

        // Runs `calls` once before it is measured, so JIT and first-use statics are not counted
        // against the loop. The measurement is Unity's GC.Alloc recorder (AllocatingGCMemory):
        // GC.GetAllocatedBytesForCurrentThread reads 0 under the editor's Mono whatever is allocated.
        private static TestDelegate WarmedUp(TestDelegate calls)
        {
            calls();
            return calls;
        }

        [Test]
        public void RunningAModuleAllocatesNothing()
        {
            var healthy = agent.AddComponent<WalkingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            TestDelegate calls = () =>
            {
                for (int i = 0; i < AllocationProbeCalls; i++)
                    AgentController.RunModule(healthy, in context, 0.02f);
            };

            Assert.That(WarmedUp(calls), Is.Not.AllocatingGCMemory(),
                        "RunModule runs once per module per creature per frame; any allocation here is garbage every frame");
        }

        // The control for the test above: proves the probe sees a per-call closure, so a zero there
        // is a measurement and not a runtime that cannot count. This is the shape RunModule used to have.
        [Test]
        public void TheAllocationProbeSeesAPerCallClosure()
        {
            var healthy = agent.AddComponent<WalkingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            TestDelegate calls = () =>
            {
                for (int i = 0; i < AllocationProbeCalls; i++)
                {
                    AgentContext local = context;
                    Fault.Run(healthy, "probe", () => healthy.Tick(in local, 0.02f));
                }
            };

            Assert.That(WarmedUp(calls), Is.AllocatingGCMemory());
        }

        [Test]
        public void ReadingAFacingModuleAllocatesNothing()
        {
            var module = agent.AddComponent<FacingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };
            TestDelegate reads = () => { for (int i = 0; i < 1000; i++) AgentController.RunFacing(module, in context, out _); };
            reads();   // warm up: JIT
            Assert.That(reads, Is.Not.AllocatingGCMemory());

            Assert.IsTrue(AgentController.RunFacing(module, in context, out Vector3 face));
            Assert.AreEqual(Vector3.forward, face);
        }

        [Test]
        public void AThrowingFacingModuleWantsNothingAndIsSwitchedOffLikeAModule()
        {
            var module = agent.AddComponent<FacingModule>();
            module.Throws = true;
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\].*AgentModule\\.Facing"));
                Assert.IsFalse(AgentController.RunFacing(module, in context, out _), "a throw reads as no facing");
            }

            Assert.IsFalse(module.enabled);
        }
    }
}
