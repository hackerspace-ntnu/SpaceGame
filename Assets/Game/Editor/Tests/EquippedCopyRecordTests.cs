// An item in a hand or on a bone is not a world entity, and must not be saved as one.
//
// It used to be. Every item prefab ships a SaveableEntity so a copy lying in the sand survives a
// reload, and the equipped copy inherited it — while WorldSaveStore.CaptureScene finds saveables
// with GetComponentsInChildren from each scene root, so it reached inside the wearer and wrote the
// gauntlet on their forearm into the save at its world pose. The next hydrate built that record
// back as a loose root object with its Rigidbody live: a second gauntlet at the player, falling.
// One more per load. A single save file held six generations of Jetpack, GrapplingHook and
// RepulsorGauntlet stacked from y=110 down to y=-8153.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class EquippedCopyRecordTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        private GameObject Item(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [Test]
        public void SanitizeTakesTheWorldRecordOffAnEquippedCopy()
        {
            GameObject item = Item("Gauntlet");
            item.AddComponent<SaveableEntity>();

            EquipItemSocket.Sanitize(item);

            Assert.IsNull(item.GetComponent<SaveableEntity>(),
                          "An equipped copy kept its SaveableEntity, so the world save will capture " +
                          "it inside the wearer and re-spawn it as a loose item on the next load.");
        }

        [Test]
        public void SanitizeReachesANestedRecord()
        {
            GameObject item = Item("HullModule");

            var fixture = new GameObject("Fixture");
            fixture.transform.SetParent(item.transform, false);
            fixture.AddComponent<SaveableEntity>();

            EquipItemSocket.Sanitize(item);

            Assert.IsEmpty(item.GetComponentsInChildren<SaveableEntity>(true),
                           "CaptureScene walks children, so a nested SaveableEntity is captured too.");
        }

        // The savers are inert without an entity to collect them, and a prefab that is DROPPED is
        // built from the asset again — so they stay. Asserting it keeps the strip honest: this is a
        // record being removed, not a general strip of the persistence namespace.
        [Test]
        public void SanitizeLeavesTheSaversAlone()
        {
            GameObject item = Item("Jetpack");
            item.AddComponent<SaveableEntity>();
            item.AddComponent<TransformSaveable>();

            EquipItemSocket.Sanitize(item);

            Assert.IsNotNull(item.GetComponent<TransformSaveable>());
        }

        [Test]
        public void SanitizeStillNeutersPhysics()
        {
            GameObject item = Item("Wingsuit");
            item.AddComponent<SaveableEntity>();
            Rigidbody body = item.AddComponent<Rigidbody>();
            BoxCollider solid = item.AddComponent<BoxCollider>();

            EquipItemSocket.Sanitize(item);

            Assert.IsTrue(body.isKinematic);
            Assert.IsFalse(body.useGravity);
            Assert.IsFalse(body.detectCollisions);
            Assert.IsFalse(solid.enabled);
        }
    }
}
