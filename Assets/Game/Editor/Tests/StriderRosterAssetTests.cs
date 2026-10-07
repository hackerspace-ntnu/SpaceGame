using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class StriderRosterAssetTests
    {
        private static FactionRoster Roster => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.StriderRosterPath);

        [Test]
        public void Roster_Exists_AndValidates()
        {
            Assert.IsNotNull(Roster, "run Tools/SpaceGame/Agents/Author Strider Roster");
            var problems = RosterValidation.Problems(Roster);
            Assert.IsEmpty(problems, string.Join("\n", problems));
            Assert.AreSame(Roster, Roster.faction.roster, "faction ↔ roster back-reference");
        }

        [Test]
        public void Roster_HasTheSpecTiers()
        {
            AssertTier(0, (RosterRole.Rider, 2));
            AssertTier(1, (RosterRole.Rider, 3));
            AssertTier(2, (RosterRole.Rider, 5));
        }

        [Test]
        public void HandItems_AreTheThree_AndBakedOnEveryStriderNomad()
        {
            CollectionAssert.AreEquivalent(new[] { "basicgun", "GravelBlaster", "NetGun" }, Roster.handItems.Select(i => i.name));
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                var candidates = new SerializedObject(prefab.GetComponent<NpcRandomLoadout>()).FindProperty("candidates");
                var baked = Enumerable.Range(0, candidates.arraySize).Select(i => candidates.GetArrayElementAtIndex(i).objectReferenceValue as InventoryItem);
                CollectionAssert.AreEquivalent(Roster.handItems, baked, $"{prefab.name}: rebuild after authoring the roster");
            }
        }

        [Test]
        public void Riders_AreTheFiveMonowheels_WeightedAlike()
        {
            RosterMember[] riders = Roster.members.Where(m => m.role == RosterRole.Rider).ToArray();
            CollectionAssert.AreEquivalent(
                StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles).Select(StriderMonowheelBuilder.PrefabPath),
                riders.Select(m => AssetDatabase.GetAssetPath(m.prefab)));

            // A war party is dealt its Riders from a deck (FactionRoster.Deal): alike weights put no
            // wheel at the back of every round, where a small party would never reach it (the user,
            // 2026-10-05: some monowheels were too rare).
            Assert.IsTrue(riders.All(r => Mathf.Approximately(r.weight, riders[0].weight) && r.weight > 0f),
                          "run Author Strider Roster");
        }

        [Test]
        public void TheCrabOutrider_HasLeftTheRoster()
        {
            CollectionAssert.DoesNotContain(Roster.members.Select(m => AssetDatabase.GetAssetPath(m.prefab)),
                                            StriderCrabOutriderBuilder.PrefabPath, "the crabs stay with the city as a fixed prefab");
        }

        [Test]
        public void HostileLines_AreAuthored() => Assert.AreEqual(8, Roster.hostileLines.lines.Length);

        private static void AssertTier(int index, params (RosterRole role, int count)[] expected) =>
            CollectionAssert.AreEqual(expected, Roster.warPartyTiers[index].roles.Select(r => (r.role, r.count)).ToArray(), $"tier {index}");
    }
}
