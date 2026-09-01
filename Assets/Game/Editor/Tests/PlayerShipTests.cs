// PlayerShip: the second drivable ship (PlayerShipBuilder). The project-wide sweeps already
// prove it is wired and registered; these tests cover what a sweep cannot know — that the door
// state a player leaves behind actually comes back, and that the builder's output keeps the
// shape the design promised (13 moving parts, two switchable assemblies, one boarding point) —
// and that the interior it promised is still a place you can stand.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class PlayerShipTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";

        // The player's capsule, from PlayerCharacterNetworked. Walkability means room for THIS,
        // not for a point.
        private const float BodyRadius = 0.5f;
        private const float BodyHeight = 2f;
        private const float StandingSkin = 0.02f;

        // The interior is full of trigger volumes that are not walls — the passenger seats' click
        // surfaces reach out over the aisle on purpose. Unity's queries count triggers by default,
        // and a probe that does would read a seat as a pillar. SpawnClearance.HasRoomToStand, which
        // is what actually decides whether a body fits somewhere in play, ignores them for exactly
        // this reason; these probes ask the same question and must ask it the same way.
        private const QueryTriggerInteraction NotTriggers = QueryTriggerInteraction.Ignore;

        private GameObject ship;

        [TearDown]
        public void TearDown()
        {
            if (ship != null) Object.DestroyImmediate(ship);
        }

        private GameObject InstantiateShip()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath} — run Tools ▸ Vehicles ▸ Build PlayerShip Prefab.");

            ship = Object.Instantiate(prefab);
            ship.transform.position = Vector3.zero;
            // Colliders are placed by the transform hierarchy, and nothing has stepped physics
            // yet — without this the overlap queries run against the prefab's untouched poses.
            Physics.SyncTransforms();
            return ship;
        }

        // Physics queries hit the whole edit scene, and a fixture that ran earlier can leave a
        // body standing in this one's ship. Everything these probes read is filtered back to the
        // instance under test, so the result does not depend on what else is in the scene.
        private IEnumerable<Collider> Ours(IEnumerable<Collider> hits) =>
            hits.Where(h => h.transform.IsChildOf(ship.transform));

        private IEnumerable<RaycastHit> Ours(IEnumerable<RaycastHit> hits) =>
            hits.Where(h => h.transform.IsChildOf(ship.transform));

        /// <summary>
        /// Highest ground anywhere under a standing body's footprint, or null if it is over
        /// nothing. Sampling the centre alone reads the deck's step up to the cockpit as a wall:
        /// the ray finds the low side, the capsule is then placed 0.4 m below the step it is
        /// beside, and its own width puts it inside the riser. A body that wide stands on the
        /// highest thing beneath it.
        /// </summary>
        private float? FloorUnderFootprint(Vector3 above)
        {
            float? highest = null;
            foreach (Vector3 offset in new[]
                     {
                         Vector3.zero,
                         Vector3.forward * BodyRadius, Vector3.back * BodyRadius,
                         Vector3.left * BodyRadius, Vector3.right * BodyRadius,
                     })
            {
                foreach (RaycastHit hit in Ours(Physics.RaycastAll(above + offset, Vector3.down,
                                                                  BodyHeight * 2f, ~0, NotTriggers)))
                    if (highest == null || hit.point.y > highest.Value)
                        highest = hit.point.y;
            }
            return highest;
        }

        /// <summary>A collider's own name plus, for a baked hull, the source mesh it came from.</summary>
        private static string Describe(Collider collider) =>
            collider is MeshCollider mesh && mesh.sharedMesh != null
                ? collider.name + " (" + mesh.sharedMesh.name + ")"
                : collider.name;

        private ArticulatedPart[] BayDoors() =>
            ship.GetComponentsInChildren<ArticulatedPart>(true)
                .Where(p => p.name.StartsWith("BayDoorLeaf"))
                .ToArray();

        /// <summary>
        /// Parts the aft doors and pushes the move into the physics scene. Instant, because nothing
        /// steps Update here — an animated open would never arrive.
        /// </summary>
        private void OpenBayDoors()
        {
            foreach (ArticulatedPart leaf in BayDoors())
                leaf.SetOpen(true, instant: true);
            Physics.SyncTransforms();
        }

        [Test]
        public void PlayerShip_KeepsItsOpenDoors() =>
            PersistenceProbe.For(PrefabPath)
                .Mutate(go =>
                {
                    // A player state: side door opened (which deploys stair + platform), back
                    // door left shut.
                    foreach (ArticulatedPart part in go.GetComponentsInChildren<ArticulatedPart>(true))
                        if (part.name != "BackDoor")
                            part.SetOpen(true, instant: true);
                })
                .AssertSurvivesRoundTrip();

        [Test]
        public void PlayerShip_HasTheBuiltShape()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath} — run Tools ▸ Vehicles ▸ Build PlayerShip Prefab.");

            ArticulatedPart[] parts = prefab.GetComponentsInChildren<ArticulatedPart>(true);
            Assert.AreEqual(13, parts.Length,
                "Back door, six bay-door panels, four sliding leaves, boarding stair and sill platform.");

            string[] names = parts.Select(p => p.name).ToArray();
            Assert.Contains("BackDoor", names);
            Assert.Contains("BoardingStair", names);
            Assert.Contains("SillPlatform", names);
            // Three panels a side, because the bulkhead has no pocket for a solid leaf.
            Assert.AreEqual(3, names.Count(n => n.StartsWith("BayDoorLeaf_Port")));
            Assert.AreEqual(3, names.Count(n => n.StartsWith("BayDoorLeaf_Stbd")));

            Assert.AreEqual(11, prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true).Length,
                "Every sliding leaf is a switch for the side assembly; the back door and every "
                + "bay-door panel are switches for the aft one.");
            // Four chairs: the front-left one drives the ship's root MountModule (the one the
            // SteerModule is bound to — the controls), the other three are passenger seats with
            // their own modules that no SteerModule references (sit, no helm).
            Assert.AreEqual(4, prefab.GetComponentsInChildren<SpaceGame.Agents.MountModule>(true).Length,
                "Root pilot module + three passenger seat modules.");
            Assert.IsEmpty(prefab.GetComponentsInChildren<MountStation>(true),
                "The ship is boarded with MountModule alone — no station redirects.");
            Assert.AreEqual(1, prefab.GetComponents<SpaceGame.Agents.SteerModule>().Length,
                "Only the root module grants control.");
            Assert.AreEqual(3, prefab.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith("PassengerSeat")), "Three ride-along seats.");
            Assert.IsNotNull(prefab.GetComponent<Unity.Netcode.NetworkObject>());
            Assert.AreNotEqual(0u, prefab.GetComponent<Unity.Netcode.NetworkObject>().PrefabIdHash,
                "Script-built prefabs ship GlobalObjectIdHash 0 unless re-imported — see builder.");
        }

        /// <summary>
        /// Look at a chair, get that chair — and only the pilot's gets the controls.
        /// </summary>
        /// <remarks>
        /// The ship is boarded the way every other mount is: the root MountModule is directly
        /// interactable, and Interactor resolves an IInteractable by walking UP from the collider
        /// it hit. That is what makes the pilot's chair the helm without anything sitting on it —
        /// and it is also what would quietly make the OTHER three chairs the helm. A passenger
        /// seat that loses its trigger volume keeps its module, its network sync and its save
        /// wiring; the only symptom is that sitting down flies the ship, and nothing logs.
        ///
        /// So this asserts through the real resolver rather than by counting components: exactly
        /// one chair answers with the module SteerModule is bound to, and the rest each answer
        /// with a seat of their own.
        /// </remarks>
        [Test]
        public void PlayerShip_EachChairOffersItsOwnSeat()
        {
            InstantiateShip();
            MountModule helm = ship.GetComponent<MountModule>();
            Assert.IsNotNull(helm, "The root module is the helm — SteerModule is bound to it.");

            var answered = new List<(string chair, MountModule seat)>();
            foreach (Transform chair in ship.GetComponentsInChildren<Transform>(true)
                         .Where(t => t.name.StartsWith("Cockpit_Seat_Command")))
            {
                // Straight down onto the seat from inside the cockpit: the canopy carries no
                // collider, so this is the one approach that is the same for all four chairs
                // however they are turned.
                Bounds seatBounds = chair.GetComponent<Renderer>().bounds;
                var ray = new Ray(seatBounds.center + Vector3.up * (seatBounds.extents.y + 0.6f),
                                  Vector3.down);
                RaycastHit[] hits = Ours(Physics.RaycastAll(ray, 3f)).ToArray();

                Assert.IsTrue(
                    Interactor.ResolveAlongRay(hits, hits.Length, out IInteractable found, out _),
                    $"Nothing interactable under a body looking at '{chair.name}'.");
                Assert.IsInstanceOf<MountModule>(found,
                    $"'{chair.name}' answers with {found.GetType().Name}, not a seat.");
                answered.Add((chair.name, (MountModule)found));
            }

            Assert.AreEqual(4, answered.Count, "Four command chairs.");

            string[] helmChairs = answered.Where(a => a.seat == helm).Select(a => a.chair).ToArray();
            Assert.AreEqual(1, helmChairs.Length,
                "Exactly one chair may take the controls, but these do: " + string.Join(", ", helmChairs));
            Assert.AreEqual(3, answered.Where(a => a.seat != helm).Select(a => a.seat).Distinct().Count(),
                "The other three chairs each seat their occupant in a seat of their own.");
        }

        /// <summary>
        /// A body must be able to walk the length of the main deck, down the middle.
        /// </summary>
        /// <remarks>
        /// This ship shipped once with its interior filled in. The collision was derived from the
        /// art by rule, and the rule that handled the hollow hull skins — shrink-wrap the surface
        /// into grid cells — has to make each cell span every surface point in it, so wherever the
        /// skin curved from floor to roof the cell became a pillar standing in the bay. Nothing
        /// about that is visible: the prefab looks right, the console says nothing, and you only
        /// find it by trying to walk. The collision comes from a baked convex decomposition now
        /// (see PlayerShipBuilder), and this is the assertion that says so in terms of the thing
        /// that actually broke.
        ///
        /// The centre line rather than the whole deck, because the deck slab is not the room: it
        /// runs on under the hull walls that stand on it, so its own footprint includes metres
        /// that were never floor. The aisle is the part the design does promise — the way from the
        /// back ramp forward — and it needs no threshold to be meaningful.
        ///
        /// Each position stands on whatever the floor turns out to be there rather than on the
        /// deck slab's own plane. The deck steps up toward the cockpit, and a probe held at one
        /// height reads the step as a wall — which is how the first version of this test failed on
        /// collision that was doing its job.
        /// </remarks>
        [Test]
        public void PlayerShip_MainDeckAisleIsWalkable()
        {
            Bounds deck = InstantiateShip().transform.Find("Model/Mesh_Deck_Main").GetComponent<Renderer>().bounds;

            // The aisle runs from the back ramp forward, and the aft end of it is a DOORWAY. A
            // closed door standing in the way is the doors working, not the interior filling in —
            // which is the only thing this test is about — so they are opened first.
            OpenBayDoors();

            var blocked = new List<string>();

            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float z = Mathf.Lerp(deck.min.z + BodyRadius, deck.max.z - BodyRadius, i / (float)steps);
                Vector3 above = new Vector3(deck.center.x, deck.max.y + BodyHeight, z);

                float? floor = FloorUnderFootprint(above);
                if (floor == null)
                {
                    blocked.Add($"z {z:F1}: no floor under the aisle");
                    continue;
                }

                // A little standing skin: a capsule seated exactly on the surface it stands on is
                // tangent to it, and tangency is not a reading worth arguing about.
                Vector3 feet = new Vector3(above.x, floor.Value + StandingSkin, above.z);
                Collider[] hit = Ours(Physics.OverlapCapsule(
                    feet + Vector3.up * BodyRadius,
                    feet + Vector3.up * (BodyHeight - BodyRadius), BodyRadius, ~0, NotTriggers)).ToArray();
                if (hit.Length > 0)
                    blocked.Add($"z {z:F1}: standing at y {floor.Value:F2} is inside " + Describe(hit[0]));
            }

            Assert.IsEmpty(blocked,
                $"{blocked.Count} of {steps + 1} standing positions along the main deck's centre " +
                "line are inside collision. The interior is filling in again — check the collision " +
                "bake in player_ship_export.py.");
        }

        /// <summary>
        /// PhysX refuses a concave MeshCollider on a non-kinematic Rigidbody, and the ship is one
        /// (it rests on the sand as dead weight when parked). A concave collider slipping into the
        /// build is an error raised at spawn time, in play, on whoever loads the ship first.
        /// </summary>
        [Test]
        public void PlayerShip_CollisionIsAllConvex()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsFalse(prefab.GetComponent<Rigidbody>().isKinematic,
                "The premise of this test: a kinematic body could carry concave colliders.");

            MeshCollider[] concave = prefab.GetComponentsInChildren<MeshCollider>(true)
                .Where(m => !m.convex)
                .ToArray();
            Assert.IsEmpty(concave.Select(m => m.name),
                "Every MeshCollider on the ship must be convex.");
        }

        /// <summary>
        /// Every leaf's switch must drive the whole assembly — stair and platform included. That
        /// is both the "any panel is interactable" contract and the "opening the door runs out
        /// the ladder" behaviour, and it lives in serialized wiring a refactor could drop.
        /// </summary>
        [Test]
        public void PlayerShip_EveryLeafSwitchDrivesTheWholeAssembly()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            ArticulatedPartInteraction[] sides = prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true)
                .Where(i => i.name.StartsWith("SlidingDoorLeaf"))
                .ToArray();
            Assert.AreEqual(4, sides.Length, "Each sliding leaf carries its own switch.");

            foreach (ArticulatedPartInteraction side in sides)
            {
                var so = new SerializedObject(side);
                SerializedProperty parts = so.FindProperty("parts");
                var driven = Enumerable.Range(0, parts.arraySize)
                    .Select(i => parts.GetArrayElementAtIndex(i).objectReferenceValue)
                    .OfType<ArticulatedPart>()
                    .Select(p => p.name)
                    .ToArray();

                Assert.AreEqual(6, driven.Length, side.name + ": four leaves + stair + platform.");
                Assert.Contains("BoardingStair", driven);
                Assert.Contains("SillPlatform", driven);
            }
        }

        /// <summary>
        /// The aft entrance is one switch: ramp and both leaves, carried by all three, so pressing
        /// any of them opens the lot. Wired in serialized arrays a refactor could quietly drop —
        /// and the way it fails is a ramp that lowers onto a shut door.
        /// </summary>
        [Test]
        public void PlayerShip_TheAftEntranceOpensAsOnePiece()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            ArticulatedPartInteraction[] aft = prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true)
                .Where(i => i.name == "BackDoor" || i.name.StartsWith("BayDoorLeaf"))
                .ToArray();
            Assert.AreEqual(7, aft.Length, "The ramp and all six bay-door panels each carry a switch.");

            foreach (ArticulatedPartInteraction switchOn in aft)
            {
                var so = new SerializedObject(switchOn);
                SerializedProperty parts = so.FindProperty("parts");
                var driven = Enumerable.Range(0, parts.arraySize)
                    .Select(i => parts.GetArrayElementAtIndex(i).objectReferenceValue)
                    .OfType<ArticulatedPart>()
                    .Select(p => p.name)
                    .ToArray();

                Assert.AreEqual(7, driven.Length, switchOn.name + ": ramp + all six panels.");
                Assert.Contains("BackDoor", driven);
                Assert.AreEqual(6, driven.Count(n => n.StartsWith("BayDoorLeaf")),
                    switchOn.name + ": every panel of both leaves.");
            }
        }

        /// <summary>
        /// The point of the doors: shut, the aft doorway is solid; open, a body fits through it.
        ///
        /// Both halves matter and each fails on its own. Leaves sized to the wrong aperture seal
        /// nothing — the ramp lowers onto a gap you can walk straight through, and the whole
        /// feature is invisible. Leaves with nowhere to retract to still LOOK like doors and still
        /// animate, but leave a doorway too narrow to enter, which reads in play as the ship
        /// refusing to let you aboard. Everything about them is measured at build time off geometry
        /// nothing names, so this asserts the measurement landed, not that the wiring exists.
        /// </summary>
        [Test]
        public void PlayerShip_AftDoorwaySealsShutAndClearsOpen()
        {
            InstantiateShip();

            ArticulatedPart[] leaves = BayDoors();
            Assert.AreEqual(6, leaves.Length, "Two leaves of three telescoping panels each.");

            // Where the two leaves meet, which is the middle of the doorway: the innermost panel of
            // each side, measured off the panels rather than re-derived, so this reads the same
            // aperture the builder actually used.
            Bounds port = LeafBounds(leaves.Single(l => l.name == "BayDoorLeaf_Port1"));
            Bounds stbd = LeafBounds(leaves.Single(l => l.name == "BayDoorLeaf_Stbd1"));
            Vector3 middle = (port.center + stbd.center) * 0.5f;

            Vector3 gap = new Vector3(BodyRadius * 2f, BodyHeight, 0.4f) * 0.5f;
            Assert.IsNotEmpty(
                Ours(Physics.OverlapBox(middle, gap, Quaternion.identity, ~0, NotTriggers)).ToArray(),
                "Shut, the aft doorway is open air — the leaves are not sealing it.");

            OpenBayDoors();

            Collider[] blocking = Ours(
                Physics.OverlapBox(middle, gap, Quaternion.identity, ~0, NotTriggers)).ToArray();
            Assert.IsEmpty(blocking.Select(Describe),
                "Open, the aft doorway is still blocked — the leaves have nowhere to retract to.");
        }

        private static Bounds LeafBounds(ArticulatedPart panel) =>
            panel.GetComponentInChildren<Renderer>().bounds;

        // ─────────── The inventory wall ───────────
        //
        // Placed by BuildInventoryWall, which measures its spot off the main deck and the side
        // door rather than carrying authored coordinates. That is the right way to place it and it
        // is also why these tests exist: a re-export that moves either landmark moves the wall, and
        // the way that fails is a rack standing in the middle of the room, or half inside the hull,
        // with nothing at all in the console.

        private WallInventory Wall() => ship.GetComponentInChildren<WallInventory>(true);

        [Test]
        public void PlayerShip_HasOneInventoryWall()
        {
            InstantiateShip();

            WallInventory[] walls = ship.GetComponentsInChildren<WallInventory>(true);
            Assert.AreEqual(1, walls.Length,
                            "The ship carries exactly one gear wall — every wall message names " +
                            "its own index, but nothing else here expects a second one.");

            PackSurface face = walls[0].GetComponentInChildren<PackSurface>(true);
            Assert.IsNotNull(face, "The wall has no placement face, so nothing can be put on it.");
            Assert.AreEqual(PackSurfaceId.WallGrid, face.Id);
            Assert.AreEqual(5.40f, face.Size.x, 0.001f);
            Assert.AreEqual(2.70f, face.Size.y, 0.001f);
        }

        /// <summary>
        /// The face looks into the room and its v axis points up.
        ///
        /// Both are worth pinning because neither survives a guess. A PackSurface's frame is local
        /// X = u, Z = v, Y = the normal, and there is no rotation that gives u-right, v-up and an
        /// outward normal at once — so the builder measures the surface and rotates the fitting to
        /// match rather than trusting the prefab's forward. A wall that came out facing the hull
        /// would look perfectly normal and accept nothing.
        /// </summary>
        [Test]
        public void PlayerShip_InventoryWallFacesIntoTheRoom()
        {
            InstantiateShip();

            PackSurface face = Wall().GetComponentInChildren<PackSurface>(true);
            Vector3 normal = ship.transform.InverseTransformDirection(face.transform.up);
            Vector3 up = ship.transform.InverseTransformDirection(face.transform.forward);

            Assert.Less(Mathf.Abs(normal.y), 0.01f, "The face should be vertical.");
            Assert.Greater(up.y, 0.99f, "The face's v axis should point up, not down or sideways.");

            // Inboard: the normal has to point back toward the ship's centreline, whichever side
            // the door put the wall on.
            //
            // Compared on the LATERAL axis alone, and against the face's CENTRE. A PackSurface's
            // transform sits at its rectangle's (0,0) corner, not in the middle of it, so a vector
            // from there to the ship's origin is mostly height and length — dotting a horizontal
            // normal against it answers 0.6 on a wall that is aimed perfectly.
            Vector3 centre = ship.transform.InverseTransformPoint(face.ToWorld(face.Size * 0.5f, 0f));

            Assert.Greater(Mathf.Abs(normal.x), 0.99f,
                           "The face should look straight across the ship, not along it.");
            Assert.Less(normal.x * centre.x, 0f,
                        "The face is pointing at the hull rather than into the room.");
        }

        /// <summary>
        /// The space gear occupies is empty.
        ///
        /// The wall is not flush against the hull and cannot be: the aft room's starboard side is a
        /// run of arch ribs whose feet reach 0.62 m inboard of the deck edge. So the fitting stands
        /// clear of them, and this is the test that says the clearance is still enough — a box the
        /// size of the grid, one item deep, in front of the face.
        /// </summary>
        [Test]
        public void PlayerShip_NothingBlocksTheInventoryWallsFace()
        {
            InstantiateShip();

            WallInventory wall = Wall();
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);

            // A third of a metre: deeper than the biggest thing the ladder puts on a wall lies, and
            // shallow enough that a probe standing in the aisle is not what fails this test.
            const float ItemDepth = 0.33f;

            Vector3 centre = face.ToWorld(face.Size * 0.5f, ItemDepth * 0.5f);
            var half = new Vector3(face.Size.x * 0.5f, ItemDepth * 0.5f, face.Size.y * 0.5f);

            Collider[] blocking = Ours(
                    Physics.OverlapBox(centre, half, face.transform.rotation, ~0, NotTriggers))
                .Where(c => !c.transform.IsChildOf(wall.transform))
                .ToArray();

            Assert.IsEmpty(blocking.Select(Describe),
                           "Something of the ship's own is standing in the wall's grid — gear " +
                           "placed there would be inside it.");
        }

        /// <summary>
        /// It stands on the deck rather than floating over it or sinking into it. Measured by
        /// dropping a ray from just under the fitting's own base, because the base is what the
        /// builder positions FROM — the grid band's height above the deck is a model constant.
        /// </summary>
        [Test]
        public void PlayerShip_InventoryWallStandsOnTheDeck()
        {
            InstantiateShip();

            WallInventory wall = Wall();
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);

            // The grid's bottom edge, then down to the deck. GRID_Z0 in inventory_wall.py is
            // 0.36 m, so a wall sitting on the floor puts its bottom row that far up.
            Vector3 bottom = face.ToWorld(new Vector2(face.Size.x * 0.5f, 0f), 0.2f);

            Assert.IsTrue(
                Physics.Raycast(bottom, Vector3.down, out RaycastHit hit, 3f, ~0, NotTriggers),
                "Nothing under the inventory wall at all — it is not standing on a deck.");

            float clearance = bottom.y - hit.point.y;
            Assert.That(clearance, Is.InRange(0.2f, 0.65f),
                        $"The wall's bottom row sits {clearance:0.00} m over the floor under it; " +
                        "the model puts it 0.36 m up, so anything far from that means the fitting " +
                        "is floating or buried.");
        }
    }
}
