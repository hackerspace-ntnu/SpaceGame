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
