// Guards the crash's oxygen beat: the plant thrown out of the lander, the carry that brings it back,
// the air the cabin has (or has not) while it is out, the objective that asks for it, and what an
// old save gets.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp. Built by hand; nothing runs Awake or OnEnable in
// EditMode, so the few lifecycle calls the rules need are made explicitly.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class OxygenPlantRecoveryTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ChainPath = "Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset";

        private readonly List<Object> spawned = new();

        [TearDown]
        public void CleanUp()
        {
            ActiveRacks().RemoveAll(r => r == null || spawned.Contains(r.gameObject));

            foreach (Object o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        // ─────────────────────────── The carry home ───────────────────────────

        [Test]
        public void ThePlantSnapsIntoItsMountOnlyWhenItIsOutAndClose()
        {
            OxygenPlantMount mount = Mount(out _);
            Liftable plant = LoosePlant(mount);

            Assert.IsFalse(mount.Accepts(plant), "a mount with its plant in it would take a second one.");

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsTrue(mount.Accepts(plant), "a mount whose plant is out refused it.");
            Assert.IsTrue(LiftDestinations.Reached(mount.Point + new Vector3(1f, 3f, 0.5f), mount.Point, mount.Radius),
                          "a plant carried to the mount, a deck above the ground probe, did not count as there.");
            Assert.IsFalse(LiftDestinations.Reached(mount.Point + Vector3.right * (mount.Radius + 0.5f), mount.Point, mount.Radius),
                           "a plant outside the mount's radius snapped in.");
        }

        [Test]
        public void TheEmptyMountTakesTheCarriedPlantAndSaysWhatIsMissingOtherwise()
        {
            OxygenPlantMount mount = Mount(out _);
            var dock = Track(new GameObject("PlantDock"));
            dock.AddComponent<BoxCollider>().isTrigger = true;
            var lift = dock.AddComponent<LiftDock>();
            var so = new SerializedObject(lift);
            so.FindProperty("destination").objectReferenceValue = mount;
            so.ApplyModifiedPropertiesWithoutUndo();
            Liftable plant = LoosePlant(mount);

            Assert.IsFalse(lift.Takes(plant), "an occupied mount offers to take a second plant.");
            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsTrue(lift.Takes(plant), "the empty mount does not take its own plant.");
            Assert.IsFalse(lift.Takes(null), "the empty mount offers to take nothing.");
            StringAssert.Contains("missing", lift.Prompt, "aimed at with empty hands, the mount does not say the plant is missing.");
        }

        [Test]
        public void TheShippedMountHasAnEmptyFrameADockLampsAndAGenerousReach()
        {
            GameObject ship = AssetDatabase.LoadAssetAtPath<GameObject>(EditorTools.OxygenPlantRecoveryAuthoring.ShipPath);
            OxygenPlantMount mount = ship.GetComponentInChildren<OxygenPlantMount>(true);
            Transform empty = mount.transform.Find(EditorTools.OxygenPlantRecoveryAuthoring.EmptyMountName);

            Assert.IsNotNull(empty, "nothing shows on the wall while the plant is out.");
            Assert.IsFalse(empty.gameObject.activeSelf, "the empty frame stands in front of the plant while it is in its mount.");
            Assert.IsNotNull(empty.GetComponentInChildren<LiftDock>(true), "the empty mount cannot be aimed at to set the plant in.");
            Assert.IsTrue(empty.GetComponentInChildren<LiftDock>(true).GetComponent<Collider>().isTrigger, "the dock volume is solid and blocks the cabin.");
            Assert.AreEqual(3, empty.GetComponentsInChildren<Presentation.SparkingCable>(true).Length, "the ripped cables do not all spark.");
            Assert.GreaterOrEqual(mount.Radius, 4f, "the carrier has to stand nearly in the wall to set a 3.8 m plant in.");
            Assert.IsNotNull(mount.GetComponent<OxygenPlantStatusLights>(), "nothing tells the crew the plant is running.");
            Assert.AreEqual(mount.Damage, mount.GetComponent<TorchRepairable>(), "the mount does not know its own cracks.");
        }

        [Test]
        public void TheLampsGlowGreenOnlyWhileThePlantRuns()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            TorchRepairable cracks = Cracks(mount, out _);
            var lampGo = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            Renderer lamp = lampGo.GetComponent<Renderer>();
            var lights = mount.gameObject.AddComponent<OxygenPlantStatusLights>();
            var so = new SerializedObject(lights);
            so.FindProperty("mount").objectReferenceValue = mount;
            so.FindProperty("lamps").arraySize = 1;
            so.FindProperty("lamps").GetArrayElementAtIndex(0).objectReferenceValue = lamp;
            so.ApplyModifiedPropertiesWithoutUndo();
            MethodInfo update = typeof(OxygenPlantStatusLights).GetMethod("Update", Hidden);

            Color Painted()
            {
                update.Invoke(lights, null);
                var block = new MaterialPropertyBlock();
                lamp.GetPropertyBlock(block);
                return block.GetColor("_BaseColor");
            }

            cracks.Damage();
            Power(generator);
            Color whileCracked = Painted();
            Assert.Greater(whileCracked.r, whileCracked.g, "a cracked plant shows green.");

            cracks.Solder(cracks.TotalSeconds);
            Color whileRunning = Painted();
            Assert.Greater(whileRunning.g, whileRunning.r, "a whole, powered plant in its mount does not show green.");
        }

        // ─────────────────────────── The crash reserve and the refill ───────────────────────────

        [Test]
        public void TheCrashPutsTheSuitOnAMinutesLongReserveThatDrainsFromFull()
        {
            var body = Track(new GameObject("Crew"));
            var suit = body.AddComponent<SuitOxygen>();
            typeof(SuitOxygen).GetMethod("Awake", Hidden).Invoke(suit, null);

            suit.EnterCrashReserve();
            Assert.IsTrue(suit.OnCrashReserve, "the crash did not put the suit on its emergency reserve.");
            Assert.GreaterOrEqual(suit.SuitSeconds, 180f, "the crash reserve is seconds, not minutes.");
            Assert.AreEqual(1f, suit.SuitFraction, 1e-4f, "the crash reserve does not read full when it starts.");
            Assert.IsTrue(suit.OnReserve, "a crew member on the crash reserve is not on reserve.");

            float half = SuitOxygen.SuitAfter(false, suit.SuitSeconds, suit.Ceiling, 0f, suit.SuitSeconds * 0.5f, 0f);
            Assert.AreEqual(suit.SuitSeconds * 0.5f, half, 1e-3f, "the reserve does not drain by the ordinary rule.");
            Assert.AreEqual(suit.Ceiling, SuitOxygen.CeilingAfter(suit.Ceiling, suit.SuitCapacity, hasAir: false), 1e-4f,
                            "the reserve shrank while it was spent, so its gauge would never drain.");
            Assert.AreEqual(suit.SuitCapacity, SuitOxygen.CeilingAfter(suit.Ceiling, suit.SuitCapacity, hasAir: true), 1e-4f,
                            "real air did not end the crash reserve.");

            suit.EnterCrashReserve();
            Assert.AreEqual(suit.Ceiling, suit.SuitSeconds, 1e-4f, "a second crash call topped the reserve up again.");
        }

        [Test]
        public void ASaveOnTheCrashReserveKeepsWhatIsLeftOfIt()
        {
            var body = Track(new GameObject("Crew"));
            var suit = body.AddComponent<SuitOxygen>();
            typeof(SuitOxygen).GetMethod("Awake", Hidden).Invoke(suit, null);

            suit.RestoreOxygen(150f);
            Assert.AreEqual(150f, suit.SuitSeconds, 1e-4f, "a reserve saved at 150 s came back clamped to a suit's 60.");
            Assert.IsTrue(suit.OnCrashReserve, "a reserve saved larger than a suit came back as an ordinary one.");
        }

        [Test]
        public void TheCrewHaveAirOnlyOnceAFilledBottleIsSeatedAndItSurvivesASave()
        {
            OxygenPlantMount mount = Mount(out _);
            var step = ScriptableObject.CreateInstance<RefillAirStep>();
            spawned.Add(step);
            var world = new ObjectiveWorld();

            Assert.IsFalse(step.IsMet(world), "the refill was met before anybody seated a bottle.");
            StringAssert.Contains("gear wall", step.Status(world), "a player with no bottle is not sent to the gear wall.");

            mount.RestoreRefilled(true);
            Assert.IsTrue(step.IsMet(world), "a filled bottle seated did not meet the refill.");

            mount.Restore(true, Vector3.one, Vector3.forward * 40f);
            var saver = mount.gameObject.AddComponent<OxygenPlantMountSaveable>();
            object captured = saver.CaptureState();
            OxygenPlantMount reloaded = Mount(out _);
            reloaded.gameObject.AddComponent<OxygenPlantMountSaveable>()
                    .RestoreState(Newtonsoft.Json.Linq.JObject.FromObject(captured, SaveSerializer.Serializer));
            Assert.IsTrue(reloaded.AirRefilled, "the crew's air was forgotten by a reload.");

            var old = Newtonsoft.Json.Linq.JObject.Parse("{ \"detached\": false, \"furrowFrom\": {\"x\":0,\"y\":0,\"z\":0}, \"furrowTo\": {\"x\":0,\"y\":0,\"z\":1} }");
            reloaded.GetComponent<OxygenPlantMountSaveable>().RestoreState(old);
            Assert.IsFalse(reloaded.AirRefilled, "a record from before the refill read as refilled.");
        }

        [Test]
        public void TheTorchIsExplainedOnceThePlantIsSeatedCracked()
        {
            OxygenPlantMount mount = Mount(out _);
            TorchRepairable cracks = Cracks(mount, out _);
            var step = ScriptableObject.CreateInstance<RecoverOxygenPlantStep>();
            spawned.Add(step);
            var world = new ObjectiveWorld();

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse(step.IsRemarkDue(world, 0), "the torch was explained while the plant lay outside.");

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            cracks.Damage();
            Assert.IsTrue(step.IsRemarkDue(world, 0), "a cracked plant in its mount did not get the torch explained.");
            string said = string.Join(" ", step.RemarkLines(0));
            StringAssert.Contains("gear wall", said, "the torch remark does not say where the torch is.");
            StringAssert.Contains("LMB", said, "the torch remark does not say which button solders.");
        }

        [Test]
        public void TheShippedChainRefillsAirRightAfterThePlantAndTheWallBottlesComeEmpty()
        {
            var chain = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(ChainPath);
            IReadOnlyList<ObjectiveStep> steps = chain.Steps;
            Assert.IsInstanceOf<RefillAirStep>(steps[2], "the refill is not the step right after the oxygen plant.");
            Assert.AreEqual("refill-air", steps[2].Id, "the refill step's id changed, which orphans a save at it.");
            Assert.IsNotNull(new SerializedObject(steps[2]).FindProperty("bottle").objectReferenceValue, "the refill does not know the bottle.");
            Assert.IsNotNull(new SerializedObject(steps[1]).FindProperty("torch").objectReferenceValue, "the plant step cannot point at the torch.");
            StringAssert.Contains("B", string.Join(" ", steps[2].Briefing), "the refill briefing never says which key opens the pack.");

            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(EditorTools.SolderingTorchBuilder.GearWallPath).GetComponent<SpaceGame.Items.WallInventory>();
            Assert.AreEqual(0f, new SerializedObject(wall).FindProperty("perCrewCharge").floatValue, 1e-4f,
                            "the gear wall's crew bottles come full, so there is nothing to fill.");
        }

        // ─────────────────────────── The cracks ───────────────────────────

        [Test]
        public void APlantCarriedHomeIsCrackedAndRunsOnlyOnceEverySeamIsSoldered()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            TorchRepairable cracks = Cracks(mount, out Transform[] seams);
            Power(generator);

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            cracks.Damage();
            Assert.IsTrue(mount.Damaged, "the plant snapped home whole, so the torch has nothing to do.");
            Assert.IsFalse(mount.Running, "a cracked plant with a cell in it gives the cabin air.");
            Assert.AreEqual(0, cracks.WorkingSeam, "the first seam is not the one glowing.");

            Assert.IsTrue(cracks.IsOnWorkingSeam(seams[0].position + Vector3.up * 0.1f), "a flame on the glowing seam did not count.");
            Assert.IsFalse(cracks.IsOnWorkingSeam(seams[2].position), "a flame on a seam out of turn counted.");

            cracks.Solder(2f);
            Assert.AreEqual(1, cracks.WorkingSeam, "two seconds of flame did not close the first seam.");
            cracks.Solder(3.9f);
            Assert.IsTrue(mount.Damaged, "the plant was whole before six seconds of flame.");
            cracks.Solder(0.2f);
            Assert.IsFalse(mount.Damaged, "six seconds of flame left the plant cracked.");
            Assert.IsTrue(mount.Running, "a whole, powered plant in its mount does not run.");
        }

        [Test]
        public void HalfSolderedWorkSurvivesASaveAndAWholePlantSavesNothing()
        {
            OxygenPlantMount mount = Mount(out _);
            TorchRepairable cracks = Cracks(mount, out _);
            var saver = mount.gameObject.AddComponent<TorchRepairableSaveable>();

            Assert.IsNull(saver.CaptureState(), "a plant that was never cracked writes a record.");

            cracks.Damage();
            cracks.Solder(3.5f);
            object captured = saver.CaptureState();
            Assert.IsNotNull(captured, "a half-soldered plant was not saved.");

            OxygenPlantMount reloaded = Mount(out _);
            TorchRepairable again = Cracks(reloaded, out _);
            reloaded.gameObject.AddComponent<TorchRepairableSaveable>()
                    .RestoreState(Newtonsoft.Json.Linq.JObject.FromObject(captured, SaveSerializer.Serializer));
            Assert.IsTrue(again.NeedsRepair, "a half-soldered plant reloaded whole.");
            Assert.AreEqual(3.5f, again.SolderedSeconds, 1e-4f, "the reload lost the soldering already done.");
            Assert.AreEqual(1, again.WorkingSeam, "the reload put the glow back on a seam already closed.");

            again.GetComponent<TorchRepairableSaveable>().RestoreState(null);
            Assert.IsFalse(again.NeedsRepair, "a save with no record (older than the cracks) loaded a cracked plant.");
        }

        [Test]
        public void TheObjectiveWalksFromLiftThroughCarryAndSolderToTheCell()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            TorchRepairable cracks = Cracks(mount, out Transform[] seams);
            var step = ScriptableObject.CreateInstance<RecoverOxygenPlantStep>();
            spawned.Add(step);
            var world = new ObjectiveWorld();

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Liftable plant = Track(new GameObject("LoosePlant")).AddComponent<Liftable>();
            List<Liftable> loads = ActiveLoads();
            loads.Add(plant);
            try
            {
                StringAssert.Contains("Lift it by the handle", step.Status(world), "the plant lying outside does not say how to move it.");

                SetCarried(plant, true);
                StringAssert.StartsWith("Carrying", step.Status(world), "a carried plant does not say how far it has to go.");
                Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 home) && home == mount.Point, "while carried the waypoint is not the mount.");
            }
            finally
            {
                loads.Remove(plant);
            }

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            cracks.Damage();
            StringAssert.Contains("solder the seams", step.Status(world), "a cracked plant does not ask for the torch.");
            StringAssert.Contains("0/3", step.Status(world), "the seams left are not counted.");
            Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 seam) && seam == seams[0].position, "the waypoint is not on the glowing seam.");

            cracks.Solder(2f);
            StringAssert.Contains("1/3", step.Status(world), "a closed seam is not counted.");

            cracks.Solder(4f);
            Assert.AreEqual("Fit a power cell", step.Status(world), "a whole plant does not ask for its cell.");
            Assert.IsFalse(step.IsMet(world), "the objective was met with no cell.");

            Power(generator);
            Assert.IsTrue(step.IsMet(world), "the plant is home, whole and powered, and the objective is not met.");
        }

        // ─────────────────────────── The air ───────────────────────────

        [Test]
        public void TheCabinHasNoAirWhileThePlantIsOutOrUnpowered()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            BreathableVolume cabin = Cabin(mount);

            Assert.IsFalse(cabin.HasAir, "an unpowered plant fills the cabin with air.");

            Power(generator);
            Assert.IsTrue(cabin.HasAir, "a powered plant in its mount gives the cabin no air.");

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse(cabin.HasAir, "the cabin kept its air with the plant thrown out.");
            Assert.IsFalse(mount.Running, "a plant thrown out of its mount still counts as running.");

            var cave = Track(new GameObject("Cave"));
            cave.AddComponent<BoxCollider>().isTrigger = true;
            Assert.IsTrue(cave.AddComponent<BreathableVolume>().HasAir, "air with no supply behind it ran out.");
        }

        [Test]
        public void NoBottleCanBeFilledWhileThePlantIsOut()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            var dockGo = new GameObject("TankDock");
            dockGo.transform.SetParent(generator.transform, false);
            var volume = dockGo.AddComponent<BoxCollider>();
            volume.isTrigger = true;

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            typeof(OxygenPlantMount).GetMethod("Present", Hidden).Invoke(mount, null);

            Assert.IsFalse(volume.enabled, "the plant is out of the ship and its tank collar can still be aimed at.");

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            typeof(OxygenPlantMount).GetMethod("Present", Hidden).Invoke(mount, null);
            Assert.IsTrue(volume.enabled, "the plant came back and its tank collar stayed dead.");
        }

        // ─────────────────────────── The objective ───────────────────────────

        [Test]
        public void TheObjectiveIsMetOnlyWithThePlantHomeAndRunning()
        {
            OxygenPlantMount mount = Mount(out OxygenGenerator generator);
            var step = ScriptableObject.CreateInstance<RecoverOxygenPlantStep>();
            spawned.Add(step);
            var world = new ObjectiveWorld();

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse(step.IsMet(world), "the objective was met with the plant lying outside.");

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse(step.IsMet(world), "the objective was met by a plant with no power.");
            StringAssert.Contains("power cell", step.Status(world), "an unpowered plant does not ask for a cell.");

            Power(generator);
            Assert.IsTrue(step.IsMet(world), "the plant is home and running, and the objective is not met.");
        }

        [Test]
        public void TheOxygenStepIsSecondAndNoOldStepLostItsId()
        {
            var chain = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(ChainPath);
            Assert.IsNotNull(chain, $"No chain at {ChainPath}.");

            IReadOnlyList<ObjectiveStep> steps = chain.Steps;
            Assert.AreEqual("learn-controls", steps[0].Id, "the controls are no longer the first step.");
            Assert.IsInstanceOf<RecoverOxygenPlantStep>(steps[1], "the oxygen plant is not the second step.");
            var shipped = new SerializedObject(steps[1]);
            StringAssert.Contains("Lift it", shipped.FindProperty("outsideStatus").stringValue, "the shipped step still says to drag the plant.");
            StringAssert.Contains("solder", shipped.FindProperty("damagedStatus").stringValue, "the shipped step never asks for the torch.");

            var ids = new HashSet<string>();
            foreach (ObjectiveStep s in steps) Assert.IsTrue(ids.Add(s.Id), $"two steps share the id '{s.Id}'.");

            // A save names its step by id, so these must all still resolve after the insert.
            foreach (string old in new[] { "learn-controls", "assess-damage", "first-module", "first-artifact", "repair-ship" })
                Assert.IsTrue(ids.Contains(old), $"a save at '{old}' would restart the chain: the step is gone.");
        }

        // ─────────────────────────── Old saves ───────────────────────────

        [Test]
        public void AWorldSavedBeforeThisFeatureHasItsPlantInPlace()
        {
            OxygenPlantMount mount = Mount(out _);
            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);

            var saver = mount.gameObject.AddComponent<OxygenPlantMountSaveable>();
            saver.RestoreState(null);

            Assert.IsFalse(mount.Detached, "an old save, with no record for the mount, left the plant out of the ship.");
            Assert.IsFalse(mount.HasBeenEjected, "an old save got a crash it never had.");
            Assert.IsNull(saver.CaptureState(), "a plant that was never thrown out writes a record.");

            mount.Restore(true, Vector3.one, Vector3.forward * 40f);
            object captured = saver.CaptureState();
            Assert.IsNotNull(captured, "a plant out of its mount was not saved.");

            var reloaded = Mount(out _);
            reloaded.gameObject.AddComponent<OxygenPlantMountSaveable>()
                    .RestoreState(Newtonsoft.Json.Linq.JObject.FromObject(captured, SaveSerializer.Serializer));
            Assert.IsTrue(reloaded.Detached, "a plant saved out of its mount came back in it.");
        }

        // ─────────────────────────── Fixture ───────────────────────────

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        /// <summary>A hull root with a rack, and the oxygen fixture under it carrying the mount.</summary>
        private OxygenPlantMount Mount(out OxygenGenerator generator)
        {
            GameObject hull = Track(new GameObject("TestHull"));
            var rack = hull.AddComponent<ShipPartRack>();

            // OnEnable does not run in EditMode, and DestroyImmediate never runs OnDisable, so the
            // registry can hold racks destroyed by earlier fixtures. This hull is put first, which
            // is the one ObjectiveWorld.Ship answers with, and taken out again in CleanUp.
            List<ShipPartRack> racks = ActiveRacks();
            racks.RemoveAll(r => r == null);
            racks.Insert(0, rack);

            var fixture = new GameObject("OxygenGenerator");
            fixture.transform.SetParent(hull.transform, false);
            fixture.transform.localPosition = new Vector3(-2f, 3f, 0.4f);
            generator = fixture.AddComponent<OxygenGenerator>();
            fixture.AddComponent<BoxCollider>();
            var mount = fixture.AddComponent<OxygenPlantMount>();

            var loose = Track(new GameObject("LoosePlantPrefab")).AddComponent<Liftable>();
            var so = new SerializedObject(mount);
            so.FindProperty("loosePlantPrefab").objectReferenceValue = loose.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            return mount;
        }

        /// <summary>Three seams on the fixture, a metre apart, and the mount's reference to them.</summary>
        private TorchRepairable Cracks(OxygenPlantMount mount, out Transform[] seams)
        {
            var cracks = mount.gameObject.AddComponent<TorchRepairable>();
            seams = new Transform[3];
            for (int i = 0; i < seams.Length; i++)
            {
                seams[i] = new GameObject("Seam_" + i).transform;
                seams[i].SetParent(mount.transform, false);
                seams[i].localPosition = new Vector3(0f, i, 0.5f);
            }

            var so = new SerializedObject(cracks);
            SerializedProperty list = so.FindProperty("seams");
            list.arraySize = seams.Length;
            for (int i = 0; i < seams.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = seams[i];
            so.FindProperty("secondsPerSeam").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var mountSo = new SerializedObject(mount);
            mountSo.FindProperty("damage").objectReferenceValue = cracks;
            mountSo.ApplyModifiedPropertiesWithoutUndo();
            return cracks;
        }

        private static List<Liftable> ActiveLoads() =>
            (List<Liftable>)typeof(Liftable).GetField("active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        /// <summary>The deciding machine's own record, written as a granted lift would write it.</summary>
        private static void SetCarried(Liftable plant, bool held)
        {
            FieldInfo field = typeof(Liftable).GetField("decided", Hidden);
            var state = (Liftable.LiftState)field.GetValue(plant);
            state.Held = held;
            field.SetValue(plant, state);
        }

        private static List<ShipPartRack> ActiveRacks() =>
            (List<ShipPartRack>)typeof(ShipPartRack)
                .GetField("active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private Liftable LoosePlant(OxygenPlantMount mount) =>
            Track(new GameObject("LoosePlant")).AddComponent<Liftable>();

        private BreathableVolume Cabin(OxygenPlantMount mount)
        {
            GameObject go = Track(new GameObject("BreathableAir"));
            go.AddComponent<BoxCollider>().isTrigger = true;
            var volume = go.AddComponent<BreathableVolume>();
            var so = new SerializedObject(volume);
            so.FindProperty("airSupply").objectReferenceValue = mount;
            so.ApplyModifiedPropertiesWithoutUndo();
            return volume;
        }

        /// <summary>A charged cell in the slot, written straight into the plant's mirror.</summary>
        private static void Power(OxygenGenerator generator)
        {
            FieldInfo field = typeof(OxygenGenerator).GetField("plant", Hidden);
            object plant = field.GetValue(generator);
            plant.GetType().GetField("Battery").SetValue(plant, 1f);
            field.SetValue(generator, plant);
        }
    }
}
