// What a portal promises: two of them, and what goes in comes out the other at speed.
//
// Three things are pinned here, and each of them is a way the feature has been asked to be wrong.
//
//   • THE COUNT. A portal gun has two barrels and there are two holes in the world, full stop.
//     PortalPair owns a fixed array of two and re-fires MOVE the existing aperture rather than
//     opening another, which is also what keeps the render target alive across a re-fire. A
//     regression here does not throw — it litters the level with apertures and reallocates a
//     screen-sized RenderTexture on every trigger pull.
//
//   • THE TRANSFER. Position, rotation and velocity all come from Portal.TransferFrom and nothing
//     else is allowed to compute one, because the view and the exit disagreeing is the one bug a
//     player reads instantly. Rotating the velocity VECTOR rather than reapplying a speed along the
//     exit normal is what preserves a diagonal entry, and what makes "speedy thing goes in, speedy
//     thing comes out" true rather than aspirational.
//
//   • THE SEGMENT TEST. Everything that moves by rewriting its own transform — every raycasting
//     projectile in the game — goes through Portal.Crossing instead of the traversal trigger, since
//     a collider with no Rigidbody raises no trigger callback at all. It has to catch a shot that
//     passes through the opening and ignore one that passes the wall beside it.
//
// Edit mode, so nothing here may lean on Awake or OnEnable: AddComponent raises neither outside
// play mode, which is why Portal.All is populated by hand below rather than by the components
// registering themselves.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools
{
    public class PortalTraversalTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private GameObject prefab;

        [SetUp]
        public void SetUp()
        {
            Portal.All.Clear();
            prefab = NewPortal("PortalPrefab", Vector3.zero, Quaternion.identity, register: false);
        }

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

        /// <summary>An aperture facing <paramref name="rotation"/>'s forward, sized like the shipped one.</summary>
        private GameObject NewPortal(string name, Vector3 position, Quaternion rotation,
                                     bool register = true)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);

            Portal portal = go.AddComponent<Portal>();
            portal.SetSize(new Vector2(3.45f, 6.15f));

            if (register) Portal.All.Add(portal);
            spawned.Add(go);
            return go;
        }

        // ── The count ──────────────────────────────────────────────────────────

        [Test]
        public void FiringBothBarrelsRepeatedlyLeavesExactlyTwoApertures()
        {
            var owner = new GameObject("player");
            spawned.Add(owner);

            PortalPair pair = PortalPair.Of(owner);
            Portal source = prefab.GetComponent<Portal>();

            int before = Object.FindObjectsByType<Portal>(FindObjectsSortMode.None).Length;

            Portal firstPrimary = null;
            Portal firstSecondary = null;

            for (int shot = 0; shot < 4; shot++)
            {
                Portal primary = pair.Open(PortalPair.Primary, source,
                                           new Vector3(shot, 0f, 0f), Quaternion.identity,
                                           null, new Vector2(3.45f, 6.15f), Color.white);
                Portal secondary = pair.Open(PortalPair.Secondary, source,
                                             new Vector3(0f, 0f, shot + 10f),
                                             Quaternion.Euler(0f, 180f, 0f),
                                             null, new Vector2(3.45f, 6.15f), Color.white);

                if (shot == 0) { firstPrimary = primary; firstSecondary = secondary; }

                // The same two GameObjects every time. A re-fire that instantiated
                // instead would pass a count check taken only at the end while
                // still throwing away a render target on every trigger pull.
                Assert.AreSame(firstPrimary, primary, "the primary barrel opened a second aperture");
                Assert.AreSame(firstSecondary, secondary, "the secondary barrel opened a second aperture");
            }

            int after = Object.FindObjectsByType<Portal>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(2, after - before, "eight shots left something other than two apertures");

            Assert.IsNull(pair.Get(2), "the pair handed out a third aperture");
            Assert.IsNull(pair.Get(-1));

            Assert.AreSame(firstSecondary, firstPrimary.Linked, "the pair came back unlinked");
            Assert.AreSame(firstPrimary, firstSecondary.Linked);

            foreach (Portal opened in new[] { firstPrimary, firstSecondary })
                spawned.Add(opened.gameObject);
        }

        // ── The transfer ───────────────────────────────────────────────────────

        [Test]
        public void VelocityComesOutAtTheSpeedItWentIn()
        {
            // Facing each other down the X axis is the awkward case, not the easy
            // one: the transfer's own 180 degree spin has to compose with the exit
            // portal's facing, and a sign error there is invisible in position and
            // obvious in velocity.
            Portal entry = NewPortal("entry", Vector3.zero,
                                     Quaternion.LookRotation(Vector3.forward)).GetComponent<Portal>();
            Portal exit = NewPortal("exit", new Vector3(50f, 20f, -30f),
                                    Quaternion.LookRotation(Vector3.right)).GetComponent<Portal>();
            Portal.Link(entry, exit);

            Matrix4x4 transfer = exit.TransferFrom(entry);

            // A diagonal entry, which is what a straightened trajectory would hide.
            var velocity = new Vector3(3f, -18f, 24f);
            Vector3 carried = transfer.MultiplyVector(velocity);

            Assert.AreEqual(velocity.magnitude, carried.magnitude, 1e-3f,
                            "the traversal changed the speed");

            // And it comes out of the FRONT of the exit, not into the wall behind it.
            Vector3 approach = entry.transform.forward * -1f;
            Vector3 leaving = transfer.MultiplyVector(approach);
            Assert.Greater(Vector3.Dot(leaving, exit.transform.forward), 0.99f,
                           "the traversal put the exit velocity back through the wall");
        }

        [Test]
        public void PositionAndVelocityAgreeOnWhichWayIsOut()
        {
            Portal entry = NewPortal("entry", Vector3.zero,
                                     Quaternion.LookRotation(Vector3.forward)).GetComponent<Portal>();
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.up, Vector3.forward)).GetComponent<Portal>();
            Portal.Link(entry, exit);

            Matrix4x4 transfer = exit.TransferFrom(entry);

            // Just behind the entry plane — the instant after a crossing.
            Vector3 crossed = entry.transform.position - entry.transform.forward * 0.1f;
            Vector3 arrived = transfer.MultiplyPoint3x4(crossed);

            Assert.Greater(exit.SideOf(arrived), 0f,
                           "something that crossed arrived BEHIND the exit, inside its wall");
        }

        // ── The segment test ───────────────────────────────────────────────────

        [Test]
        public void ASegmentThroughTheOpeningIsCarried()
        {
            Portal entry = NewPortal("entry", Vector3.zero,
                                     Quaternion.LookRotation(Vector3.forward)).GetComponent<Portal>();
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right)).GetComponent<Portal>();
            Portal.Link(entry, exit);

            // A bullet's frame: in front of the plane to behind it, straight
            // through the middle. An aperture faces OUT into the room along its
            // forward, which is +Z here, so a shot arriving from the room starts
            // at +Z and travels toward the wall.
            Portal crossed = Portal.Crossing(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
                                             out Vector3 point, out Matrix4x4 transfer);

            Assert.AreSame(entry, crossed, "a shot fired straight through the opening missed it");
            Assert.AreEqual(0f, point.z, 1e-3f, "the crossing was not found on the plane");
            Assert.AreEqual(0f,
                            Vector3.Distance(exit.transform.position,
                                             transfer.MultiplyPoint3x4(entry.transform.position)),
                            1e-3f, "the transfer does not land on the exit");
        }

        [Test]
        public void ASegmentPastTheOpeningIsLeftAlone()
        {
            Portal entry = NewPortal("entry", Vector3.zero,
                                     Quaternion.LookRotation(Vector3.forward)).GetComponent<Portal>();
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right)).GetComponent<Portal>();
            Portal.Link(entry, exit);

            // Same plane, but out past the rim: the aperture ends where the picture
            // does, and the wall it is cut into carries on.
            Assert.IsNull(Portal.Crossing(new Vector3(9f, 0f, 1f), new Vector3(9f, 0f, -1f),
                                          out _, out _),
                          "a shot into the wall beside the aperture was teleported");
        }

        [Test]
        public void ASegmentIntoTheBackIsLeftAlone()
        {
            Portal entry = NewPortal("entry", Vector3.zero,
                                     Quaternion.LookRotation(Vector3.forward)).GetComponent<Portal>();
            Portal exit = NewPortal("exit", new Vector3(0f, 0f, 100f),
                                    Quaternion.LookRotation(Vector3.right)).GetComponent<Portal>();
            Portal.Link(entry, exit);

            // Back to front. The back of an aperture is the wall it is cut into,
            // and a wall is not a way in.
            Assert.IsNull(Portal.Crossing(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f),
                                          out _, out _),
                          "the back of an aperture let something through");
        }

        [Test]
        public void AnUnpairedApertureCarriesNothing()
        {
            NewPortal("lonely", Vector3.zero, Quaternion.LookRotation(Vector3.forward));

            Assert.IsNull(Portal.Crossing(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
                                          out _, out _),
                          "a portal with nowhere to go still swallowed a shot");
        }
    }
}
