// The faction assets, read off disk.
//
// Every assertion here reads the ASSET, never the class. A serialized field keeps whatever was last
// saved into it (INVARIANTS.md), so a default changed in C# proves nothing about what ships — and
// the failure this file exists for was exactly that shape: PlayerFaction, NPCFaction and
// RobotFaction all carried `factionName: Robots` on disk for months, so three different sides of
// every fight printed the same word in a dev overlay and no bug involving them could be read.
//
// The GUID assertions matter as much as the names. A faction's ID is its asset GUID, it is what
// prefabs reference and what saves store, and the four assets were RENAMED rather than recreated
// for precisely that reason. Recreating one silently unsides every prefab that pointed at it and
// every creature in every existing save.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class FactionAssetTests
    {
        private const string CoreDir = "Assets/Game/ScriptableObjects/Factions/Core";

        /// <summary>
        /// The GUIDs the four renamed assets must keep, recorded here so a rename that loses one
        /// fails in CI rather than in somebody's save file. These are not arbitrary: they are what
        /// is already written into shipped prefabs and saved worlds.
        /// </summary>
        private static readonly (string Asset, string DisplayName, string Guid)[] Core =
        {
            ("HumansFaction",    "Humans",     "e60e1b5fdd5ac4c0187e288640201cf6"),
            ("SandTribeFaction", "Sand Tribe", "f098756d43d674f47a68dfd8dae0dd1d"),
            ("ClankerFaction",   "Clankers",   "ad0ce5393f2404ae19642fa6323625ee"),
            ("OutlawFaction",    "Outlaws",    "4815f6a4ad2c743ad900584b2c1e0c4b"),
            ("FaunaFaction",     "Fauna",      "acfac6dcc51034e60915d44e81aa5710"),
            ("WildlifeFaction",  "Wildlife",   "2ee6e7c9c668f4169a98d2307a4d4b0b"),
        };

        private static FactionDefinition Load(string asset) =>
            AssetDatabase.LoadAssetAtPath<FactionDefinition>($"{CoreDir}/{asset}.asset");

        private static IEnumerable<FactionDefinition> All() =>
            AssetDatabase.FindAssets("t:FactionDefinition", new[] { CoreDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<FactionDefinition>)
                .Where(f => f != null);

        [Test]
        public void EveryCoreFactionExistsUnderItsOwnName()
        {
            foreach ((string asset, string display, string guid) in Core)
            {
                FactionDefinition faction = Load(asset);
                Assert.IsNotNull(faction, $"{CoreDir}/{asset}.asset is missing");
                Assert.AreEqual(display, faction.factionName,
                                $"{asset} is the designer-facing name of this side of a fight");
                Assert.AreEqual(guid, faction.ID,
                                $"{asset} must keep its GUID — prefabs and saves name it by that, " +
                                "so a recreated asset unsides everything that referenced it");
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID($"{CoreDir}/{asset}.asset"),
                                $"{asset}'s stamped ID and its actual asset GUID have diverged");
            }
        }

        [Test]
        public void NoTwoFactionsShareADisplayName()
        {
            List<FactionDefinition> factions = All().ToList();
            Assert.Greater(factions.Count, 0, "no faction assets found — has the folder moved?");

            IEnumerable<string> duplicates = factions
                .GroupBy(f => f.factionName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            CollectionAssert.IsEmpty(duplicates.ToList(),
                "Two sides of a fight printing the same word makes every faction bug in play " +
                "unreadable. Humans, Sand Tribe and Clankers all said \"Robots\" until 2026-09-15.");
        }

        [Test]
        public void NoFactionIsStillCalledRobots()
        {
            foreach (FactionDefinition faction in All())
            {
                Assert.AreNotEqual("Robots", faction.factionName,
                    $"{faction.name} kept the placeholder name three assets used to share");
                Assert.AreNotEqual("Unnamed Faction", faction.factionName,
                    $"{faction.name} was never given a display name");
            }
        }

        [Test]
        public void EveryFactionHasAnIdAndItIsItsAssetGuid()
        {
            foreach (FactionDefinition faction in All())
            {
                string path = AssetDatabase.GetAssetPath(faction);
                Assert.IsFalse(string.IsNullOrEmpty(faction.ID),
                    $"{path} has no ID. OnValidate stamps it, and a build ships whatever the " +
                    "asset last saved — an idless faction cannot be restored from a save.");
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), faction.ID, path);
            }
        }

        [Test]
        public void TheOutlawsAreHostileToTheCrew()
        {
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
                $"{CoreDir}/GlobalRelationships.asset");
            Assert.IsNotNull(table);

            Assert.AreEqual(FactionRelationship.Hostile,
                            table.Get(Load("OutlawFaction"), Load("HumansFaction")),
                            "Outlaws are the crew's mirror: the same body, the same guns, the " +
                            "wrong side (design §3.7)");
        }

        /// <summary>
        /// Fauna is the project's "peaceful" faction: nothing acquires it and it acquires nothing.
        ///
        /// Deliberately "no HOSTILE row" rather than "no row at all" — design §3.2 gives the
        /// Clankers an explicit `Neutral` row toward Fauna, because a row beats their `Hostile`
        /// default and that is how "Clankers shoot people, not animals" is expressed. A Neutral row
        /// says exactly what the absence of a row says; a Hostile one turns every animal in the
        /// world into an enemy, which is the mistake the agent skill warns about.
        /// </summary>
        [Test]
        public void NothingIsHostileToTheAnimals()
        {
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
                $"{CoreDir}/GlobalRelationships.asset");
            FactionDefinition fauna = Load("FaunaFaction");

            var so = new SerializedObject(table);
            SerializedProperty rows = so.FindProperty("relationships");

            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                bool touchesFauna =
                    row.FindPropertyRelative("factionA").objectReferenceValue == fauna ||
                    row.FindPropertyRelative("factionB").objectReferenceValue == fauna;

                if (!touchesFauna) continue;

                Assert.AreNotEqual((int)FactionRelationship.Hostile,
                                   row.FindPropertyRelative("relationship").enumValueIndex,
                                   $"row {i} makes something hostile to Fauna; peaceful animals " +
                                   "are peaceful because nothing is");
            }
        }
    }
}
