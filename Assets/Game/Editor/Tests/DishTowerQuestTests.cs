// The satellite tower's part in the opening quest: the dish starting nearly straight up, its drives dead
// until the grappling hook leaves its board, the one marker on the tower being the transmitter itself, and
// the two remarks the lander makes along the way.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types. Built
// by hand: nothing runs Awake or OnEnable in EditMode, so both are invoked or stood in for explicitly.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class DishTowerQuestTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string TowerPrefabPath = "Assets/Game/Prefabs/Environment/Structures/SatelliteTower/SatelliteTower.prefab";
        private const string StepPath = "Assets/Game/ScriptableObjects/Objectives/Steps/07_FitTransmitter.asset";
        private const string InputPath = "Assets/Game/Settings/Input/InputSystem_Actions.inputactions";

        private readonly List<Object> spawned = new();

        [TearDown]
        public void CleanUp()
        {
            TestHulls.Forget(spawned);
            LiveConsoles().RemoveAll(c => c == null || spawned.Contains(c.gameObject));

            foreach (Object o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        // ─────────────────────────── The start pose ───────────────────────────

        [Test]
        public void ANewWorldsDishPointsNearlyStraightUpAndAnOldTurnKeepsItsAngle()
        {
            DishRig rig = Rig();
            Assert.AreEqual(85f, rig.Elevation, 1e-3f, "a new world's dish does not point nearly straight up.");
            Assert.AreEqual(rig.MaxElevation, rig.StartElevation, 1e-3f, "the start is not the motor's safe top.");

            var saver = rig.gameObject.AddComponent<DishRigSaveable>();
            Assert.IsNull(saver.CaptureState(), "an untouched dish wrote a record.");

            rig.RestoreAngles(10f, 40f);
            JObject turned = RoundTrip(saver.CaptureState());
            Assert.IsNotNull(turned, "a dish turned down to 40 degrees wrote no record.");

            DishRig reloaded = Rig();
            reloaded.gameObject.AddComponent<DishRigSaveable>().RestoreState(turned);
            Assert.AreEqual(40f, reloaded.Elevation, 1e-3f, "a saved angle did not come back.");

            reloaded.GetComponent<DishRigSaveable>().RestoreState(null);
            Assert.AreEqual(85f, reloaded.Elevation, 1e-3f, "no record does not mean the starting pose.");

            var tower = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefabPath);
            Assert.IsNotNull(tower, $"No tower at {TowerPrefabPath}.");
            var shipped = new SerializedObject(tower.GetComponentInChildren<DishRig>(true));
            Assert.AreEqual(85f, shipped.FindProperty("startElevation").floatValue, 1e-3f,
                            "the shipped tower's dish does not start nearly straight up.");
        }

        // ─────────────────────────── The drives ───────────────────────────

        [Test]
        public void TheDishDrivesWakeOnlyOnceTheHookHasLeftItsBoardAndStayAwake()
        {
            InventoryItem hook = Item("TestGrapplingHook");
            DishConsole console = Console(out WallInventory board, out _, hook, null);
            Assert.IsTrue(board.TryPlace(hook, PackSurfaceId.WallGrid, Middle, 0f), "the fixture's hook did not fit its board.");

            Tick(console);
            Assert.IsFalse(console.DrivesUnlocked, "the drives answer with the hook still on its board.");
            Assert.AreEqual("Dish drives unresponsive", console.Prompt);

            board.TakeOut(hook.ID);
            Tick(console);
            Assert.IsTrue(console.DrivesUnlocked, "the drives stay dead after the hook was taken.");
            Assert.AreNotEqual("Dish drives unresponsive", console.Prompt);

            Assert.IsTrue(board.TryPlace(hook, PackSurfaceId.WallGrid, Middle, 0f));
            Tick(console);
            Assert.IsTrue(console.DrivesUnlocked, "putting the hook back locked the dish again.");

            var saver = console.gameObject.AddComponent<DishConsoleSaveable>();
            JObject record = RoundTrip(saver.CaptureState());
            Assert.IsNotNull(record, "awake drives wrote no record.");

            DishConsole reloaded = Console(out WallInventory fullBoard, out _, hook, null);
            Assert.IsTrue(fullBoard.TryPlace(hook, PackSurfaceId.WallGrid, Middle, 0f));
            reloaded.gameObject.AddComponent<DishConsoleSaveable>().RestoreState(record);
            Tick(reloaded);
            Assert.IsTrue(reloaded.DrivesUnlocked, "a world whose drives were awake reloaded them dead.");

            // An old save: no record, the hook already gone from the board's own saved contents.
            DishConsole old = Console(out _, out _, hook, null);
            old.gameObject.AddComponent<DishConsoleSaveable>().RestoreState(null);
            Tick(old);
            Assert.IsTrue(old.DrivesUnlocked, "an old world whose hook was taken is locked out of its dish.");
        }

        // ─────────────────────────── The one marker ───────────────────────────

        [Test]
        public void TheOnlyMarkerOnTheTowerIsTheTransmitterItself()
        {
            InventoryItem transmitter = Item("TestDishTransmitter");
            ShipPartRack rack = TestHulls.TransmitterRack(spawned, broken: true, out _);
            Console(out _, out WallInventory cradle, Item("TestHook"), transmitter);
            cradle.transform.position = new Vector3(40f, 66f, 20f);
            Assert.IsTrue(cradle.TryPlace(transmitter, PackSurfaceId.WallGrid, Middle, 0f));

            FitTransmitterStep step = Step(transmitter);
            var world = new ObjectiveWorld();

            Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 atSocket));
            Assert.AreEqual(world.SocketOf(ShipPartKind.Transmitter).Centre, atSocket, "the burnt unit is not the first marker.");

            rack.RestoreMasks(0b01, 0);
            Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 onDish), "no marker on the transmitter while it is on the dish.");
            Assert.AreEqual(cradle.transform.position, onDish, "the tower's marker is not on the transmitter.");
            Assert.IsFalse(step.TryGetBeacon(world, out _), "a second marker stands on the tower.");
            Assert.IsTrue(step.TryGetTransmitterOnDish(out _));

            cradle.TakeOut(transmitter.ID);
            Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 home), "no marker once the transmitter is off the dish.");
            Assert.AreEqual(world.SocketOf(ShipPartKind.Transmitter).Centre, home, "a carried transmitter is not marked home.");
        }

        // ─────────────────────────── The remarks ───────────────────────────

        [Test]
        public void TheHookRemarkComesDueWhenTheHookLeavesItsBoard()
        {
            InventoryItem hook = Item("TestHook");
            TestHulls.TransmitterRack(spawned, broken: false, out _);
            DishConsole console = Console(out WallInventory board, out _, hook, null);
            Assert.IsTrue(board.TryPlace(hook, PackSurfaceId.WallGrid, Middle, 0f));

            FitTransmitterStep step = Step(null);
            var world = new ObjectiveWorld();
            Assert.AreEqual(2, step.RemarkCount);

            Tick(console);
            Assert.IsFalse(step.IsRemarkDue(world, FitTransmitterStep.HookRemark), "the hook remark came before the hook moved.");
            Assert.IsFalse(step.IsRemarkDue(world, FitTransmitterStep.TowerRemark), "the tower remark came with nobody near the tower.");

            board.TakeOut(hook.ID);
            Tick(console);
            Assert.IsTrue(step.IsRemarkDue(world, FitTransmitterStep.HookRemark), "taking the hook brought no remark.");

            var progress = ObjectiveDirector.Resolve(AssetDatabase.LoadAssetAtPath<ObjectiveChain>(
                "Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset"), "fit-transmitter", false, true, 0b11);
            Assert.AreEqual(0b11, progress.Remarks, "a save's said remarks do not come back, so they would be said again.");
        }

        [Test]
        public void TheHookRemarkNamesTheRealKeys()
        {
            var step = AssetDatabase.LoadAssetAtPath<FitTransmitterStep>(StepPath);
            Assert.IsNotNull(step, $"No step at {StepPath}.");
            string line = string.Join(" ", step.RemarkLines(FitTransmitterStep.HookRemark));
            string tower = string.Join(" ", step.RemarkLines(FitTransmitterStep.TowerRemark));
            StringAssert.Contains("dish", tower);

            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            Assert.IsNotNull(input, $"No input actions at {InputPath}.");

            foreach ((string action, string key) in new[] { ("BodyInventory", "i"), ("GauntletLeft", "q"), ("GauntletRight", "e") })
            {
                InputAction bound = input.FindAction(action);
                Assert.IsNotNull(bound, $"No '{action}' action.");
                Assert.IsTrue(bound.bindings.Any(b => b.path == "<Keyboard>/" + key),
                              $"'{action}' is no longer on {key.ToUpperInvariant()}, but the lander still says so.");
                StringAssert.Contains(key.ToUpperInvariant(), line);
            }
        }

        // ─────────────────────────── Fixture ───────────────────────────

        private DishRig Rig()
        {
            var go = new GameObject("TestDish");
            spawned.Add(go);
            var rig = go.AddComponent<DishRig>();
            typeof(DishRig).GetMethod("Awake", Hidden).Invoke(rig, null);
            return rig;
        }

        /// <summary>A console with a hook board and a transmitter cradle, registered as the live one.</summary>
        private DishConsole Console(out WallInventory board, out WallInventory cradle, InventoryItem hook, InventoryItem transmitter)
        {
            DishRig rig = Rig();
            board = Wall("CatwalkGearBoard");
            cradle = Wall("TransmitterCradle");

            var console = rig.gameObject.AddComponent<DishConsole>();
            var so = new SerializedObject(console);
            so.FindProperty("rig").objectReferenceValue = rig;
            so.FindProperty("hookBoard").objectReferenceValue = board;
            so.FindProperty("hookItem").objectReferenceValue = hook;
            so.FindProperty("transmitterCradle").objectReferenceValue = cradle;
            so.ApplyModifiedPropertiesWithoutUndo();

            List<DishConsole> live = LiveConsoles();
            live.RemoveAll(c => c == null);
            live.Insert(0, console);
            return console;
        }

        private WallInventory Wall(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);

            var surfaceGo = new GameObject("SURF_WallGrid");
            surfaceGo.transform.SetParent(go.transform, false);
            var surface = surfaceGo.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, PackSurfaceId.WallGrid);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, Vector2.one * 0.9f * Cells);
            return go.AddComponent<WallInventory>();
        }

        private static float Cells => PackGrid.Cell / PackScale.LegacyCell;

        private static Vector2 Middle => Vector2.one * 0.45f * Cells;

        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = item.itemName = item.ID = name;
            spawned.Add(item);
            Registry<InventoryItem>.Register(item);
            return item;
        }

        private FitTransmitterStep Step(InventoryItem transmitter)
        {
            var step = ScriptableObject.CreateInstance<FitTransmitterStep>();
            spawned.Add(step);
            var so = new SerializedObject(step);
            so.FindProperty("transmitter").objectReferenceValue = transmitter;
            so.ApplyModifiedPropertiesWithoutUndo();
            return step;
        }

        private static void Tick(DishConsole console) =>
            typeof(DishConsole).GetMethod("Update", Hidden).Invoke(console, null);

        private static List<DishConsole> LiveConsoles() =>
            (List<DishConsole>)typeof(DishConsole).GetField("live", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private static JObject RoundTrip(object captured) =>
            captured == null ? null : JObject.Parse(JsonConvert.SerializeObject(captured, SaveSerializer.Settings));
    }
}
