// The Sky Tribe faction asset, read off disk — sky-tribe-and-vessels plan Task 1.
//
// FactionAssetTests already covers the four renamed core factions and the Fauna/Wildlife rules;
// this file is the Sky-specific half: the shipped Sky tribe faction asset, and the
// relationship rows it mirrors from Sand. Reads the asset, never the class default (INVARIANTS.md;
// FactionAssetTests' own header explains why that distinction has already bitten this project once).
//
// The ledger's `tribes` list is deliberately NOT asserted here: it lives in persistentScene.unity,
// and the test note for this task is explicit that grepping scene YAML from a test is not the way
// to check it — that is a scene read-back done as part of running the authoring menu, not an
// EditMode assertion.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SkyTribeAssetTests
    {
        private const string CoreDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string SkyPath = CoreDir + "/SkyTribeFaction.asset";
        private const string SandPath = CoreDir + "/SandTribeFaction.asset";
        private const string RelationshipsPath = CoreDir + "/GlobalRelationships.asset";

        private static FactionDefinition Sky() => AssetDatabase.LoadAssetAtPath<FactionDefinition>(SkyPath);
        private static FactionDefinition Sand() => AssetDatabase.LoadAssetAtPath<FactionDefinition>(SandPath);

        private static FactionRelationshipTable Relationships() =>
            AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);

        [Test]
        public void SkyTribeFactionExistsWithNeutralDefaultAndAnId()
        {
            FactionDefinition sky = Sky();
            Assert.IsNotNull(sky, $"{SkyPath} is missing — run Tools/SpaceGame/Agents/Author Sky Tribe Faction");

            Assert.AreEqual("Sky Tribe", sky.factionName);
            Assert.AreEqual(FactionRelationship.Neutral, sky.defaultStance,
                "a tribe must default Neutral or FactionGoodwillLedger.Awake refuses to track it " +
                "(spacegame-tribe §1)");

            Assert.IsFalse(string.IsNullOrEmpty(sky.ID), $"{SkyPath} has no ID stamped");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(SkyPath), sky.ID,
                "ID must be the asset's own GUID — it is what saves and prefabs will name Sky by");
        }

        /// <summary>
        /// Every row Sand has in GlobalRelationships gets a matching Sky row toward the same other
        /// faction with the same stance (plan Task 1: "Sky's relationships mirror Sand's"). Currently
        /// vacuous — Sand has no non-default rows of its own — but it is written against the general
        /// rule so it starts failing the moment a Sand row is added without re-running the menu.
        /// </summary>
        [Test]
        public void EverySandRowHasAMatchingSkyRow()
        {
            FactionDefinition sand = Sand();
            FactionDefinition sky = Sky();
            Assert.IsNotNull(sand);
            Assert.IsNotNull(sky);

            FactionRelationshipTable table = Relationships();
            Assert.IsNotNull(table);

            var so = new SerializedObject(table);
            SerializedProperty rows = so.FindProperty("relationships");

            var sandRows = new List<(FactionDefinition Other, int Stance)>();
            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                var a = row.FindPropertyRelative("factionA").objectReferenceValue as FactionDefinition;
                var b = row.FindPropertyRelative("factionB").objectReferenceValue as FactionDefinition;
                int stance = row.FindPropertyRelative("relationship").enumValueIndex;

                FactionDefinition other = a == sand ? b : b == sand ? a : null;
                if (other == null || other == sky) continue;

                sandRows.Add((other, stance));
            }

            foreach ((FactionDefinition other, int stance) in sandRows)
            {
                bool matched = false;
                for (int i = 0; i < rows.arraySize && !matched; i++)
                {
                    SerializedProperty row = rows.GetArrayElementAtIndex(i);
                    var a = row.FindPropertyRelative("factionA").objectReferenceValue;
                    var b = row.FindPropertyRelative("factionB").objectReferenceValue;
                    bool touchesSkyAndOther = (Equals(a, sky) && Equals(b, other)) ||
                                               (Equals(a, other) && Equals(b, sky));

                    matched = touchesSkyAndOther &&
                              row.FindPropertyRelative("relationship").enumValueIndex == stance;
                }

                Assert.IsTrue(matched,
                    $"Sand has a {(FactionRelationship)stance} row toward {other.factionName} with no " +
                    "matching Sky row — Sky's relationships must mirror Sand's (plan Task 1)");
            }
        }

        [Test]
        public void ThereIsNoRowBetweenSandAndSky()
        {
            FactionDefinition sand = Sand();
            FactionDefinition sky = Sky();
            Assert.IsNotNull(sand);
            Assert.IsNotNull(sky);

            FactionRelationshipTable table = Relationships();
            Assert.IsNotNull(table);

            var so = new SerializedObject(table);
            SerializedProperty rows = so.FindProperty("relationships");

            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                var a = row.FindPropertyRelative("factionA").objectReferenceValue;
                var b = row.FindPropertyRelative("factionB").objectReferenceValue;
                bool touchesBoth = (Equals(a, sand) && Equals(b, sky)) || (Equals(a, sky) && Equals(b, sand));

                Assert.IsFalse(touchesBoth,
                    $"row {i} pairs Sand and Sky directly — they are Neutral to each other, which " +
                    "the design expresses as no row at all, same as any two ordinary tribes");
            }
        }

        [Test]
        public void SkyDoesNotShareADisplayNameWithAnyOtherFaction()
        {
            FactionDefinition sky = Sky();
            Assert.IsNotNull(sky);

            IEnumerable<FactionDefinition> others = AssetDatabase.FindAssets("t:FactionDefinition", new[] { CoreDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<FactionDefinition>)
                .Where(f => f != null && f != sky);

            Assert.IsFalse(others.Any(f => f.factionName == sky.factionName),
                "two sides of a fight printing the same word makes every faction bug involving them unreadable");
        }
    }
}
