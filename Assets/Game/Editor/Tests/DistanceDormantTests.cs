// What a DistanceDormant reads off its own body for the sleep decision.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class DistanceDormantTests
    {
        private readonly List<GameObject> spawned = new();
        private static readonly HashSet<Transform> NoPlayers = new();

        [SetUp]
        public void SetUp() => ClearRegistry();

        [TearDown]
        public void TearDown()
        {
            ClearRegistry();
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void AFreshSubjectReadsAsUnhurtAndExemptFromNothing()
        {
            DistanceDormant subject = NewSubject();
            DormancyInputs inputs = subject.Read(false, NoPlayers, 100f);

            Assert.IsFalse(inputs.InGroup || inputs.SeatedAloft || inputs.Flying || inputs.HuntsPlayer);
            Assert.IsTrue(float.IsPositiveInfinity(inputs.SecondsSinceHurt));
        }

        [Test]
        public void HuntingASeatedPlayerKeepsItAwake()
        {
            DistanceDormant subject = NewSubject();
            var player = NewObject("player").transform;
            var seat = NewObject("seat").transform;
            seat.SetParent(NewObject("mount").transform);
            var body = NewObject("player body").transform;
            body.SetParent(seat);

            AgentTargeting.GetOrAdd(subject.gameObject).ForceTarget(body);
            var players = new HashSet<Transform> { body, player };

            Assert.IsTrue(subject.Read(false, players, 0f).HuntsPlayer);
        }

        [Test]
        public void HuntingAnotherNpcDoesNotKeepItAwake()
        {
            DistanceDormant subject = NewSubject();
            AgentTargeting.GetOrAdd(subject.gameObject).ForceTarget(NewObject("clanker").transform);

            Assert.IsFalse(subject.Read(false, NoPlayers, 0f).HuntsPlayer);
        }

        [Test]
        public void SeatedUnderAnAirborneCarrierIsAloft()
        {
            DistanceDormant subject = NewSubject();
            GameObject craft = NewObject("craft");
            craft.AddComponent<FakeCarrier>();
            subject.transform.SetParent(craft.transform);

            Assert.IsTrue(subject.Read(false, NoPlayers, 0f).SeatedAloft);
        }

        [Test]
        public void DisablingTheMarkerReleasesTheAgent()
        {
            DistanceDormant subject = NewSubject();
            subject.Agent.Dormant = true;

            // AddComponent does not run lifecycle callbacks outside play mode, so neither does enabled = false.
            subject.enabled = false;
            typeof(DistanceDormant).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(subject, null);

            Assert.IsFalse(subject.Agent.Dormant);
            CollectionAssert.DoesNotContain((ICollection<DistanceDormant>)DistanceDormant.All, subject);
        }

        [Test]
        public void TearingDownTheBodyLeavesTheAgentParked()
        {
            DistanceDormant subject = NewSubject();
            subject.Agent.Dormant = true;

            // A destroyed or deactivated body runs OnDisable with the marker itself still enabled.
            typeof(DistanceDormant).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(subject, null);

            Assert.IsTrue(subject.Agent.Dormant);
            CollectionAssert.DoesNotContain((ICollection<DistanceDormant>)DistanceDormant.All, subject);
        }

        // DestroyImmediate does not run OnDisable outside play mode, so subjects would linger in the
        // static registry as destroyed objects.
        private static void ClearRegistry() =>
            typeof(DistanceDormant).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        private DistanceDormant NewSubject()
        {
            GameObject go = NewObject("subject");
            go.AddComponent<FakeMotor>();
            AgentController agent = go.AddComponent<AgentController>();
            typeof(AgentController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            DistanceDormant subject = go.AddComponent<DistanceDormant>();
            typeof(DistanceDormant).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(subject, null);
            return subject;
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private class FakeCarrier : MonoBehaviour, IAirborneCarrier { }

        private class FakeMotor : MonoBehaviour, IMovementMotor
        {
            public Vector3 Velocity => Vector3.zero;
            public float TopSpeed => 0f;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime) { }
            public void ForceStop() { }
        }
    }
}
