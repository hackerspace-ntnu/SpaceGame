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
using SpaceGame.Characters;
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

            // These measure where the traveller ENDED UP, so both apertures have to outlive the
            // journey. Single-use is the shipped default and has its own test below.
            entry.SetCloseOnTraversal(false);
            exit.SetCloseOnTraversal(false);

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

        /// <summary>
        /// A creature far too big for the opening is refused — and, more importantly, is refused
        /// BEFORE it is handed the pass through the wall.
        ///
        /// Being tracked by an aperture is not merely being eligible to teleport: it stops the wall
        /// the aperture is cut into from colliding with the traveller at all. So the untested
        /// version gave a ten-metre creature two things at once, and the second was the worse one —
        /// it teleported whole the moment the CENTRE of its colliders crossed the plane inside the
        /// ellipse, and until then the masonry the picture is painted on simply stopped existing
        /// for it.
        /// </summary>
        [Test]
        public void SomethingTooBigForTheApertureIsRefusedAndKeepsTheWall()
        {
            var wallObject = new GameObject("wall");
            spawned.Add(wallObject);
            wallObject.transform.position = new Vector3(0f, 0f, -0.5f);
            BoxCollider wall = wallObject.AddComponent<BoxCollider>();
            wall.size = new Vector3(40f, 40f, 1f);

            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            entry.Place(Vector3.zero, Quaternion.LookRotation(Vector3.forward), wall, 0);
            Portal.Link(entry, exit);

            // Ten metres across every axis, against a 3.45 x 6.15 m opening. Nothing about how it
            // is turned can make that fit.
            var giant = new GameObject("giant");
            spawned.Add(giant);
            BoxCollider hide = giant.AddComponent<BoxCollider>();
            hide.size = new Vector3(10f, 10f, 10f);
            giant.AddComponent<Rigidbody>().isKinematic = true;
            giant.AddComponent<PortalTraveller>();

            Place(giant, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(giant);
            entry.AdvanceTraversal();

            Assert.IsFalse(Physics.GetIgnoreCollision(hide, wall),
                "the aperture switched the wall off for something that cannot fit through it, so " +
                "the creature walks into the picture instead of being stopped by the masonry");

            Place(giant, new Vector3(0f, 0f, -0.6f));
            entry.AdvanceTraversal();

            Assert.Less(Vector3.Distance(giant.transform.position, new Vector3(0f, 0f, -0.6f)), 0.01f,
                        "a creature three times the width of the opening was teleported through it");
        }

        /// <summary>
        /// Something that walks up to an aperture and STOPS is taken through it.
        ///
        /// This is the case the plane-crossing test cannot serve, and it is most of the game. A
        /// NavMeshAgent will not path into a wall, because navigation has no idea the hole is
        /// there; a legged machine walks to the rim and halts. Neither ever drives its centre past
        /// the plane, so under the crossing test alone the dune rat and the Nomad ignored apertures
        /// completely and the astronaut reached the opening and stood in it.
        ///
        /// Note what this test does NOT do: it never moves the mover across the plane. It places it
        /// once, touching, and steps the portal.
        /// </summary>
        [Test]
        public void SomethingTouchingTheApertureIsPulledThrough()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            // These measure where the traveller ENDED UP, so both apertures have to outlive the
            // journey. Single-use is the shipped default and has its own test below.
            entry.SetCloseOnTraversal(false);
            exit.SetCloseOnTraversal(false);

            GameObject mover = NewMover();

            // Half a metre in front of the plane. The mover is 0.6 m deep, so its own surface is
            // 0.2 m from the opening — touching, by any reading that is not about its centre.
            Place(mover, new Vector3(0f, 0f, 0.5f));
            RequirePhysicsQueries(mover);

            // Twice, and it has to be twice. The first step only tracks the traveller — the pull
            // deliberately waits until a frame of motion has been measured, so that something still
            // walking toward the plane is left to cross it on its own and straddle the aperture.
            // The mover does not move between the two, which is the whole point: it has stopped.
            entry.AdvanceTraversal();
            entry.AdvanceTraversal();

            Assert.Greater(exit.SideOf(mover.transform.position), 0f,
                "something standing against the aperture was not taken through it — which is every " +
                "creature in the game that decides where to walk instead of being pushed there");
        }

        /// <summary>
        /// And is not immediately pulled back in by the aperture it came out of.
        ///
        /// An arrival stands in front of the exit, touching it, on the very side the pull acts
        /// from. Without the arrival flag the contact rule sends it straight back the moment the
        /// re-entry cooldown lapses, and then again, forever — so the guard is not a nicety, it is
        /// what makes the rule usable at all.
        /// </summary>
        [Test]
        public void SomethingPulledThroughIsNotPulledStraightBack()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            // These measure where the traveller ENDED UP, so both apertures have to outlive the
            // journey. Single-use is the shipped default and has its own test below.
            entry.SetCloseOnTraversal(false);
            exit.SetCloseOnTraversal(false);

            GameObject mover = NewMover();
            Place(mover, new Vector3(0f, 0f, 0.5f));
            RequirePhysicsQueries(mover);

            entry.AdvanceTraversal();
            entry.AdvanceTraversal();
            Vector3 arrived = mover.transform.position;

            // Past the re-entry cooldown, so the only thing that can still be holding it is the
            // arrival flag. Physics has to be told where the mover ended up, or the exit's own
            // sweep cannot see it.
            Thread.Sleep(250);
            Physics.SyncTransforms();

            exit.AdvanceTraversal();
            exit.AdvanceTraversal();

            Assert.Less(Vector3.Distance(mover.transform.position, arrived), 1f,
                "the exit pulled the traveller back through the moment it arrived, which is an " +
                "endless round trip rather than a portal");
        }

        /// <summary>
        /// A pair is good for one journey: both apertures shut the moment anything comes through.
        ///
        /// Both, and that is the part worth pinning. An aperture whose partner has gone still draws
        /// a lit ring around its swirl and is indistinguishable from a working portal until
        /// somebody walks into the wall behind it — so leaving the far end standing would turn
        /// every trip into scenery the player has to clear up.
        /// </summary>
        [Test]
        public void GoingThroughAPairShutsBothEndsOfIt()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewMover();

            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);
            entry.AdvanceTraversal();

            Place(mover, new Vector3(0f, 0f, -0.6f));
            entry.AdvanceTraversal();

            // Unity's fake-null: the components are destroyed, so both compare equal to null.
            Assert.IsTrue(entry == null, "the aperture that was walked into stayed open");
            Assert.IsTrue(exit == null,
                "the far aperture stayed open after its partner shut, which is a dead end wearing " +
                "the face of a working portal");
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

        // ── The barrel cursor ──────────────────────────────────────────────────
        //
        // The gun's own trigger is not reachable from an edit-mode test — it needs an owner, an
        // aim provider, a camera and an input manager. What IS reachable, and what the trigger
        // does nothing but ask, is the cursor: given a pair in some state, which barrel does the
        // next shot come out of. Every one of these is a shape the shipped gun got wrong by only
        // ever asking for Primary.

        /// <summary>Two clicks, two holes. The hard requirement, in one test.</summary>
        [Test]
        public void ConsecutiveShotsWalkTheTwoBarrels()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal prefab = NewPrefab();

            int first = pair.PeekBarrel();
            pair.CommitBarrel(first);
            spawned.Add(pair.Open(first, prefab, Vector3.zero, Quaternion.identity,
                                  null, ApertureSize, Color.white, 20f).gameObject);

            int second = pair.PeekBarrel();
            pair.CommitBarrel(second);
            spawned.Add(pair.Open(second, prefab, new Vector3(0f, 0f, 40f), Quaternion.identity,
                                  null, ApertureSize, Color.white, 20f).gameObject);

            Assert.AreNotEqual(first, second, "both clicks came out of the same barrel");
            Assert.AreEqual(2, pair.OpenCount, "two shots left fewer than two apertures open");
        }

        /// <summary>
        /// The cursor is claimed when the shot LEAVES, not when the aperture opens.
        ///
        /// The blob takes a visible fraction of a second to reach the wall, so a cursor that waited
        /// for the arrival would let two quick clicks both read "nothing open yet", both pick the
        /// same barrel, and the second move the aperture the first had just placed.
        /// </summary>
        [Test]
        public void TwoShotsInFlightAtOnceStillTakeDifferentBarrels()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);

            int first = pair.PeekBarrel();
            pair.CommitBarrel(first);

            // Nothing has opened yet — both blobs are still in the air.
            int second = pair.PeekBarrel();

            Assert.AreNotEqual(first, second,
                               "a second shot fired before the first landed reused its barrel");
        }

        /// <summary>
        /// An expired barrel is the one to refill, whatever the alternation says.
        ///
        /// Without this the cursor is as likely as not to be pointing at the aperture that is still
        /// open when the other one times out, and the shot meant to restore the pair moves the
        /// survivor instead — one portal on screen, and nothing to explain why.
        /// </summary>
        [Test]
        public void AShotAfterOneApertureExpiresRefillsTheEmptyBarrel()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal prefab = NewPrefab();

            Portal primary = pair.Open(PortalPair.Primary, prefab, Vector3.zero, Quaternion.identity,
                                       null, ApertureSize, Color.white, 20f);
            Portal secondary = pair.Open(PortalPair.Secondary, prefab, new Vector3(0f, 0f, 40f),
                                         Quaternion.identity, null, ApertureSize, Color.white, 20f);
            spawned.Add(secondary.gameObject);

            // The cursor is left pointing at the barrel that is about to survive, which is the
            // arrangement plain alternation gets wrong.
            pair.CommitBarrel(PortalPair.Primary);
            primary.Close();

            Assert.AreEqual(PortalPair.Primary, pair.PeekBarrel(),
                            "the next shot would have moved the surviving aperture");

            spawned.Add(pair.Open(pair.PeekBarrel(), prefab, Vector3.one, Quaternion.identity,
                                  null, ApertureSize, Color.white, 20f).gameObject);

            Assert.AreEqual(2, pair.OpenCount, "the pair was not restored to two apertures");
        }

        // ── The clone ───────────────────────────────────────────────────

        /// <summary>
        /// The picture standing out of the far aperture is made of renderers and nothing else.
        ///
        /// It used to be an Instantiate of the whole object followed by Destroy on every script,
        /// collider and body the copy came with — and on the one thing anybody actually walks a
        /// portal with, the player, that produced a second live networked player. Unity refuses to
        /// remove a component another component declares [RequireComponent] on, so the strip pass
        /// logged seven "Can't remove X because Y depends on it" errors and left the copy running
        /// with its Rigidbody, its NetworkObject, its input listeners and its save identity — the
        /// save system's own log recorded the duplicate reassigning an entity id.
        ///
        /// The mover here is that shape in miniature: a body carrying a script that requires it.
        /// </summary>
        [Test]
        public void ACloneIsMadeOfRenderersAndNothingElse()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewVisibleMover();
            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);

            entry.AdvanceTraversal();

            GameObject clone = mover.GetComponent<PortalTraveller>().Clone;
            Assert.IsNotNull(clone, "standing in an aperture produced no clone at all");

            foreach (Component part in clone.GetComponentsInChildren<Component>(true))
                Assert.IsTrue(part is Transform || part is MeshFilter || part is Renderer,
                              $"the clone carries a live {part.GetType().Name}. A clone is a " +
                              "picture, not a second copy of the object.");
        }

        /// <summary>
        /// And it is a picture of THIS object — same mesh, same materials.
        ///
        /// The cheap way to satisfy the test above is to clone nothing, which passes it and shows
        /// the player half a body at every aperture. This is the other half of the contract.
        /// </summary>
        [Test]
        public void ACloneShowsTheSameMeshAndMaterials()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewVisibleMover();
            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);

            entry.AdvanceTraversal();

            GameObject clone = mover.GetComponent<PortalTraveller>().Clone;
            var source = mover.GetComponentInChildren<MeshFilter>();
            var picture = clone.GetComponentInChildren<MeshFilter>();

            Assert.IsNotNull(picture, "the clone has nothing to draw");
            Assert.AreEqual(source.sharedMesh, picture.sharedMesh,
                            "the clone is not made of the same mesh as the original");
            Assert.AreEqual(source.GetComponent<MeshRenderer>().sharedMaterial,
                            picture.GetComponent<MeshRenderer>().sharedMaterial,
                            "the clone is not wearing the original's material");
        }

        /// <summary>
        /// The picture stands out of the FAR aperture, at the transferred pose.
        ///
        /// Pinned per renderer rather than on the clone's root, because that is what the halves
        /// meeting at the seam depends on: an object whose parts move relative to each other —
        /// every animated rig — has to be posed part by part or the picture is the object's rest
        /// pose standing in the doorway.
        /// </summary>
        [Test]
        public void ACloneStandsOutOfTheFarAperture()
        {
            Portal entry = NewPortal("entry", Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right));
            Portal.Link(entry, exit);

            GameObject mover = NewVisibleMover();
            Place(mover, new Vector3(0f, 0f, 0.6f));
            RequirePhysicsQueries(mover);

            entry.AdvanceTraversal();

            Transform source = mover.GetComponentInChildren<MeshFilter>().transform;
            Transform picture = mover.GetComponent<PortalTraveller>()
                                     .Clone.GetComponentInChildren<MeshFilter>().transform;

            Vector3 expected = exit.TransferFrom(entry).MultiplyPoint3x4(source.position);

            Assert.Less(Vector3.Distance(picture.position, expected), 0.01f,
                        "the clone is not standing where the far aperture puts it");
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
        /// A mover shaped like the thing the clone broke on: a body, a collider, something to
        /// look at, and a script that declares [RequireComponent] on the body.
        ///
        /// Unlike <see cref="NewMover"/> this one leaves the clone switched ON — the clone is
        /// what these tests are about.
        /// </summary>
        private GameObject NewVisibleMover()
        {
            var go = new GameObject("visible mover");
            spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(0.6f, 1.8f, 0.6f);
            go.AddComponent<Rigidbody>().isKinematic = true;

            // The real component from the real failure, so the test fails for the reason the game
            // did rather than for a made-up dependency of its own.
            go.AddComponent<PlayerMovement>();

            // A primitive, so the mesh and material are real without loading a project asset. It
            // is a CHILD, and offset, because a clone posed only at its root would still pass a
            // test where the only renderer sits at the origin.
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            go.AddComponent<PortalTraveller>();

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
