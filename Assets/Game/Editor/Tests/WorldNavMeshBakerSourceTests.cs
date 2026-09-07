// What the world bake takes as ground and what it leaves out.
//
// The one that matters is the agent case: a NavMesh creature is a kinematic body with a solid
// collider, which the "non-kinematic bodies are scenery that moves" rule reads as ground. It baked
// six patrol robots into the first Clanker settlement as holes in the mesh under their own feet.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public class WorldNavMeshBakerSourceTests
    {
        private static readonly LayerMask Everything = ~0;
        private GameObject root;

        [SetUp]
        public void SetUp() => root = new GameObject("BakeSubject");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void AStaticColliderIsGround()
        {
            var col = root.AddComponent<BoxCollider>();
            Assert.IsTrue(WorldNavMeshBaker.IsBakeable(col, Everything));
        }

        [Test]
        public void KinematicSceneryIsGround()
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            var col = root.AddComponent<BoxCollider>();
            Assert.IsTrue(WorldNavMeshBaker.IsBakeable(col, Everything));
        }

        [Test]
        public void AFallingBodyIsNotGround()
        {
            root.AddComponent<Rigidbody>().isKinematic = false;
            var col = root.AddComponent<BoxCollider>();
            Assert.IsFalse(WorldNavMeshBaker.IsBakeable(col, Everything));
        }

        [Test]
        public void ATriggerIsNotGround()
        {
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            Assert.IsFalse(WorldNavMeshBaker.IsBakeable(col, Everything));
        }

        [Test]
        public void AnExcludedLayerIsNotGround()
        {
            var col = root.AddComponent<BoxCollider>();
            root.layer = 5;
            LayerMask withoutFive = ~(1 << 5);
            Assert.IsFalse(WorldNavMeshBaker.IsBakeable(col, withoutFive));
        }

        [Test]
        public void AWalkerIsNeverGround_EvenThoughItIsKinematic()
        {
            // The shape every NavMesh creature in the project has: agent + kinematic body + collider.
            root.AddComponent<NavMeshAgent>();
            root.AddComponent<Rigidbody>().isKinematic = true;
            var col = root.AddComponent<CapsuleCollider>();
            Assert.IsFalse(WorldNavMeshBaker.IsBakeable(col, Everything));
        }

        [Test]
        public void AWalkersLimbColliderIsNotGroundEither()
        {
            root.AddComponent<NavMeshAgent>();
            var limb = new GameObject("Limb");
            limb.transform.SetParent(root.transform);
            var col = limb.AddComponent<BoxCollider>();
            Assert.IsFalse(WorldNavMeshBaker.IsBakeable(col, Everything));
        }
    }
}
