// A group's vehicle a player takes (NpcWorldSim.ReleaseToPlayer) stops being the group's: out of Live
// and Fighters, out of the column, never folded away under its new driver, and saved by the world
// like any player vehicle -- empty of NPCs, across a reload too (CrewSaveable).
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
    public class GroupMemberTakenByPlayerTests
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
            warParty = new NpcGroupTemplate { id = "taken-war-party", tribe = tribe, runtimeOnly = true, bountyHunters = true };

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
            NpcGroup group = sim.CreateGroup(warParty, "taken-" + junk.Count, Vector3.zero);
            group.Spawned = true;
            return group;
        }

        private GameObject Member(NpcGroup group, GameObject member, bool leads)
        {
            junk.Add(member);
            if (!member.TryGetComponent(out FormationModule formation)) formation = member.AddComponent<FormationModule>();
            formation.SetFormation(group.Id, leads);
            GroupMembership.Stamp(member, group, group.Live.Count, tribe);
            group.Live.Add(member);
            return member;
        }

        private GameObject Wheel(string variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PrefabPath(variant));
            Assume.That(prefab, Is.Not.Null, "run SpaceGame > Build Strider Monowheels");
            GameObject wheel = Object.Instantiate(prefab);

            // What NpcSpawn.Create does at runtime (DisownToExternal refuses outside play mode).
            var so = new SerializedObject(wheel.GetComponent<SaveableEntity>());
            so.FindProperty("scope").enumValueIndex = (int)SaveScope.External;
            so.ApplyModifiedPropertiesWithoutUndo();
            return wheel;
        }

        [Test]
        public void ATakenMember_LeavesLiveFightersAndTheColumn()
        {
            NpcGroup group = Group();
            var fighter = new GameObject("Fighter");
            fighter.AddComponent<HealthComponent>();
            Member(group, fighter, leads: true);
            GameObject other = Member(group, new GameObject("Other"), leads: false);
            Assume.That(group.Fighters, Does.Contain(fighter));

            sim.ReleaseToPlayer(fighter);

            CollectionAssert.DoesNotContain(group.Live, fighter, "a taken member drags the group's centroid after the player");
            CollectionAssert.DoesNotContain(group.Fighters, fighter, "a taken member keeps a beaten party alive");
            Assert.AreEqual(1, group.Live.Count);
            Assert.AreEqual(0, group.FightersSpawned, "nobody left to die for the party is counted as one it can lose");
            Assert.IsNull(fighter.GetComponent<GroupMembership>().Group);
            Assert.IsEmpty(fighter.GetComponent<FormationModule>().FormationId, "it must leave the column");
            Assert.AreSame(other.GetComponent<FormationModule>(), FormationModule.LeaderOf(group.Id),
                "the column closes up behind the next member");
        }

        [Test]
        public void APlayerMountingAMember_OnTheServer_TakesItFromTheGroup()
        {
            NpcGroup group = Group();
            GameObject wheel = Member(group, Wheel("Runner"), leads: true);
            var membership = wheel.GetComponent<GroupMembership>();

            // What MountModule.Mounted delivers once the server has seated a player (offline: this machine decides).
            typeof(GroupMembership).GetMethod("OnPlayerMounted", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(membership, new object[] { null });

            CollectionAssert.DoesNotContain(group.Live, wheel);
            Assert.IsTrue(wheel.GetComponent<SaveableEntity>().BelongsToWorld);
        }

        [Test]
        public void TheFold_NoLongerTakesATakenMemberAway()
        {
            NpcGroup group = Group();
            GameObject member = Member(group, new GameObject("Wheel"), leads: true);

            sim.ReleaseToPlayer(member);
            typeof(NpcWorldSim).GetMethod("DespawnMembers", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            Assert.IsTrue(member != null, "folding the party removed the wheel from under its new driver");
        }

        [Test]
        public void ATakenWheel_IsSavedByTheWorld_WithItsSeatsStoodDown()
        {
            NpcGroup group = Group();
            GameObject wheel = Member(group, Wheel("Double"), leads: false);

            sim.ReleaseToPlayer(wheel);

            var saveable = wheel.GetComponent<SaveableEntity>();
            Assert.IsTrue(saveable.BelongsToWorld, "disowned, the wheel is gone after a reload");
            Assert.IsFalse(string.IsNullOrEmpty(saveable.PrefabId), "without a prefab id the record can never be put back");
            Assert.IsNotNull(wheel.GetComponent<CrewSaveable>());
            Assert.IsTrue(wheel.GetComponent<NpcPassenger>().IsStoodDown, "a load would seat a stranger in the saddle");
            Assert.IsTrue(wheel.GetComponent<MountedGunners>().IsStoodDown, "a load would seat strangers at the guns");
            Assert.IsFalse(wheel.GetComponent<SpaceGame.World.SceneTracked>().KeepChunksLoaded);
        }

        [Test]
        public void ATakenWheelsRecord_StandsTheReloadedWheelsSeatsDown()
        {
            NpcGroup group = Group();
            GameObject taken = Member(group, Wheel("Double"), leads: false);
            sim.ReleaseToPlayer(taken);
            object captured = taken.GetComponent<CrewSaveable>().CaptureState();
            Assume.That(captured, Is.Not.Null);

            GameObject reloaded = Wheel("Double");
            junk.Add(reloaded);
            var saver = reloaded.AddComponent<CrewSaveable>();
            saver.RestoreState(JObject.FromObject(captured, SaveSerializer.Serializer));

            Assert.IsTrue(reloaded.GetComponent<NpcPassenger>().IsStoodDown);
            Assert.IsTrue(reloaded.GetComponent<MountedGunners>().IsStoodDown);
        }

        [Test]
        public void ACrewedWheel_SavesNothingUnderTheCrewKey()
        {
            GameObject wheel = Wheel("Runner");
            junk.Add(wheel);
            Assert.IsNull(wheel.AddComponent<CrewSaveable>().CaptureState(), "a crewed wheel is at its prefab's default");
        }
    }
}
