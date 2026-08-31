// PlayerShip: the second drivable ship (PlayerShipBuilder). The project-wide sweeps already
// prove it is wired and registered; these tests cover what a sweep cannot know — that the door
// state a player leaves behind actually comes back, and that the builder's output keeps the
// shape the design promised (7 moving parts, 2 switches, one boarding point) — and that
// the interior it promised is still a place you can stand.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
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
            Assert.AreEqual(7, parts.Length,
                "Back door, four sliding leaves, boarding stair and sill platform.");

            string[] names = parts.Select(p => p.name).ToArray();
            Assert.Contains("BackDoor", names);
            Assert.Contains("BoardingStair", names);
            Assert.Contains("SillPlatform", names);

            Assert.AreEqual(5, prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true).Length,
                "Every sliding leaf is a switch for the side assembly, plus the back door's own.");
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
    }
}
