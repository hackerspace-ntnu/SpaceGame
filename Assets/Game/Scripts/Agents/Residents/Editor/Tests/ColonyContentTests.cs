// The Mars colony's resident content, read off the assets: its lines parse and speak for real archetypes, every archetype's post is
// offered by some prop, a bunk gives each berth a lying seat and a bed spot of its own, and a building's beds are the bed spots it holds.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class ColonyContentTests
    {
        private const string CulturePath = "Assets/Game/ScriptableObjects/Residents/ColonyCulture.asset";
        private const string LinesPath = "Assets/Game/ScriptableObjects/Residents/Lines/ColonyLines.txt";
        private const string ArchetypeFolder = "Assets/Game/ScriptableObjects/Residents/Archetypes/Colony/";
        private const string PropFolder = "Assets/Game/Prefabs/Environment/Decorations/Astronaut";
        private const string BuildingFolder = "Assets/Game/Prefabs/Environment/Structures/AstronautSettlement";
        private const string BunkPath = PropFolder + "/AstroDeco_Bunk.prefab";

        [Test]
        public void TheColonyLinesParseCleanWithKnownTokensAndRealSpeakers()
        {
            LineTable table = LineTable.Parse(File.ReadAllText(LinesPath));

            Assert.IsEmpty(table.Errors, string.Join("\n", table.Errors));
            Assert.Greater(table.Rows.Count, 0);
            foreach (LineRow row in table.Rows)
            {
                Assert.LessOrEqual(SpeechTokens.CountTokens(row.text, out string unknown), LineTable.MaxTokensPerLine, row.key);
                Assert.IsNull(unknown, row.key);
                if (row.speaker != null)
                    Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ResidentArchetype>(ArchetypeFolder + row.speaker + ".asset"),
                                     $"{row.key}: no colony archetype named '{row.speaker}'");
            }
        }

        [Test]
        public void EveryColonyArchetypesPostIsOfferedBySomeProp()
        {
            var culture = AssetDatabase.LoadAssetAtPath<SettlementCulture>(CulturePath);
            Assert.IsNotNull(culture, "no ColonyCulture asset");

            HashSet<SpotUse> offered = AllPropSpots().Select(spot => spot.Use).ToHashSet();
            foreach (ResidentArchetype archetype in culture.archetypes)
            {
                if (archetype.post != null)
                    Assert.IsTrue(offered.Contains(archetype.post), $"{archetype.name}: nothing offers its post '{archetype.post.name}'");
                if (archetype.chore != null)
                {
                    Assert.IsTrue(offered.Contains(archetype.chore.source), $"{archetype.name}: its chore has no source stop");
                    Assert.IsTrue(offered.Contains(archetype.chore.target), $"{archetype.name}: its chore has no target stop");
                }
            }
        }

        [Test]
        public void ABunkGivesEachBerthALyingSeatAndABedSpotOfItsOwn()
        {
            GameObject bunk = AssetDatabase.LoadAssetAtPath<GameObject>(BunkPath);
            Seat[] seats = bunk.GetComponentsInChildren<Seat>(true);
            SettlementSpot[] beds = bunk.GetComponentsInChildren<SettlementSpot>(true).Where(s => s.Use != null && s.Use.sleeps).ToArray();

            Assert.AreEqual(2, seats.Length, "a bunk has two berths");
            Assert.AreEqual(2, beds.Length, "and a bed spot for each");
            Assert.IsTrue(seats.All(seat => seat.Pose == SeatPose.Lie), "a berth is lain on");
            Assert.IsTrue(beds.All(bed => bed.Use.seated), "a bed is a seated use, so its resident claims the berth's seat");
            Assert.IsEmpty(SettlementPlaces.SeatlessSpots(beds.Select(b => b.Position).ToList(), seats, ResidentTuning.Instance.seatReach),
                           "every bed spot has a seat of its own within reach");
        }

        [Test]
        public void ABuildingsBedsAreTheBedSpotsItHolds()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab Colony_", new[] { BuildingFolder }))
            {
                GameObject building = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var dwelling = building.GetComponent<Dwelling>();
                int berths = building.GetComponentsInChildren<SettlementSpot>(true).Count(s => s.Use != null && s.Use.sleeps);

                if (dwelling == null) Assert.AreEqual(0, berths, $"{building.name} has bed spots but no Dwelling to house them");
                else Assert.AreEqual(berths, dwelling.Beds, $"{building.name}: beds and bed spots disagree");
            }
        }

        private static IEnumerable<SettlementSpot> AllPropSpots() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { PropFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
                .SelectMany(prefab => prefab.GetComponentsInChildren<SettlementSpot>(true))
                .Where(spot => spot.Use != null);
    }
}
