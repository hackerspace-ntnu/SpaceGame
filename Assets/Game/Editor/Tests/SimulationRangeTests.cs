// SimulationRange applying the rule to real components, without play mode.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SimulationRangeTests
    {
        private readonly List<GameObject> spawned = new();
        private SimulationRange range;

        [SetUp]
        public void SetUp()
        {
            ClearRegistry();
            GameObject host = NewObject("world sim");
            host.AddComponent<NpcWorldSim>();               // spawnRadius 250, despawnRadius 350 by default
            range = host.AddComponent<SimulationRange>();
            range.InteriorReturnPosition = _ => null;
        }

        [TearDown]
        public void TearDown()
        {
            ClearRegistry();
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void AnNpcFarFromEveryPlayerSleeps_AndANearOneDoesNot()
        {
            DistanceDormant far = NewSubject(new Vector3(800f, 0f, 0f));
            DistanceDormant near = NewSubject(new Vector3(100f, 0f, 0f));

            range.Tick(new[] { NewObject("player").transform }, 0f);

            Assert.IsTrue(far.Agent.Dormant);
            Assert.IsFalse(near.Agent.Dormant);
        }

        [Test]
        public void WalkingUpWakesIt()
        {
            DistanceDormant npc = NewSubject(new Vector3(800f, 0f, 0f));
            Transform player = NewObject("player").transform;

            range.Tick(new[] { player }, 0f);
            Assert.IsTrue(npc.Agent.Dormant);

            player.position = new Vector3(600f, 0f, 0f);
            range.Tick(new[] { player }, 0.5f);
            Assert.IsFalse(npc.Agent.Dormant);
        }

        [Test]
        public void APlayerInsideAnInteriorWakesTheDoorTheyCameIn()
        {
            DistanceDormant resident = NewSubject(new Vector3(800f, 0f, 0f));
            Transform player = NewObject("player").transform;            // standing at the interior's world-origin coordinates
            range.InteriorReturnPosition = _ => new Vector3(790f, 0f, 0f); // the door they walked in by

            range.Tick(new[] { player }, 0f);

            Assert.IsFalse(resident.Agent.Dormant);
        }

        [Test]
        public void APlayerInsideAnInteriorAlsoWakesWhereTheyStand()
        {
            // A mount ridden into the interior stands beside its rider, not at the door.
            DistanceDormant mount = NewSubject(new Vector3(5f, 0f, 0f));
            Transform player = NewObject("player").transform;
            range.InteriorReturnPosition = _ => new Vector3(790f, 0f, 0f);

            range.Tick(new[] { player }, 0f);

            Assert.IsFalse(mount.Agent.Dormant);
        }

        [Test]
        public void ADisabledControllerKeepsItsDormancy()
        {
            // Dead or ragdolled: waking it would resume the NavMeshAgent under the ragdoll.
            DistanceDormant npc = NewSubject(new Vector3(800f, 0f, 0f));
            Transform player = NewObject("player").transform;
            range.Tick(new[] { player }, 0f);
            Assert.IsTrue(npc.Agent.Dormant);

            npc.Agent.enabled = false;
            player.position = new Vector3(790f, 0f, 0f);
            range.Tick(new[] { player }, 0.5f);

            Assert.IsTrue(npc.Agent.Dormant);
            Assert.IsTrue(npc.Agent.IsParked);
        }

        [Test]
        public void APassengerKeepsWhateverItWasWhenSeated()
        {
            DistanceDormant asleep = NewSubject(new Vector3(800f, 0f, 0f));
            DistanceDormant awake = NewSubject(new Vector3(100f, 0f, 0f));
            Transform player = NewObject("player").transform;
            range.Tick(new[] { player }, 0f);
            asleep.Agent.RidesAsPassenger = true;
            awake.Agent.RidesAsPassenger = true;

            player.position = new Vector3(2000f, 0f, 0f);  // far from where the awake one sits
            asleep.transform.position = new Vector3(1990f, 0f, 0f); // the hull carried the sleeper to the player
            range.Tick(new[] { player }, 0.5f);

            Assert.IsTrue(asleep.Agent.Dormant);
            Assert.IsFalse(awake.Agent.Dormant);
        }

        [Test]
        public void NoPlayersPutsEverySubjectToSleep()
        {
            DistanceDormant npc = NewSubject(Vector3.zero);
            range.Tick(new Transform[0], 0f);
            Assert.IsTrue(npc.Agent.Dormant);
        }

        // DestroyImmediate does not run OnDisable outside play mode, so subjects (ours, or a sibling
        // fixture's) would linger in the static registry as destroyed objects.
        private static void ClearRegistry() =>
            typeof(DistanceDormant).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        private DistanceDormant NewSubject(Vector3 position)
        {
            GameObject go = NewObject("npc");
            go.transform.position = position;
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

        private class FakeMotor : MonoBehaviour, IMovementMotor, ISelfDrivingMotor
        {
            public Vector3 Velocity => Vector3.zero;
            public float TopSpeed => 0f;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime) { }
            public void ForceStop() { }
            public void SuspendSelfDrive() { }
            public void ResumeSelfDrive() { }
        }
    }
}
