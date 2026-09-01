// Guards the ship-parts salvage loop: the modules a player finds, the sockets they go into, and
// the two rules that make the loop a loop (one module per socket, one module per pack rack).
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class ShipPartsTests
    {
        private const string ShipPrefabPath =
            "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";
        private const string ItemDir = "Assets/Game/Resources/Items/ShipParts";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The rack face of the expedition rig, in cells. See PackSurfaceId.Rack.</summary>
        private const int RackCells = 9;

        private readonly List<GameObject> spawned = new();

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────────────────────── Content ───────────────────────────

        /// <summary>
        /// Every kind a socket can ask for is a module a player can actually be holding, and every
        /// one of those is registered.
        ///
        /// The failure this catches is silent twice over: a kind with no item is a hole in the hull
        /// that nothing in the game can fill, and an unregistered prefab drops on CLIENTS ONLY, so
        /// playing as the host can never find it missing.
        /// </summary>
        [Test]
        public void EveryPartKind_HasOneRegisteredItem()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsNotNull(list, $"No network prefab list at {NetworkPrefabsPath} — the rest of " +
                                   "this test would prove nothing about registration.");

            List<InventoryItem> items = ItemsOnDisk();
            Assert.IsNotEmpty(items, $"No ship part items under {ItemDir}. Run " +
                                     "Tools/Items/Build Ship Parts.");

            foreach (ShipPartKind kind in Kinds())
            {
                List<InventoryItem> matching = items
                    .Where(item => item.itemPrefab != null &&
                                   item.itemPrefab.GetComponent<ShipPartItem>() is { } part &&
                                   part.Kind == kind)
                    .ToList();

                Assert.AreEqual(1, matching.Count,
                    $"{kind} is carried by {matching.Count} item(s), expected exactly one. A kind " +
                    "with none is a socket nothing in the game can ever fill.");

                Assert.IsTrue(list.Contains(matching[0].itemPrefab),
                    $"{kind}'s prefab is not in {NetworkPrefabsPath}. Dropping it from the hotbar " +
                    "routes through GameServices.World.Spawn, which fails on clients only.");
            }
        }

        /// <summary>
        /// The ship has a socket for every kind, and its sockets are wired.
        ///
        /// This is the test that fails when ship_lander_blockout.blend is re-exported with a module
        /// renamed: the builder would otherwise ship a hull whose reactor mounts simply do not
        /// exist, and the only symptom in play is an item that fits nothing anywhere.
        /// </summary>
        [Test]
        public void PlayerShip_HasASocketForEveryKind()
        {
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            Assert.IsNotNull(ship, $"No ship prefab at {ShipPrefabPath}.");

            var rack = ship.GetComponent<ShipPartRack>();
            Assert.IsNotNull(rack, "PlayerShip has no ShipPartRack — run " +
                                   "Tools/Vehicles/Build PlayerShip Prefab.");

            IReadOnlyList<ShipPartSocket> sockets = rack.Sockets;
            Assert.IsFalse(sockets.Any(s => s == null),
                "The rack has an empty socket slot. Its index is a bit of the saved mask, so a " +
                "null there shifts every module after it onto the wrong bit.");

            foreach (ShipPartKind kind in Kinds())
                Assert.IsTrue(sockets.Any(s => s.Kind == kind),
                    $"No socket on PlayerShip takes {kind}. The mesh was renamed or dropped — " +
                    "check PART_KINDS in ship_parts.py and re-run both export scripts.");

            Assert.AreEqual(0, rack.AuthoredMask,
                "PlayerShip is authored with modules already fitted. It is meant to spawn wrecked; " +
                "a whole ship leaves the salvage loop with nothing to do.");
        }

        // ─────────────────────────── The fitting rule ───────────────────────────

        [Test]
        public void TryInstall_FitsAMatchingModuleOnce()
        {
            ShipPartRack rack = BuildRack(ShipPartKind.NuclearMotor, ShipPartKind.AirIntake);

            Assert.IsTrue(rack.TryInstall(0, ShipPartKind.NuclearMotor),
                "A matching module was refused by an empty socket.");
            Assert.IsTrue(rack.IsInstalled(0), "TryInstall reported success but the socket is empty.");

            // Host dispatch re-enters, and two players can press in the same frame. A socket that
            // accepted twice would consume the second module for nothing.
            Assert.IsFalse(rack.TryInstall(0, ShipPartKind.NuclearMotor),
                "A filled socket accepted a second module. Both players are then billed for one fit.");
        }

        [Test]
        public void TryInstall_RefusesTheWrongKindAndAnUnknownSocket()
        {
            ShipPartRack rack = BuildRack(ShipPartKind.NuclearMotor, ShipPartKind.AirIntake);

            Assert.IsFalse(rack.TryInstall(1, ShipPartKind.NuclearMotor),
                "An air intake socket took a nuclear motor.");
            Assert.IsFalse(rack.IsInstalled(1), "A refused fit still filled the socket.");

            Assert.IsFalse(rack.TryInstall(7, ShipPartKind.NuclearMotor),
                "A socket index off the end of the rack was accepted. That index is a bit shift; " +
                "out of range it would corrupt the mask rather than fail.");
            Assert.IsFalse(rack.TryInstall(-1, ShipPartKind.NuclearMotor),
                "A negative socket index was accepted.");
        }

        /// <summary>
        /// The saved mask survives a round trip through the rack, and a restore of the authored
        /// mask resets a repaired hull rather than leaving the previous world's repairs on it.
        /// </summary>
        [Test]
        public void RestoreMask_RoundTripsAndResets()
        {
            ShipPartRack rack = BuildRack(ShipPartKind.NuclearMotor, ShipPartKind.AirIntake);

            rack.TryInstall(0, ShipPartKind.NuclearMotor);
            rack.TryInstall(1, ShipPartKind.AirIntake);
            int saved = rack.InstalledMask;

            rack.RestoreMask(0);
            Assert.IsFalse(rack.IsInstalled(0), "Restoring an empty mask left a module fitted.");

            rack.RestoreMask(saved);
            Assert.IsTrue(rack.IsInstalled(0) && rack.IsInstalled(1),
                "A saved mask did not come back. Every module a player hauled home is then lost " +
                "on the first load.");
        }

        // ─────────────────────────── The pack rule ───────────────────────────

        /// <summary>
        /// A module takes the whole rack and fits nowhere else.
        ///
        /// That is the cost the salvage loop is built on — you haul an engine or you carry your
        /// gear — and it is carried entirely by the authored shape, so a missing or resized row in
        /// PackShapes.asset silently removes the tradeoff.
        /// </summary>
        [Test]
        public void EveryModule_FillsTheWholeRack()
        {
            PackShapeLibrary library = LoadShapeLibrary();

            foreach (InventoryItem item in ItemsOnDisk())
            {
                PackShape shape = PackShapes.For(item, library);

                Assert.AreEqual(RackCells, shape.Width,
                    $"'{item.itemName}' is {shape.Width} cells wide, not the rack's {RackCells}. " +
                    "Other gear would fit beside it and the module would cost nothing to carry.");
                Assert.AreEqual(RackCells, shape.Height,
                    $"'{item.itemName}' is {shape.Height} cells deep, not the rack's {RackCells}.");
            }
        }

        // ─────────────────────────── Helpers ───────────────────────────

        private static IEnumerable<ShipPartKind> Kinds() =>
            System.Enum.GetValues(typeof(ShipPartKind)).Cast<ShipPartKind>();

        private static List<InventoryItem> ItemsOnDisk() =>
            AssetDatabase.FindAssets("t:InventoryItem", new[] { ItemDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<InventoryItem>)
                .Where(item => item != null)
                .ToList();

        private static PackShapeLibrary LoadShapeLibrary()
        {
            string[] found = AssetDatabase.FindAssets("t:PackShapeLibrary");
            Assert.IsNotEmpty(found, "No PackShapeLibrary in the project, so this test could not " +
                                     "tell an authored 9x9 from a derived block.");

            var library = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(
                AssetDatabase.GUIDToAssetPath(found[0]));
            Assert.IsNotNull(library, "The pack shape library asset did not load.");
            return library;
        }

        /// <summary>
        /// A rack with one socket per kind given, built by hand. Nothing here runs Awake — EditMode
        /// never does — so the rack starts on its serialized defaults, which is exactly the state a
        /// freshly placed wreck is in.
        /// </summary>
        private ShipPartRack BuildRack(params ShipPartKind[] kinds)
        {
            GameObject root = NewObject("TestShip");

            for (int i = 0; i < kinds.Length; i++)
            {
                // Named in index order so the rack's own name sort matches the order given here.
                var socketGo = new GameObject($"Part_{i}_{kinds[i]}");
                socketGo.transform.SetParent(root.transform, false);

                var socket = socketGo.AddComponent<ShipPartSocket>();
                var so = new SerializedObject(socket);
                SerializedProperty kind = so.FindProperty("kind");
                Assert.IsNotNull(kind, "ShipPartSocket.kind is gone or renamed — these tests can " +
                                       "no longer author a socket, so they would prove nothing.");
                kind.enumValueIndex = (int)kinds[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            ShipPartRack rack = root.AddComponent<ShipPartRack>();
            Assert.AreEqual(kinds.Length, rack.Sockets.Count,
                "The rack did not discover the sockets under it.");
            return rack;
        }
    }
}
