// AgentController's two parking reasons (Offstage: the residents' routine; Dormant: simulation
// distance) share one parked state, and neither writer can release the other's park.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class AgentParkingTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void DormantParksTheMotorAndNeverTouchesEnabled()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Dormant = true;
            Assert.IsTrue(agent.IsParked);
            Assert.IsTrue(motor.Suspended);
            Assert.IsTrue(agent.enabled);

            agent.Dormant = false;
            Assert.IsFalse(agent.IsParked);
            Assert.IsFalse(motor.Suspended);
        }

        [Test]
        public void OffstageSurvivesDormantClearing()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Offstage = true;
            agent.Dormant = true;
            agent.Dormant = false;

            Assert.IsTrue(agent.IsParked, "the routine still has it indoors");
            Assert.IsTrue(motor.Suspended);
        }

        [Test]
        public void DormantSurvivesOffstageClearing()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Dormant = true;
            agent.Offstage = true;
            agent.Offstage = false;

            Assert.IsTrue(agent.IsParked, "still out of every player's range");
            Assert.IsTrue(motor.Suspended);
        }

        [Test]
        public void SeatedWhileDormant_WokenMidRide_WalksAgainWhenPutDown()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Dormant = true;
            agent.RidesAsPassenger = true;
            agent.Dormant = false;
            Assert.IsTrue(motor.Suspended, "seating owns the motor while it rides");

            agent.RidesAsPassenger = false;
            Assert.IsFalse(motor.Suspended, "awake and on its feet again, it must walk");
        }

        [Test]
        public void SeatedAwake_FallenAsleepMidRide_IsParkedWhenPutDown()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.RidesAsPassenger = true;
            agent.Dormant = true;
            Assert.IsTrue(agent.IsParked);
            Assert.IsFalse(motor.Suspended, "seating owns the motor while it rides");

            agent.RidesAsPassenger = false;
            Assert.IsTrue(motor.Suspended, "set down out of every player's range, it sleeps");
        }

        private (AgentController, FakeMotor) NewAgent()
        {
            var go = new GameObject("agent");
            spawned.Add(go);
            FakeMotor motor = go.AddComponent<FakeMotor>();
            AgentController agent = go.AddComponent<AgentController>();
            typeof(AgentController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            return (agent, motor);
        }

        private class FakeMotor : MonoBehaviour, IMovementMotor, ISelfDrivingMotor
        {
            public bool Suspended { get; private set; }

            public Vector3 Velocity => Vector3.zero;
            public float TopSpeed => 0f;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime) { }
            public void ForceStop() { }

            public void SuspendSelfDrive() => Suspended = true;
            public void ResumeSelfDrive() => Suspended = false;
        }
    }
}
