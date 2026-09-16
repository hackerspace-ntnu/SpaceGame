// The shipped Sand Tribe roster: valid, wired both ways, and the only source of the nomads' guns.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class RosterAssetTests
    {
        private const string SandFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset";

        // Nomad.prefab is excluded: its recipe (NomadPrefabBuilder.Nomad) sets RandomWeapon = false
        // -- he carries the walking staff instead -- so he was never built with an NpcRandomLoadout
        // and has nothing for this test to check.
        private static readonly string[] NomadPrefabs =
        {
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Maroon.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_StrawHat.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Tan.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Umber.prefab",
        };

        private static FactionRoster Sand => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SandRosterPath);

        [Test]
        public void SandRoster_Exists_AndValidates()
        {
            Assert.IsNotNull(Sand, $"No roster at {RosterAuthoring.SandRosterPath}. Run Tools/SpaceGame/Agents/Author Sand Tribe Roster.");
            var problems = RosterValidation.Problems(Sand);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void SandFaction_PointsAtItsRoster()
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(SandFactionPath);
            Assert.AreSame(Sand, faction.roster);
            Assert.AreSame(faction, Sand.faction);
        }

        [Test]
        public void SandRoster_HasTheSpecTiers()
        {
            Assert.AreEqual(3, Sand.warPartyTiers.Length);
            Assert.AreEqual(2, Sand.MaxTier);

            AssertTier(0, (RosterRole.Scout, 2));
            AssertTier(1, (RosterRole.Warrior, 3), (RosterRole.Scout, 1));
            AssertTier(2, (RosterRole.Warrior, 3), (RosterRole.Rider, 2));
        }

        private static void AssertTier(int index, params (RosterRole role, int count)[] expected)
        {
            RoleCount[] roles = Sand.warPartyTiers[index].roles;
            CollectionAssert.AreEqual(expected, roles.Select(r => (r.role, r.count)).ToArray(), $"tier {index}");
        }

        [Test]
        public void SandRoster_HandItems_AreTheBakedCandidatesOnEveryNomad()
        {
            foreach (string path in NomadPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);

                var loadout = prefab.GetComponent<NpcRandomLoadout>();
                Assert.IsNotNull(loadout, $"{path} has no NpcRandomLoadout");

                SerializedProperty candidates = new SerializedObject(loadout).FindProperty("candidates");
                var baked = Enumerable.Range(0, candidates.arraySize)
                    .Select(i => candidates.GetArrayElementAtIndex(i).objectReferenceValue as InventoryItem)
                    .ToArray();

                CollectionAssert.AreEqual(Sand.handItems, baked,
                    $"{path}: re-run Tools/SpaceGame/Agents/Build Sand Nomad NPCs after changing the roster.");
            }
        }

        [Test]
        public void SandRoster_HostileLines_AreAuthored()
        {
            Assert.IsNotNull(Sand.hostileLines);
            Assert.IsNotEmpty(Sand.hostileLines.lines);
        }
    }
}
