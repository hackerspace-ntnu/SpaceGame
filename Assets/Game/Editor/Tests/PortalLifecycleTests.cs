// What a portal gun promises beyond the maths: two holes that are open at the same time, that
// something can actually walk through, and that are gone twenty seconds later.
//
// Each of these pins a defect the shipped feature had, and all three were invisible to the
// existing PortalTraversalTests because those exercise the transfer matrix, which was never the
// broken part.
//
//   • THE DOOR RAN AT ALL. Traversal used OnTriggerEnter, and the traveller volume is a
//     BoxCollider on a CHILD of the portal with no Rigidbody anywhere in the chain — so Unity
//     delivered those messages to a GameObject that has no Portal on it, and nothing ever went
//     through an aperture in any scene. It is a swept volume now, and the sweep is what these
//     tests drive.
//
//   • THE PAIR SURVIVES ONE HALF CLOSING. An aperture ends itself when its life runs out, so
//     PortalPair can no longer be the only thing that closes one. If it does not hear about it,
//     the slot keeps a destroyed reference and the next shot from that barrel takes the
//     "already have one, move it" branch on an object that is gone.
//
//   • THE LIFETIME IS THE GUN'S, NOT THE PREFAB'S. A pair placed in a scene by hand is scenery
//     and must not evaporate; only what the gun opens expires.
//
// Edit mode, so nothing here may lean on Awake, OnEnable or LateUpdate — AddComponent raises none
// of them outside play mode. Portal.All is filled by hand and Portal.AdvanceTraversal is called by
// hand, which is exactly why that method is public.
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools
{
    public class PortalLifecycleTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() => Portal.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Portal portal in new List<Portal>(Portal.All))
                if (portal != null) Object.DestroyImmediate(portal.gameObject);

            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
            Portal.All.Clear();
        }

        private static readonly Vector2 ApertureSize = new Vector2(3.45f, 6.15f);

        private Portal NewPortal(string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            spawned.Add(go);

            Portal portal = go.AddComponent<Portal>();
            portal.SetSize(ApertureSize);
            portal.Place(position, rotation, null, 0);
            Portal.All.Add(portal);

            return portal;
        }

        /// <summary>A prefab-shaped source for PortalPair.Open — never registered, never placed.</summary>
        private Portal NewPrefab()
        {
            var go = new GameObject("PortalPrefab");
            spawned.Add(go);
            return go.AddComponent<Portal>();
        }

        // ── The door ───────────────────────────────────────────────────────────

        /// <summary>
        /// Something with a body, standing in the opening, moved through the plane.
        ///
        /// Deliberately never raises a trigger callback — nothing simulates physics in edit mode —
        /// which is the whole point: the aperture has to find it by looking.
        /// </summary>
        [Test]
        public void SomethingWalkedThroughAnApertureComesOutTheOther()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewMover();

            // In front of the plane: an aperture faces OUT into the room along +forward, so the
            // room side is +Z here.
            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);
            entry.AdvanceTraversal();

            // And now behind it. One step, straight through the middle of the opening.
            Place(mover, new Vector3(0f, 0f, -0.6f));
            entry.AdvanceTraversal();

            Assert.Greater(exit.SideOf(mover.transform.position), 0f,
                           "walking into an aperture did not put anything out of the other one");
            Assert.Less(Vector3.Distance(mover.transform.position, exit.transform.position), 2f,
                        "the traveller came out nowhere near the exit");
        }

        [Test]
        public void SomethingPastTheRimWalksIntoTheWall()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewMover();

            // Beside the opening. The mover's own half-metre of box still reaches into the swept
            // volume, so it is tracked — the sweep is generous on purpose, because it decides who
            // may pass through the WALL, not who teleports. Its centre is 2.3 m out against a
            // 1.725 m half-width, so WithinAperture, which is what decides the crossing, says no.
            Place(mover, new Vector3(2.3f, 0f, 0.6f));
            RequirePhysicsQueries(mover);
            entry.AdvanceTraversal();

            Place(mover, new Vector3(2.3f, 0f, -0.6f));
            entry.AdvanceTraversal();

            Assert.Less(Vector3.Distance(mover.transform.position, new Vector3(2.3f, 0f, -0.6f)), 0.01f,
                        "something that crossed the wall beside the aperture was teleported");
        }

        [Test]
        public void AnUnpairedApertureCarriesNobody()
        {
            Portal lonely = NewPortal("lonely", Vector3.zero,
                                      Quaternion.LookRotation(Vector3.forward));

            GameObject mover = NewMover();

            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);
            lonely.AdvanceTraversal();

            Place(mover, new Vector3(0f, 0f, -0.6f));
            lonely.AdvanceTraversal();

            Assert.Less(Vector3.Distance(mover.transform.position, new Vector3(0f, 0f, -0.6f)), 0.01f,
                        "a portal with nowhere to go still swallowed somebody");
        }

        // ── The lifetime ───────────────────────────────────────────────────────

        [Test]
        public void AnApertureWithNoLifetimeStaysOpenForever()
        {
            Portal scenery = NewPortal("scenery", Vector3.zero,
                                       Quaternion.LookRotation(Vector3.forward));
            scenery.SetLifetime(0f);

            Assert.IsFalse(scenery.Expired, "a hand-placed aperture expired");
            Assert.AreEqual(float.PositiveInfinity, scenery.Remaining);
        }

        [Test]
        public void AnApertureExpiresOnceItsLifetimeIsUp()
        {
            Portal shot = NewPortal("shot", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            shot.SetLifetime(20f);

            Assert.IsFalse(shot.Expired, "a fresh aperture was already expired");
            Assert.AreEqual(20f, shot.Remaining, 0.5f, "the aperture did not take its lifetime");

            // Re-placed with a life it outlives inside the test. Place restarts the clock, so the
            // lifetime is set afterwards — which is the order PortalPair.Open uses.
            shot.Place(Vector3.zero, Quaternion.LookRotation(Vector3.forward), null, 0);
            shot.SetLifetime(0.01f);
            Thread.Sleep(60);

            Assert.IsTrue(shot.Expired, "an aperture outlived its lifetime");
            Assert.AreEqual(0f, shot.Remaining, 1e-4f);
        }

        [Test]
        public void RefiringABarrelGivesTheApertureItsFullLifeAgain()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal prefab = NewPrefab();

            Portal opened = pair.Open(PortalPair.Primary, prefab, Vector3.zero, Quaternion.identity,
                                      null, ApertureSize, Color.white, 20f);
            spawned.Add(opened.gameObject);

            Thread.Sleep(60);

            Portal again = pair.Open(PortalPair.Primary, prefab, Vector3.right, Quaternion.identity,
                                     null, ApertureSize, Color.white, 20f);

            Assert.AreSame(opened, again, "re-firing a barrel opened a second aperture");
            Assert.AreEqual(20f, again.Remaining, 0.02f,
                            "a moved aperture kept the old shot's remaining life");
        }

        // ── The pair ───────────────────────────────────────────────────────────

        [Test]
        public void AnApertureThatShutsFreesItsSlotAndUnlinksTheOther()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal prefab = NewPrefab();

            Portal primary = pair.Open(PortalPair.Primary, prefab, Vector3.zero, Quaternion.identity,
                                       null, ApertureSize, Color.white, 20f);
            Portal secondary = pair.Open(PortalPair.Secondary, prefab, new Vector3(0f, 0f, 40f),
                                         Quaternion.Euler(0f, 180f, 0f), null, ApertureSize,
                                         Color.white, 20f);

            spawned.Add(secondary.gameObject);

            Assert.AreSame(secondary, primary.Linked, "the two barrels did not pair up");

            // What an expiry does, without waiting twenty seconds for it.
            primary.Close();

            Assert.IsNull(pair.Get(PortalPair.Primary),
                          "the pair kept a destroyed aperture in its slot");
            Assert.IsNotNull(pair.Get(PortalPair.Secondary),
                             "closing one aperture took the other with it");
            Assert.IsNull(secondary.Linked,
                          "the survivor is still pointing at an aperture that no longer exists");

            // And the barrel works again afterwards, which is what a stale slot breaks.
            Portal reopened = pair.Open(PortalPair.Primary, prefab, Vector3.one, Quaternion.identity,
                                        null, ApertureSize, Color.white, 20f);
            spawned.Add(reopened.gameObject);

            Assert.IsNotNull(reopened, "the barrel could not open an aperture after one expired");
            Assert.AreSame(secondary, reopened.Linked, "the re-opened aperture did not re-pair");
        }

        [Test]
        public void BothBarrelsAreOpenAtOnce()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal prefab = NewPrefab();

            Portal primary = pair.Open(PortalPair.Primary, prefab, Vector3.zero, Quaternion.identity,
                                       null, ApertureSize, new Color(1f, 0.54f, 0.12f), 20f);
            Portal secondary = pair.Open(PortalPair.Secondary, prefab, new Vector3(0f, 0f, 40f),
                                         Quaternion.Euler(0f, 180f, 0f), null, ApertureSize,
                                         new Color(0.18f, 0.72f, 1f), 20f);

            spawned.Add(primary.gameObject);
            spawned.Add(secondary.gameObject);

            Assert.IsNotNull(pair.Get(PortalPair.Primary));
            Assert.IsNotNull(pair.Get(PortalPair.Secondary));
            Assert.AreNotSame(primary, secondary, "both barrels opened the same aperture");
            Assert.AreNotEqual(primary.Colour, secondary.Colour,
                               "the two ends are indistinguishable");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// A body the sweep will pick up: a collider with a Rigidbody, which is what
        /// PortalTraveller.For asks for before it will adopt anything.
        ///
        /// The traveller is added here rather than left to the sweep so its clone can be switched
        /// off. A clone is an Instantiate followed by Destroy on every script it copied, and
        /// Destroy outside play mode is an error rather than a deferred destroy.
        /// </summary>
        private GameObject NewMover()
        {
            var go = new GameObject("mover");
            spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(0.6f, 1.8f, 0.6f);
            go.AddComponent<Rigidbody>().isKinematic = true;

            PortalTraveller traveller = go.AddComponent<PortalTraveller>();
            var serialized = new SerializedObject(traveller);
            serialized.FindProperty("showClone").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }

        /// <summary>
        /// Move something and tell PhysX about it.
        ///
        /// Nothing simulates in edit mode, so a collider's pose in the physics scene is whatever
        /// it was when it was created until this is called — and the whole sweep is a physics
        /// query against exactly those poses.
        /// </summary>
        private static void Place(GameObject go, Vector3 position)
        {
            go.transform.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The premise of the three sweep tests: that a physics query in edit mode can see a
        /// collider at all.
        ///
        /// An assumption rather than an assertion, and the distinction is the whole reason it is
        /// here. If the editor's physics scene ever stops answering queries outside play mode, all
        /// three tests go red and read as "the portal is broken" — which is the exact confusion
        /// the swept volume exists to remove. Inconclusive says the harness could not run the
        /// experiment, which is a different sentence.
        /// </summary>
        private static void RequirePhysicsQueries(GameObject mover)
        {
            Collider[] found = Physics.OverlapBox(mover.transform.position, Vector3.one,
                                                  Quaternion.identity, ~0,
                                                  QueryTriggerInteraction.Ignore);

            Assume.That(found, Is.Not.Empty,
                        "edit-mode physics queries returned nothing at all — the sweep cannot be " +
                        "exercised here, so this says nothing about the portal.");
        }
    }
}
