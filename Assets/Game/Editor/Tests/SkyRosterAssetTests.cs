// The shipped Sky Tribe roster and its four people, read off disk -- sky-tribe-and-vessels plan Task 2.
//
// The Sky people are the sand nomads' FBX run through the same NomadPrefabBuilder recipe with a
// different faction, roster and cloth palette. That makes the builder the one place a Sky change can
// leak into Sand, so the last two tests here pin the Sand side as well: its recipes still name the Sand
// faction and roster, and its prefabs still serialize them.
//
// Reads the assets, never the class defaults (INVARIANTS.md).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class SkyRosterAssetTests
    {
        private static FactionRoster Sky => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SkyRosterPath);
        private static FactionRoster Sand => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SandRosterPath);

        private static GameObject Load(NomadPrefabBuilder.NomadRecipe recipe)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
            Assert.IsNotNull(prefab, $"{recipe.PrefabPath} has not been built " +
                                     "(Tools/SpaceGame/Agents/Build Sky Nomad NPCs).");
            return prefab;
        }

        private static Object BakedFaction(GameObject prefab) =>
            new SerializedObject(prefab.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;

        private static InventoryItem[] BakedCandidates(GameObject prefab)
        {
            var loadout = prefab.GetComponent<NpcRandomLoadout>();
            Assert.IsNotNull(loadout, $"{prefab.name} has no NpcRandomLoadout");

            SerializedProperty candidates = new SerializedObject(loadout).FindProperty("candidates");
            return Enumerable.Range(0, candidates.arraySize)
                .Select(i => candidates.GetArrayElementAtIndex(i).objectReferenceValue as InventoryItem)
                .ToArray();
        }

        private static int SaverCount(GameObject prefab) => prefab.GetComponents<ISaveable>().Length;

        [Test]
        public void SkyRoster_Exists_AndValidates()
        {
            Assert.IsNotNull(Sky, $"No roster at {RosterAuthoring.SkyRosterPath}. Run Tools/SpaceGame/Agents/Author Sky Tribe Roster.");
            var problems = RosterValidation.Problems(Sky);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void SkyFaction_PointsAtItsRoster()
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);
            Assert.AreSame(Sky, faction.roster);
            Assert.AreSame(faction, Sky.faction);
        }

        [Test]
        public void SkyRoster_HasThePlanTiers_AndNoRiders()
        {
            Assert.AreEqual(3, Sky.warPartyTiers.Length);

            AssertTier(0, (RosterRole.Scout, 2));
            AssertTier(1, (RosterRole.Warrior, 3), (RosterRole.Scout, 1));
            AssertTier(2, (RosterRole.Warrior, 5), (RosterRole.Scout, 2));

            Assert.IsFalse(Sky.members.Any(m => m.role == RosterRole.Rider),
                "Sky parties fly in vessels; a Rider member would put a mount on a skiff");
        }

        private static void AssertTier(int index, params (RosterRole role, int count)[] expected)
        {
            RoleCount[] roles = Sky.warPartyTiers[index].roles;
            CollectionAssert.AreEqual(expected, roles.Select(r => (r.role, r.count)).ToArray(), $"tier {index}");
        }

        [Test]
        public void SkyRoster_FieldsEverySkyPerson_AsScoutsAndWarriors()
        {
            var people = NomadPrefabBuilder.SkyTribePeople.Select(Load).ToArray();
            foreach (RosterRole role in new[] { RosterRole.Scout, RosterRole.Warrior })
                CollectionAssert.AreEquivalent(people,
                    Sky.members.Where(m => m.role == role).Select(m => m.prefab).ToArray(), role.ToString());
        }

        [Test]
        public void SkyRoster_HandItems_AreSandsSeven_AndTheBakedCandidatesOnEverySkyNomad()
        {
            CollectionAssert.AreEqual(Sand.handItems, Sky.handItems);

            foreach (var recipe in NomadPrefabBuilder.SkyTribePeople)
                CollectionAssert.AreEqual(Sky.handItems, BakedCandidates(Load(recipe)),
                    $"{recipe.PrefabPath}: rebuild it (Build Sky Nomad NPCs / Build Sky Soldier NPC) after changing the roster.");
        }

        [Test]
        public void SkyRoster_HostileLines_AreAuthored()
        {
            Assert.IsNotNull(Sky.hostileLines);
            Assert.AreEqual(4, Sky.hostileLines.lines.Length);
        }

        [Test]
        public void EverySkyNomad_IsASkyTribeAgent_ThatSeesSavesAndRagdolls()
        {
            var skyFaction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);

            for (int i = 0; i < NomadPrefabBuilder.SkyNomads.Length; i++)
            {
                GameObject sky = Load(NomadPrefabBuilder.SkyNomads[i]);
                GameObject sand = AssetDatabase.LoadAssetAtPath<GameObject>(NomadPrefabBuilder.SandNomads[i].PrefabPath);
                Assert.IsNotNull(sand, NomadPrefabBuilder.SandNomads[i].PrefabPath);

                Assert.IsNotNull(sky.GetComponent<AgentController>(), sky.name);
                Assert.IsNotNull(sky.GetComponent<EntityFaction>(), sky.name);
                Assert.IsNotNull(sky.GetComponent<Unity.Netcode.NetworkObject>(), sky.name);
                Assert.AreSame(skyFaction, BakedFaction(sky), $"{sky.name} serializes the wrong faction");

                var shortfalls = new List<string>();
                Assert.IsFalse(VisionBaselineWiring.Raise(sky, shortfalls, apply: false), string.Join("\n", shortfalls));

                Assert.GreaterOrEqual(SaverCount(sky), SaverCount(sand),
                    $"{sky.name} has fewer savers than {sand.name}: the build stopped before SaveableWiring");
                Assert.IsNotNull(sky.GetComponent<RagdollRig>(),
                    $"{sky.name} has no RagdollRig: the build stopped before RagdollWiring");

                SerializedProperty pool = new SerializedObject(sky.GetComponent<SpaceGame.Gameplay.DialogInteraction>())
                    .FindProperty("predefinedRandomPool");
                CollectionAssert.AreEqual(NomadPrefabBuilder.SkyNomads[i].DialogLines,
                    Enumerable.Range(0, pool.arraySize).Select(j => pool.GetArrayElementAtIndex(j).stringValue).ToArray(),
                    $"{sky.name} speaks someone else's lines");
            }
        }

        [Test]
        public void TheSkySoldier_IsASkyTribeAgent_ThatSeesSavesAndRagdolls()
        {
            var skyFaction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);
            GameObject soldier = Load(NomadPrefabBuilder.SkySoldier);
            GameObject nomad = Load(NomadPrefabBuilder.SkyNomads[0]);

            Assert.IsNotNull(soldier.GetComponent<AgentController>());
            Assert.IsNotNull(soldier.GetComponent<Unity.Netcode.NetworkObject>());
            Assert.AreSame(skyFaction, BakedFaction(soldier), "the sky soldier serializes the wrong faction");

            var shortfalls = new List<string>();
            Assert.IsFalse(VisionBaselineWiring.Raise(soldier, shortfalls, apply: false), string.Join("\n", shortfalls));
            Assert.GreaterOrEqual(SaverCount(soldier), SaverCount(nomad),
                "the sky soldier has fewer savers than a sky nomad: the build stopped before SaveableWiring");
            Assert.IsNotNull(soldier.GetComponent<RagdollRig>(), "no RagdollRig: the build stopped before RagdollWiring");

            var animator = soldier.GetComponentInChildren<Animator>();
            Assert.IsTrue(animator != null && animator.avatar != null && animator.avatar.isHuman,
                "the sky soldier has no humanoid avatar and would stand in bind pose");
        }

        [Test]
        public void SandRecipes_StillNameTheSandFactionAndRoster()
        {
            foreach (var recipe in NomadPrefabBuilder.SandNomads.Append(NomadPrefabBuilder.Nomad))
            {
                Assert.AreEqual(RosterAuthoring.SandFactionPath, recipe.FactionPath, recipe.Name);
                Assert.AreEqual(RosterAuthoring.SandRosterPath, recipe.RosterPath, recipe.Name);
            }
        }

        [Test]
        public void SandNomads_StillSerializeTheSandFactionAndRoster()
        {
            var sandFaction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SandFactionPath);

            foreach (var recipe in NomadPrefabBuilder.SandNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                Assert.IsNotNull(prefab, recipe.PrefabPath);
                Assert.AreSame(sandFaction, BakedFaction(prefab), recipe.PrefabPath);
                CollectionAssert.AreEqual(Sand.handItems, BakedCandidates(prefab), recipe.PrefabPath);
            }
        }
    }
}
