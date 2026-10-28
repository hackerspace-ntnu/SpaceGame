// Nothing a rider uses may land on the mount under them.
//
// The bug these pin, reported from the esko: a lasso thrown from the saddle caught the esko being
// ridden. Every item filtered its hits with IsChildOf(owner), and a rider is a CHILD of the mount,
// not the other way round, so the mount's own colliders passed every one of those filters. The rule
// lives in AimProvider.IsOwnOrRidden and is read off the parenting, which is the one part of a ride
// every machine is told about.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class RiddenMountFilterTests
    {
        private GameObject mount;
        private GameObject rider;
        private GameObject held;
        private GameObject body;
        private GameObject target;

        [SetUp]
        public void SetUp()
        {
            // An esko-shaped mount: MountModule on the root, its body a collider below it, and the
            // rider hung off the root the way ParentRiderToMount leaves them.
            mount = new GameObject("mount", typeof(MountModule));

            body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "COL_Body";
            body.transform.SetParent(mount.transform, false);
            // Tall enough to stand across the rider's eye line, as an esko's neck and head do.
            body.transform.localPosition = new Vector3(0f, 1f, 2f);
            body.transform.localScale = new Vector3(2f, 3f, 2f);

            rider = new GameObject("rider", typeof(AimProvider));
            rider.transform.SetParent(mount.transform, false);
            rider.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            held = new GameObject("lasso");
            held.transform.SetParent(rider.transform, false);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { target, mount, rider })
                if (go != null) Object.DestroyImmediate(go);
            target = mount = rider = held = body = null;
        }

        private void Dismount()
        {
            rider.transform.SetParent(null, worldPositionStays: true);
            Physics.SyncTransforms();
        }

        [Test]
        public void TheMountUnderTheRiderIsTheirs()
        {
            Assert.IsTrue(AimProvider.IsOwnOrRidden(body.transform, rider.transform),
                "The esko's own collider passed the filter — a lasso thrown from the saddle catches " +
                "the animal being ridden, which is the reported defect verbatim.");
        }

        [Test]
        public void AskingWithTheHeldItemAnswersForTheRider()
        {
            Assert.AreSame(mount.transform, AimProvider.RiddenBy(held.transform),
                "Items pass whatever transform they hold — the item, its muzzle, the owner. Each " +
                "must resolve to the same mount, or the filter depends on which one a caller had.");
        }

        [Test]
        public void TheRidersOwnBodyIsStillTheirs()
        {
            Assert.IsTrue(AimProvider.IsOwnOrRidden(held.transform, rider.transform));
        }

        [Test]
        public void OnFootTheMountIsJustAnotherAnimal()
        {
            Dismount();

            Assert.IsNull(AimProvider.RiddenBy(rider.transform));
            Assert.IsFalse(AimProvider.IsOwnOrRidden(body.transform, rider.transform),
                "Standing beside a mount must not make it un-lassoable; only riding it does.");
        }

        [Test]
        public void TheAimLooksPastTheMountWithNoViewHandedOver()
        {
            // Only the local rider is ever given SetExternalView. Every other machine — the server
            // deciding a hit, a peer drawing it — has nothing but the parenting to go on.
            var eye = new GameObject("eye", typeof(Camera));
            eye.transform.SetParent(rider.transform, false);
            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(rider.GetComponent<AimProvider>(), eye.GetComponent<Camera>());

            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "target";
            target.transform.position = rider.transform.position + new Vector3(0f, 0f, 20f);
            Physics.SyncTransforms();

            Assert.IsTrue(rider.GetComponent<AimProvider>().TryGetAimHit(60f, out RaycastHit hit));
            Assert.AreSame(target, hit.collider.gameObject,
                "The mount's own body is the first thing a rider's eye ray crosses looking ahead; " +
                "it is not a target on any machine.");
        }
    }
}
