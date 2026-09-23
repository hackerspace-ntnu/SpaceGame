// Guards the ship-parts salvage loop: the modules a player finds, the sockets they go into, one
// module per socket, and the haul ladder that decides how big and what shape a module is once it
// is off the hull and on the mat or the ship's gear wall.
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

        /// <summary>
        /// The ship's gear wall, in cells. See PackSurfaceId.WallGrid and
        /// the wall's SurfaceCellsAcross/Up — the largest face a module can be stowed on.
        /// </summary>
        private const int WallCellsAcross = 30;
        private const int WallCellsUp = 22;

        /// <summary>
        /// The single packSize every module shared before the haul ladder, in metres. Kept as the
        /// floor the ladder is measured against, not as a size anything still uses.
        /// </summary>
        private const float OldSharedPackSize = 0.80f;

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
                    "check the part-kind names on the ship model and re-export it.");

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
        /// A module is shaped by its own outline, and no two of them are the same rectangle.
        ///
        /// <para>
        /// Every module used to carry a solid nine-by-nine row in PackShapes.asset — the rack
        /// exactly, so hauling one cost the whole face. An authored row wins over the derived
        /// footprint outright, so the price of that rule was seven identical squares: the 11 m
        /// nuclear motor, the intake plate and the stubby belly turbine were one object to the
        /// layout and to the eye. The rows are gone and one
        /// coming back is silent — nothing throws, the modules simply stop being distinguishable
        /// on the mat and on the ship's gear wall.
        /// </para>
        /// </summary>
        [Test]
        public void EveryModule_IsShapedByItsOwnOutline()
        {
            PackShapeLibrary library = LoadShapeLibrary();
            var seen = new Dictionary<Vector2Int, string>();

            foreach (InventoryItem item in ItemsOnDisk())
            {
                Assert.IsNull(library.Find(item.ID),
                    $"'{item.itemName}' has a row in PackShapes.asset, which overrides its own " +
                    "outline. Every hull module is shaped by its silhouette.");

                PackShape shape = PackShapes.For(item, library);
                PackShape derived = PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

                Assert.AreEqual(derived.Width, shape.Width,
                    $"'{item.itemName}' is not the width its own outline asks for.");
                Assert.AreEqual(derived.Height, shape.Height,
                    $"'{item.itemName}' is not the depth its own outline asks for.");

                var footprint = new Vector2Int(shape.Width, shape.Height);
                Assert.IsFalse(seen.ContainsKey(footprint),
                    $"'{item.itemName}' occupies the same {shape.Width}x{shape.Height} cells as " +
                    $"'{seen.GetValueOrDefault(footprint)}'. Two modules the player cannot tell " +
                    "apart on the wall is the bug the nine-by-nine rows were removed to fix.");
                seen[footprint] = item.itemName;
            }
        }

        /// <summary>
        /// Every module is drawn bigger on the mat than the one size they all used to share.
        ///
        /// <para>
        /// The haul ladder is four brackets rather than a multiple of true size precisely because
        /// the family spans 6.2 to 1: a single ratio that keeps the nuclear motor inside the
        /// wall's 30 cells drops the intake and the belly turbine BELOW the 0.80 m every module
        /// was drawn at, which is the complaint the ladder answers. That floor is the thing worth
        /// pinning — the exact rungs are a tuning decision and live in the builder.
        /// </para>
        /// </summary>
        [Test]
        public void EveryModule_IsDrawnBiggerThanTheOldSharedSize()
        {
            foreach (InventoryItem item in ItemsOnDisk())
            {
                var grip = item.itemPrefab.GetComponent<ItemGrip>();
                Assert.IsNotNull(grip, $"'{item.itemName}' has no ItemGrip to size it.");

                Assert.Greater(grip.PackSize, OldSharedPackSize,
                    $"'{item.itemName}' is drawn at {grip.PackSize} m on the mat, no bigger than " +
                    $"the {OldSharedPackSize} m every module shared before the haul ladder.");
            }
        }

        /// <summary>
        /// The ladder never puts a smaller module ahead of a bigger one.
        ///
        /// <para>
        /// Brackets compress the range; they must not reorder it. A reactor core drawn longer
        /// than the nuclear motor is worse than the old squares — it does not merely fail to say
        /// what a module is, it says something false.
        /// </para>
        /// </summary>
        [Test]
        public void TheHaulLadder_KeepsTrueSizeOrder()
        {
            List<(string name, float trueLength, float packSize)> rungs = ItemsOnDisk()
                .Select(item =>
                {
                    Vector3 size = ItemBounds.Measure(item.itemPrefab, null).size;
                    var grip = item.itemPrefab.GetComponent<ItemGrip>();

                    return (item.itemName, Mathf.Max(size.x, Mathf.Max(size.y, size.z)),
                            grip != null ? grip.PackSize : 0f);
                })
                .OrderBy(row => row.Item2)
                .ToList();

            for (int i = 1; i < rungs.Count; i++)
            {
                Assert.GreaterOrEqual(rungs[i].packSize, rungs[i - 1].packSize,
                    $"'{rungs[i].name}' is {rungs[i].trueLength:F2} m against " +
                    $"'{rungs[i - 1].name}'s {rungs[i - 1].trueLength:F2} m, but is drawn " +
                    $"{rungs[i].packSize} m to its {rungs[i - 1].packSize} m.");
            }
        }

        /// <summary>
        /// Every module fits the ship's gear wall, and can still be lashed to the pack's rack.
        ///
        /// <para>
        /// The two containers bound the ladder from opposite ends, and neither refusal says
        /// anything in the console — an oversized module simply shows red wherever it is dragged.
        /// The wall is 30 x 22 cells and STRICT. The rack is 9 x 9 and allows overhang along u
        /// only, and only for RECTANGLES, which is exactly what removing the authored rows made
        /// these: a long module lashed across the rack occupies every cell of the columns it
        /// crosses and hangs off both ends. Either orientation counts — a quarter turn is allowed
        /// for a derived shape.
        /// </para>
        /// </summary>
        [Test]
        public void EveryModule_FitsTheGearWallAndTheRack()
        {
            PackShapeLibrary library = LoadShapeLibrary();
            Vector2 wall = new Vector2(WallCellsAcross, WallCellsUp) * PackGrid.Cell;
            Vector2 rack = new Vector2(RackCells, RackCells) * PackGrid.Cell;

            foreach (InventoryItem item in ItemsOnDisk())
            {
                PackShape shape = PackShapes.For(item, library);

                Assert.IsTrue(FitsEitherWay(PackSurfaceId.WallGrid, wall, shape),
                    $"'{item.itemName}' is {shape.Width}x{shape.Height} cells and fits no " +
                    $"orientation of the {WallCellsAcross}x{WallCellsUp} gear wall, so it can " +
                    "never be stowed on the ship.");

                Assert.IsTrue(FitsEitherWay(PackSurfaceId.Rack, rack, shape),
                    $"'{item.itemName}' is {shape.Width}x{shape.Height} cells and fits no " +
                    "orientation of the pack's rack even with its overhang, so it can only ever " +
                    "be carried in the hand.");
            }
        }

        /// <summary>Does this shape fit the face at either yaw, after the face's own clamp?</summary>
        private static bool FitsEitherWay(PackSurfaceId surface, Vector2 size, PackShape shape) =>
            Fits(surface, size, shape) || Fits(surface, size, shape.Rotated(1));

        private static bool Fits(PackSurfaceId surface, Vector2 size, PackShape oriented)
        {
            PackShape clamped = PackOverhang.Clamp(surface, size, oriented);
            Vector2Int grid = PackGrid.CellsOn(size);

            return clamped.Width <= grid.x && clamped.Height <= grid.y;
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
                                     "tell an authored row from a derived outline.");

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
