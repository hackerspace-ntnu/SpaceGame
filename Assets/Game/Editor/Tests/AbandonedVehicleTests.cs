// A group's vehicle the group loses -- driver gone or wrecked -- stays in the world for its lifetime
// (AbandonedVehicle) instead of vanishing with the group: released from the group the way a
// player-taken one is, saved by the world store with its remaining countdown, and kept for good
// once a player mounts it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class AbandonedVehicleTests
    {
        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroupTemplate warParty;
        private FactionDefinition tribe;

        [SetUp]
        public void SetUp()
        {
            tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);
            warParty = new NpcGroupTemplate { id = "abandoned-war-party", tribe = tribe, runtimeOnly = true, bountyHunters = true };

            var go = new GameObject("Sim");
            junk.Add(go);
            sim = go.AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(sim, new[] { warParty });
            typeof(NpcWorldSim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sim, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private NpcGroup Group()
        {
            NpcGroup group = sim.CreateGroup(warParty, "abandoned-" + junk.Count, Vector3.zero);
            group.Spawned = true;
            return group;
        }

        /// <summary>A Strider wheel spawned into <paramref name="group"/> the way NpcSpawn.Create does it.</summary>
        private GameObject GroupWheel(NpcGroup group, string variant)
        {
            GameObject wheel = Wheel(variant);

            // NpcSpawn.Create disowns it to the group (DisownToExternal refuses outside play mode).
            var so = new SerializedObject(wheel.GetComponent<SaveableEntity>());
            so.FindProperty("scope").enumValueIndex = (int)SaveScope.External;
            so.ApplyModifiedPropertiesWithoutUndo();

            wheel.GetComponent<FormationModule>().SetFormation(group.Id, group.Live.Count == 0);
            GroupMembership.Stamp(wheel, group, group.Live.Count, tribe);
            group.Live.Add(wheel);
            return wheel;
        }

        private GameObject Wheel(string variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PrefabPath(variant));
            Assume.That(prefab, Is.Not.Null, "run SpaceGame > Build Strider Monowheels");
            GameObject wheel = Object.Instantiate(prefab);
            junk.Add(wheel);

            // Edit mode runs no Awake: resolve the vehicle's parts as play would.
            typeof(AbandonedVehicle).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(wheel.GetComponent<AbandonedVehicle>(), null);
            return wheel;
        }

        private static void Kill(GameObject wheel)
        {
            var health = wheel.GetComponent<HealthComponent>();
            health.Damage(health.GetMaxHealth);
            Assume.That(health.Alive, Is.False);
        }

        [Test]
        public void EveryStriderWheel_IsLeftBehind_NotDespawnedAsACorpse()
        {
            foreach (string variant in new[] { "Runner", "Hauler", "Patched", "Double", "DoubleWide" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PrefabPath(variant));
                Assume.That(prefab, Is.Not.Null, "run SpaceGame > Build Strider Monowheels");

                var vehicle = prefab.GetComponent<AbandonedVehicle>();
                Assert.IsNotNull(vehicle, $"{variant}: a defeated wheel vanishes with its war party");
                Assert.GreaterOrEqual(vehicle.Lifetime, 300f, $"{variant}: the user asked for at least five minutes");
                Assert.IsFalse(prefab.GetComponent<HealthReactionModule>().Despawns,
                    $"{variant}: the corpse despawn takes a wreck away after seconds");
            }
        }

        [TestCase(true, false, true, ExpectedResult = true, TestName = "Defeated_DriverKnockedOff")]
        [TestCase(false, false, false, ExpectedResult = true, TestName = "Defeated_Wrecked")]
        [TestCase(true, true, true, ExpectedResult = false, TestName = "NotDefeated_Driven")]
        [TestCase(false, false, true, ExpectedResult = false, TestName = "NotDefeated_DriverNotSeatedYet")]
        public bool IsDefeated(bool hadDriver, bool hasDriver, bool alive) =>
            AbandonedVehicle.IsDefeated(hadDriver, hasDriver, alive);

        [Test]
        public void AWreckedWheel_LeavesItsGroup_AndIsSavedByTheWorld_ForItsLifetime()
        {
            NpcGroup group = Group();
            GameObject wheel = GroupWheel(group, "Double");
            var vehicle = wheel.GetComponent<AbandonedVehicle>();
            Kill(wheel);

            vehicle.Tick(0.1f);

            Assert.AreEqual(AbandonedVehicle.Stage.Abandoned, vehicle.Current);
            Assert.AreEqual(vehicle.Lifetime, vehicle.Remaining, 1e-4f);
            CollectionAssert.DoesNotContain(group.Live, wheel, "the fold despawns every Live member");
            Assert.IsNull(wheel.GetComponent<GroupMembership>().Group);
            Assert.IsTrue(wheel.GetComponent<SaveableEntity>().BelongsToWorld, "disowned, a load loses it");
            Assert.IsNotNull(wheel.GetComponent<AbandonedVehicleSaveable>(), "its countdown is not saved");
            Assert.IsTrue(wheel.GetComponent<NpcPassenger>().IsStoodDown, "a load would seat a stranger in the saddle");
            Assert.IsTrue(wheel.GetComponent<MountedGunners>().IsStoodDown, "a load would seat strangers at the guns");
        }

        [Test]
        public void TheFold_LeavesAnAbandonedWheelWhereItStopped()
        {
            NpcGroup group = Group();
            GameObject wheel = GroupWheel(group, "Runner");
            Kill(wheel);
            wheel.GetComponent<AbandonedVehicle>().Tick(0.1f);

            typeof(NpcWorldSim).GetMethod("DespawnMembers", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            Assert.IsTrue(wheel != null, "the party folding took the wreck with it");
        }

        [Test]
        public void ALivingDrivenWheel_StaysItsGroups()
        {
            NpcGroup group = Group();
            GameObject wheel = GroupWheel(group, "Runner");
            var vehicle = wheel.GetComponent<AbandonedVehicle>();

            vehicle.Tick(0.1f);

            Assert.AreEqual(AbandonedVehicle.Stage.Serving, vehicle.Current);
            CollectionAssert.Contains(group.Live, wheel);
        }

        [Test]
        public void TheCountdown_RunsDown_AndAWheelOutsideAnyGroupIsKept()
        {
            NpcGroup group = Group();
            var vehicle = GroupWheel(group, "Runner").GetComponent<AbandonedVehicle>();
            vehicle.Abandon(10f);

            vehicle.Tick(4f);
            Assert.AreEqual(6f, vehicle.Remaining, 1e-4f);
            Assert.AreEqual(AbandonedVehicle.Stage.Abandoned, vehicle.Current);

            var stray = Wheel("Runner").GetComponent<AbandonedVehicle>();
            stray.Tick(0.1f);
            Assert.AreEqual(AbandonedVehicle.Stage.Kept, stray.Current,
                "a wheel no group spawned -- or one a player took -- is nobody's to take away");
        }

        [Test]
        public void AnAbandonedWheelsRecord_ResumesItsCountdown_AndAKeptOneSavesNothing()
        {
            NpcGroup group = Group();
            GameObject wheel = GroupWheel(group, "Runner");
            Kill(wheel);
            var vehicle = wheel.GetComponent<AbandonedVehicle>();
            vehicle.Tick(0.1f);
            vehicle.Abandon(120f);

            object captured = wheel.GetComponent<AbandonedVehicleSaveable>().CaptureState();
            Assume.That(captured, Is.Not.Null);
            JObject json = JObject.FromObject(captured, SaveSerializer.Serializer);
            Assert.AreEqual(120f, json["remaining"].Value<float>(), 1e-4f);

            GameObject reloaded = Wheel("Runner");
            reloaded.AddComponent<AbandonedVehicleSaveable>().RestoreState(json);
            var restored = reloaded.GetComponent<AbandonedVehicle>();
            Assert.AreEqual(AbandonedVehicle.Stage.Abandoned, restored.Current);
            Assert.AreEqual(120f, restored.Remaining, 1e-4f);

            restored.Tick(1f);
            Assert.AreEqual(AbandonedVehicle.Stage.Abandoned, restored.Current,
                "a restored wheel has no group membership and must not take that as being kept");

            var kept = Wheel("Runner").GetComponent<AbandonedVehicle>();
            kept.Tick(0.1f);
            Assert.IsNull(kept.gameObject.AddComponent<AbandonedVehicleSaveable>().CaptureState());
        }
    }
}
