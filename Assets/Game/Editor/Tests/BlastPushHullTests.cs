using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A shove that lands on a piece of a moving hull — the Sky City's deck mesh, a walker's
    /// platform — must leave the hull's kinematic body alone. The pellet gun hands BlastPush the
    /// collider's own GameObject when nothing up the tree has health, so the agent check that looks
    /// DOWN from it never saw the fleet's AgentController and the whole city went dynamic.
    /// </summary>
    public class BlastPushHullTests
    {
        private const float Step = 0.02f;
        private static readonly Vector3 Shove = new Vector3(10f, 2f, 0f);
        private static readonly Vector2 MassScaleRange = new Vector2(0.1f, 10f);

        private readonly List<GameObject> spawned = new List<GameObject>();
        private SimulationMode originalSimulationMode;

        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
            Physics.simulationMode = originalSimulationMode;
        }

        [Test]
        public void ShotAtDeckOfAgentHull_LeavesHullKinematicAndStill()
        {
            AssertHullUntouched(root => root.AddComponent<AgentController>());
        }

        [Test]
        public void ShotAtDeckOfWalkerPlatform_LeavesHullKinematicAndStill()
        {
            AssertHullUntouched(root => root.AddComponent<WalkerPlatformCarrier>());
        }

        [Test]
        public void ShotAtLooseDynamicProp_StillPushesIt()
        {
            Rigidbody prop = MakeProp(kinematic: false);

            Push(prop.GetComponent<Collider>());
            Physics.Simulate(Step);

            Assert.Greater(prop.linearVelocity.x, 1f, "a loose prop must still take the shove");
        }

        [Test]
        public void ShotAtLooseKinematicProp_StillWakesAndPushesIt()
        {
            Rigidbody prop = MakeProp(kinematic: true);

            Push(prop.GetComponent<Collider>());
            Physics.Simulate(Step);

            Assert.IsFalse(prop.isKinematic, "an ownerless kinematic prop is still woken by a shove");
            Assert.Greater(prop.linearVelocity.x, 1f);
        }

        private void AssertHullUntouched(System.Action<GameObject> addOwner)
        {
            var root = new GameObject("HullRoot");
            spawned.Add(root);
            Rigidbody hull = root.AddComponent<Rigidbody>();
            hull.isKinematic = true;
            hull.useGravity = false;
            addOwner(root);

            var deck = new GameObject("Mesh_Decks");
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, 2f, 0f);
            Collider deckCollider = deck.AddComponent<BoxCollider>();

            Vector3 before = root.transform.position;
            Push(deckCollider);
            Physics.Simulate(Step);

            Assert.IsTrue(hull.isKinematic, "a shot at the deck turned the hull's body dynamic");
            Assert.That(Vector3.Distance(before, root.transform.position), Is.LessThan(1e-4f),
                        "the hull moved under a shove");
        }

        private Rigidbody MakeProp(bool kinematic)
        {
            var prop = new GameObject("LooseProp");
            spawned.Add(prop);
            prop.transform.position = new Vector3(500f, 500f, 500f);
            prop.AddComponent<BoxCollider>();
            Rigidbody body = prop.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = kinematic;
            return body;
        }

        private static void Push(Collider caught)
        {
            BlastPush.Apply(caught, caught.gameObject, Shove, Shove.magnitude,
                            BlastPush.Leap.Proportional(1f, 1f, 0.5f), 1f, MassScaleRange);
        }
    }
}
