using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Diagnostics;

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
    }
}
