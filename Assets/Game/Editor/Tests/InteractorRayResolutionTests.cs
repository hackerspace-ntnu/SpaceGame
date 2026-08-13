// What the look-ray decides you are pointing at.
//
// The bug this pins: a vehicle's carry volume is a TRIGGER drawn around the whole deck, and the old
// resolution took the first collider the ray touched and then walked UP the hierarchy for an
// IInteractable. Standing on a deck, that volume was always the first thing hit, and the walk up
// reached the hull root -- so the carry volume answered on behalf of the hull for every control
// standing inside it. On the dune foiler that meant not one rope station could be worked; on the
// crawler the gantry volume answered with the hull's MountModule, which is why mounting was the only
// interaction in the game that ever seemed to respond.
//
// The rule now: a trigger is a detection volume, not a surface. It answers only if it carries the
// interactable itself, and is otherwise transparent to the ray. Solid colliders behave as before,
// including blocking the line of sight when they are inert.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class InteractorRayResolutionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        /// A hull whose root is interactable, with a deck-wide trigger volume hung under it and a
        /// control standing inside that volume -- the dune foiler, in miniature.
        private void BuildCraft(out Collider carryVolume, out Collider controlHandle, out Collider deck)
        {
            GameObject root = new GameObject("Hull");
            spawned.Add(root);
            root.AddComponent<StubInteractable>();       // stands in for DeckBoarding / MountModule

            GameObject volume = new GameObject("COL_CarryVolume");
            volume.transform.SetParent(root.transform, false);
            BoxCollider volumeBox = volume.AddComponent<BoxCollider>();
            volumeBox.isTrigger = true;
            carryVolume = volumeBox;

            GameObject deckObject = new GameObject("COL_Deck");
            deckObject.transform.SetParent(root.transform, false);
            deck = deckObject.AddComponent<BoxCollider>();

            GameObject station = new GameObject("Station");
            station.transform.SetParent(root.transform, false);
            station.AddComponent<StubInteractable>();    // stands in for a rigging station
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(station.transform, false);
            controlHandle = handle.AddComponent<SphereCollider>();
        }

        [Test]
        public void ATriggerVolumeDoesNotAnswerForTheHullItHangsUnder()
        {
            BuildCraft(out Collider carry, out Collider handle, out Collider deck);

            // The volume encloses the deck; the control stands inside it, further along the ray.
            // Placed well ahead of the ray's origin: a ray that STARTS inside a collider is not
            // reported as hitting it, which would quietly make this test prove nothing.
            carry.transform.position = new Vector3(0f, 0f, 15f);
            ((BoxCollider)carry).size = new Vector3(8f, 4f, 12f);
            handle.transform.position = new Vector3(0f, 0f, 16f);
            deck.transform.position = new Vector3(0f, -2f, 15f);
            ((BoxCollider)deck).size = new Vector3(8f, 0.2f, 12f);
            Physics.SyncTransforms();

            RaycastHit[] hits = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits, 30f);
            Assert.Greater(count, 1, "the ray should cross the trigger volume and then the handle");

            bool found = Interactor.ResolveAlongRay(hits, count, out IInteractable resolved, out _);

            Assert.IsTrue(found, "nothing resolved at all");
            Assert.AreEqual("Station", ((MonoBehaviour)resolved).name,
                "the carry volume answered for the hull instead of letting the ray reach the control");
        }

        [Test]
        public void ATriggerThatIsItselfAControlStillAnswers()
        {
            // The crawler's DOOR_MountStation: a trigger collider with the interactable on the very
            // same GameObject. This must keep working.
            GameObject door = new GameObject("DOOR_MountStation");
            spawned.Add(door);
            BoxCollider box = door.AddComponent<BoxCollider>();
            box.isTrigger = true;
            door.AddComponent<StubInteractable>();
            door.transform.position = new Vector3(0f, 0f, 4f);
            Physics.SyncTransforms();

            RaycastHit[] hits = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits, 20f);

            bool found = Interactor.ResolveAlongRay(hits, count, out IInteractable resolved, out _);

            Assert.IsTrue(found, "a trigger carrying its own control stopped answering");
            Assert.AreEqual("DOOR_MountStation", ((MonoBehaviour)resolved).name);
        }

        [Test]
        public void ASolidInertColliderStillBlocksTheLineOfSight()
        {
            GameObject wall = new GameObject("Wall");
            spawned.Add(wall);
            wall.AddComponent<BoxCollider>();
            wall.transform.position = new Vector3(0f, 0f, 3f);

            GameObject control = new GameObject("Control");
            spawned.Add(control);
            control.AddComponent<BoxCollider>();
            control.AddComponent<StubInteractable>();
            control.transform.position = new Vector3(0f, 0f, 6f);
            Physics.SyncTransforms();

            RaycastHit[] hits = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits, 20f);

            bool found = Interactor.ResolveAlongRay(hits, count, out IInteractable resolved, out _);

            Assert.IsFalse(found, "you should not be able to interact through a solid wall");
            Assert.IsNull(resolved);
        }

        [Test]
        public void ASolidColliderStillInheritsItsParentsInteractable()
        {
            // Looking at the hull plating must still offer the hull's own interaction.
            GameObject root = new GameObject("Hull");
            spawned.Add(root);
            root.AddComponent<StubInteractable>();

            GameObject plate = new GameObject("COL_Hull");
            plate.transform.SetParent(root.transform, false);
            plate.AddComponent<BoxCollider>();
            plate.transform.position = new Vector3(0f, 0f, 4f);
            Physics.SyncTransforms();

            RaycastHit[] hits = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits, 20f);

            bool found = Interactor.ResolveAlongRay(hits, count, out IInteractable resolved, out _);

            Assert.IsTrue(found, "a solid child stopped inheriting the hull's interaction");
            Assert.AreEqual("Hull", ((MonoBehaviour)resolved).name);
        }

        /// The player's own capsule is on the Default layer, and the camera sits just inside the top
        /// of it. Lean or look down and the eye pokes out through your own body, which then blocks
        /// every interaction as though a wall were in the way -- intermittently, which is worse.
        [Test]
        public void YourOwnBodyDoesNotBlockYourInteractions()
        {
            GameObject body = new GameObject("PlayerBody");
            spawned.Add(body);
            GameObject capsule = new GameObject("Collider");
            capsule.transform.SetParent(body.transform, false);
            capsule.AddComponent<BoxCollider>();
            capsule.transform.position = new Vector3(0f, 0f, 2f);   // between eye and control

            GameObject control = new GameObject("Control");
            spawned.Add(control);
            control.AddComponent<BoxCollider>();
            control.AddComponent<StubInteractable>();
            control.transform.position = new Vector3(0f, 0f, 8f);
            Physics.SyncTransforms();

            RaycastHit[] hits = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits, 20f);

            Assert.IsFalse(Interactor.ResolveAlongRay(hits, count, out _, out _),
                "precondition: without the body being ignored, it blocks");

            bool found = Interactor.ResolveAlongRay(hits, count, out IInteractable resolved, out _,
                                                    body.transform);

            Assert.IsTrue(found, "the player's own collider blocked their interaction");
            Assert.AreEqual("Control", ((MonoBehaviour)resolved).name);
        }

        private class StubInteractable : MonoBehaviour, IInteractable
        {
            public bool CanInteract() => true;
            public void Interact(Interactor interactor) { }
        }
    }
}
