// The terrace kit's layout rules, on the pure logic: how long a stair chain is and what it is made
// of, how many courses a wall drop takes, which joints get pillars, how a road run is cut into tiles,
// how a street's level changes merge into multi-level chains that fit, and the stepping-stone scatter.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class TerraceKitLayoutTests
    {
        private const float FlightRun = 2.7f, LandingRun = 3.5f, Step = 1.5f;
        private const int FlightsPerLanding = 2;

        private static float ChainLength(int levels) => SettlementStairChain.Length(levels, FlightRun, LandingRun, FlightsPerLanding);

        [Test]
        public void AChainIsAFlightPerLevelWithALandingAfterEveryOtherFlight()
        {
            Assert.AreEqual(2.7f, ChainLength(1), 1e-4f);
            Assert.AreEqual(5.4f, ChainLength(2), 1e-4f, "two flights need no landing between them...");
            Assert.AreEqual(11.6f, ChainLength(3), 1e-4f, "...three do: a landing after the second");
            Assert.AreEqual(14.3f, ChainLength(4), 1e-4f);
            Assert.AreEqual(20.5f, ChainLength(5), 1e-4f);

            var pieces = SettlementStairChain.Pieces(4, FlightRun, LandingRun, Step, FlightsPerLanding);
            Assert.AreEqual(5, pieces.Count, "four flights and one landing; none after the last flight");
            float[] along = { 0f, 2.7f, 5.4f, 8.9f, 11.6f };
            float[] drop = { 0f, 1.5f, 3f, 3f, 4.5f };
            for (int i = 0; i < pieces.Count; i++)
            {
                Assert.AreEqual(i == 2, pieces[i].isLanding, $"piece {i}");
                Assert.AreEqual(along[i], pieces[i].along, 1e-4f, $"piece {i} along");
                Assert.AreEqual(drop[i], pieces[i].drop, 1e-4f, $"piece {i} drop");
            }
        }

        [Test]
        public void TheGroundUnderAChainFollowsItsNosingsAndLevelsOutOnLandings()
        {
            float DropAt(float along) => SettlementStairChain.DropAt(along, 3, FlightRun, LandingRun, Step, FlightsPerLanding);
            Assert.AreEqual(0f, DropAt(-1f), 1e-4f, "behind the top");
            Assert.AreEqual(0.75f, DropAt(1.35f), 1e-4f, "half way down the top flight");
            Assert.AreEqual(3f, DropAt(5.4f + 1f), 1e-4f, "on the landing");
            Assert.AreEqual(4.5f, DropAt(11.6f), 1e-4f, "at the foot");
            Assert.AreEqual(4.5f, DropAt(20f), 1e-4f, "past the foot");
        }

        [Test]
        public void AWallTakesCoursesUntilItReachesTheGroundRoundingUp()
        {
            Assert.AreEqual(0, SettlementWallStack.CourseCount(1.5f, Step, 3, out bool tooTall), "a one-level drop is the top piece alone");
            Assert.IsFalse(tooTall);
            Assert.AreEqual(0, SettlementWallStack.CourseCount(1.52f, Step, 3, out _), "a few cm of drift needs no extra course");
            Assert.AreEqual(1, SettlementWallStack.CourseCount(1.7f, Step, 3, out _), "round up: the footing ends up buried");
            Assert.AreEqual(1, SettlementWallStack.CourseCount(3f, Step, 3, out _));
            Assert.AreEqual(2, SettlementWallStack.CourseCount(3.2f, Step, 3, out _));
            Assert.AreEqual(3, SettlementWallStack.CourseCount(7f, Step, 3, out tooTall), "capped");
            Assert.IsTrue(tooTall, "and reported");
            Assert.AreEqual(0, SettlementWallStack.CourseCount(-1f, Step, 3, out _), "ground above the wall top needs nothing");
        }

        [Test]
        public void PillarsStandAtBothEndsEveryFewBlocksAndWhereTheWallTurns()
        {
            CollectionAssert.AreEqual(new[] { 0, 3, 6, 7 }, SettlementWallStack.PillarJoints(new float[] { 0, 0, 0, 0, 0, 0, 0 }, 3, 12f));
            CollectionAssert.AreEqual(new[] { 0, 3, 4 }, SettlementWallStack.PillarJoints(new float[] { 0, 5, 10, 15 }, 10, 12f),
                                      "15 degrees turned since the start passes 12");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, SettlementWallStack.PillarJoints(new float[] { 355, 10 }, 10, 12f),
                                      "a turn across north is 15 degrees, not 345");
            CollectionAssert.AreEqual(new[] { 0, 1 }, SettlementWallStack.PillarJoints(new float[] { 90 }, 3, 12f), "a lone block gets both ends");
            CollectionAssert.IsEmpty(SettlementWallStack.PillarJoints(new float[0], 3, 12f));
        }

        [Test]
        public void ARoadRunGetsWholeTilesStretchedOnlyALittle()
        {
            Assert.IsTrue(SettlementRoadTiles.FitRun(8f, 2f, 0.15f, out int count, out float scale));
            Assert.AreEqual(4, count);
            Assert.AreEqual(1f, scale, 1e-4f);

            Assert.IsTrue(SettlementRoadTiles.FitRun(8.5f, 2f, 0.15f, out count, out scale));
            Assert.AreEqual(4, count);
            Assert.AreEqual(1.0625f, scale, 1e-4f);

            Assert.IsFalse(SettlementRoadTiles.FitRun(2.9f, 2f, 0.15f, out count, out scale), "one tile stretched 45% is left bare");
            Assert.IsFalse(SettlementRoadTiles.FitRun(1.2f, 2f, 0.15f, out _, out _), "a stub shorter than a tile is left bare");
        }

        [Test]
        public void RoadRunsAreWhatTheNodesStairsAndEndsLeaveFree()
        {
            var blocked = new List<Vector2> { new(5f, 7f), new(-1f, 2f), new(18f, 25f), new(6f, 9f) };
            CollectionAssert.AreEqual(new[] { new Vector2(2f, 5f), new Vector2(9f, 18f) }, SettlementRoadTiles.FreeRuns(20f, blocked));
            CollectionAssert.AreEqual(new[] { new Vector2(0f, 20f) }, SettlementRoadTiles.FreeRuns(20f, new List<Vector2>()));
        }

        [Test]
        public void AStreetKitProfileStillClimbsOneLevelAtATime()
        {
            var runs = new List<(float, float, int)> { (0f, 10f, 0), (10f, 20f, 3), (20f, 40f, 3) };
            SettlementTerraceField.ClampSteps(runs, 1, null, 9f);
            CollectionAssert.AreEqual(new List<(float, float, int)> { (0f, 10f, 0), (10f, 20f, 1), (20f, 40f, 2) }, runs,
                                      "each run one level past the last, trailing behind the hill");
        }

        [Test]
        public void ALevelChangeClimbsSeveralLevelsWhereTheChainFitsBelowIt()
        {
            // 10 m of lower run: three levels (11.6 m + 4.5 m to spare) do not fit, two (5.4 + 4.5) do.
            var runs = new List<(float, float, int)> { (0f, 10f, 0), (10f, 20f, 3), (20f, 40f, 3) };
            SettlementTerraceField.ClampSteps(runs, 4, ChainLength, 9f);
            CollectionAssert.AreEqual(new List<(float, float, int)> { (0f, 10f, 0), (10f, 20f, 2), (20f, 40f, 3) }, runs);

            var plenty = new List<(float, float, int)> { (0f, 30f, 0), (30f, 40f, 3) };
            SettlementTerraceField.ClampSteps(plenty, 4, ChainLength, 9f);
            Assert.AreEqual(3, plenty[1].Item3, "a long enough lower run takes the whole climb at once");

            var capped = new List<(float, float, int)> { (0f, 40f, 0), (40f, 50f, 6) };
            SettlementTerraceField.ClampSteps(capped, 4, ChainLength, 9f);
            Assert.AreEqual(4, capped[1].Item3, "never more than maxStep");
        }

        [Test]
        public void ADescendingChainStandsOnTheRunAfterItAndLeavesRoomForTheNext()
        {
            var runs = new List<(float, float, int)> { (0f, 20f, 3), (20f, 30f, 0) };
            SettlementTerraceField.ClampSteps(runs, 4, ChainLength, 9f);
            Assert.AreEqual(1, runs[1].Item3, "10 m after the change fits two levels down, not three");

            // Down two levels onto a 15 m run, then up again: the climb's chain must share that run with
            // the descent's 5.4 m, leaving 9.6 m -- room for one flight (7.2), not two (9.9).
            var valley = new List<(float, float, int)> { (0f, 20f, 2), (20f, 35f, 0), (35f, 60f, 2) };
            SettlementTerraceField.ClampSteps(valley, 4, ChainLength, 9f);
            CollectionAssert.AreEqual(new List<(float, float, int)> { (0f, 20f, 2), (20f, 35f, 0), (35f, 60f, 1) }, valley);
        }

        [Test]
        public void StonesScatterTheSameWayForTheSameSeedAndKeepTheirDistance()
        {
            var style = ScriptableObject.CreateInstance<SettlementStreetStyle>();
            var broad = new GameObject("Broad");
            var narrow = new GameObject("Narrow");
            try
            {
                style.stones.pieces = new[]
                {
                    new SettlementStreetStyle.WeightedPiece { prefab = broad, weight = 2f },
                    new SettlementStreetStyle.WeightedPiece { prefab = narrow, weight = 1f },
                };
                Rect[] footprints = { new Rect(-0.45f, -0.4f, 0.9f, 0.8f), new Rect(-0.5f, -0.2f, 1f, 0.4f) };
                var line = new List<Vector2> { new(0f, 0f), new(0f, 30f) };
                var blocked = new List<Vector2> { new(20f, 24f) };
                // A road crossing the path between 10 and 13.5 m.
                var road = new SettlementFootprint(new Vector2(0f, 11.75f), new Vector2(6f, 3.5f), Quaternion.identity);
                bool Clear(SettlementFootprint stone) => SettlementFootprint.Gap(stone, road) >= style.stones.minGap;

                List<SettlementStonePath.Stone> Scatter(int seed)
                {
                    var rng = new SettlementPlacementUtil.SeededRng(seed);
                    return SettlementStonePath.Scatter(line, blocked, 1.125f, fadeAtEnd: true, style.stones, footprints, Clear, ref rng);
                }

                var first = Scatter(42);
                var again = Scatter(42);
                Assert.Greater(first.Count, 10, "up to a stone per 0.9 m over the 22 m left free, less the ones that clashed");
                Assert.AreEqual(first.Count, again.Count);
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.AreEqual(first[i].position, again[i].position, $"stone {i}");
                    Assert.AreEqual(first[i].piece, again[i].piece, $"stone {i}");
                }
                CollectionAssert.AreNotEqual(Positions(first), Positions(Scatter(43)), "another seed scatters differently");

                for (int i = 0; i < first.Count; i++)
                {
                    var stone = first[i];
                    Assert.GreaterOrEqual(SettlementFootprint.Gap(stone.footprint, road), style.stones.minGap - 1e-4f, $"stone {i} lies on the road");
                    Assert.IsFalse(stone.position.y > 20f && stone.position.y < 24f, $"stone {i} lies on the blocked stretch");
                    Assert.LessOrEqual(Mathf.Abs(stone.position.x), 1.125f * style.stones.lateral + 1e-4f, $"stone {i} strays out of the corridor");
                    for (int j = i + 1; j < first.Count; j++)
                        Assert.GreaterOrEqual(SettlementFootprint.Gap(stone.footprint, first[j].footprint), style.stones.minGap - 1e-4f,
                                              $"stones {i} and {j} too close");
                }

                var last = first[first.Count - 1];
                Assert.Less(last.scale, 1f - style.stones.scaleJitter + 1e-4f, "the last stone at a dead end is smaller than any normal one");
            }
            finally
            {
                Object.DestroyImmediate(broad);
                Object.DestroyImmediate(narrow);
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void AStonePathFollowsTheGroundInsteadOfStepping()
        {
            var runs = new List<(float, float, int)> { (0f, 10f, 0), (10f, 20f, 3), (20f, 40f, 5) };
            SettlementTerraceField.ClampSteps(runs, 1, ChainLength, 9f, terraced: arc => arc < 15f);
            Assert.AreEqual(1, runs[1].Item3, "the stretch with stairs still climbs one level at a time");
            Assert.AreEqual(5, runs[2].Item3, "the stone path takes the ground's own level, unclamped");
        }

        // A street along +X from `from`, one point every 10 m.
        private static SettlementStreetNetwork.Street Street(int order, Vector2 from, Vector2 direction, float length, int parent = -1, float parentArc = 0f)
        {
            var street = new SettlementStreetNetwork.Street(order, 1f, parent, parentArc, from);
            for (float s = 10f; s <= length + 1e-3f; s += 10f)
            {
                street.points.Add(from + direction * s);
                street.arcs.Add(s);
            }
            return street;
        }

        [Test]
        public void TheRoadIsTheMainStreetsCoreSlabsComeNextAndPathsTheRest()
        {
            var style = ScriptableObject.CreateInstance<SettlementStreetStyle>();
            var tile = new GameObject("Tile");
            try
            {
                style.road.tiles = new[] { new SettlementStreetStyle.WeightedPiece { prefab = tile } };
                style.slabs = new[] { new SettlementStreetStyle.WeightedPiece { prefab = tile } };
                var streets = new List<SettlementStreetNetwork.Street>
                {
                    Street(SettlementStreetNetwork.MainStreet, Vector2.zero, Vector2.right, 50f),
                    Street(SettlementStreetNetwork.MainStreet, Vector2.zero, Vector2.left, 50f),
                    Street(SettlementStreetNetwork.SideStreet, new Vector2(20f, 0f), Vector2.up, 40f, 0, 20f),
                    Street(SettlementStreetNetwork.Alley, new Vector2(20f, 20f), Vector2.right, 20f, 2, 20f),
                };
                // 160 m in all: 24 m of road, 40 m of slabs.
                var sections = SettlementStreetSurfaces.Assign(streets, Vector2.zero, style, buildings: 12);
                Assert.AreEqual(SettlementStreetStyle.StreetSurface.Road, SettlementStreetSurfaces.At(sections[0], 5f), "the main street at the centre is road");
                Assert.AreEqual(SettlementStreetStyle.StreetSurface.StonePath, SettlementStreetSurfaces.At(sections[3], 5f), "an alley is always a path");
                Assert.AreNotEqual(SettlementStreetStyle.StreetSurface.Road, SettlementStreetSurfaces.At(sections[2], 5f), "a side street never gets road");

                float road = 0f;
                for (int s = 0; s < streets.Count; s++)
                {
                    Assert.AreEqual(0f, sections[s][0].from, 1e-4f, $"street {s} is covered from its start");
                    Assert.AreEqual(streets[s].Length, sections[s][sections[s].Count - 1].to, 1e-4f, $"street {s} is covered to its end");
                    for (int i = 0; i < sections[s].Count; i++)
                    {
                        if (sections[s][i].surface == SettlementStreetStyle.StreetSurface.Road) road += sections[s][i].to - sections[s][i].from;
                        if (i > 0)
                            Assert.Greater(SettlementStreetSurfaces.Rank(sections[s][i].surface), SettlementStreetSurfaces.Rank(sections[s][i - 1].surface),
                                           $"street {s} steps back up to a better surface going out");
                    }
                }
                Assert.That(road, Is.InRange(20f, 30f), "about roadShare of the length, to whole 10 m segments");

                var small = SettlementStreetSurfaces.Assign(streets, Vector2.zero, style, buildings: 5);
                foreach (var street in small)
                    foreach (var section in street)
                        Assert.AreNotEqual(SettlementStreetStyle.StreetSurface.Road, section.surface, "a small town gets no road");
            }
            finally
            {
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void StreetsAreCutBackToTheirLastHouseButKeepTheirBranches()
        {
            var streets = new List<SettlementStreetNetwork.Street>
            {
                Street(SettlementStreetNetwork.MainStreet, Vector2.zero, Vector2.right, 60f),
                Street(SettlementStreetNetwork.SideStreet, new Vector2(40f, 0f), Vector2.up, 30f, 0, 40f),
                Street(SettlementStreetNetwork.SideStreet, new Vector2(10f, 0f), Vector2.down, 30f, 0, 10f),
            };
            float[] keep = SettlementStreetNetwork.KeepLengths(streets, new[] { 20f, 12f, -1f }, 3f);
            Assert.AreEqual(40f, keep[0], 1e-4f, "the main street reaches the branch that has houses");
            Assert.AreEqual(15f, keep[1], 1e-4f, "a branch ends a margin past its last house");
            Assert.AreEqual(0f, keep[2], "a branch nobody lives on goes");

            float[] none = SettlementStreetNetwork.KeepLengths(streets, new[] { -1f, -1f, -1f }, 3f);
            Assert.AreEqual(60f, none[0], 1e-4f, "with no houses at all the main street stays");
        }

        [Test]
        public void APlazaRingsItsLargestBuildingsRoundTheCentreWithLanesLeftOpen()
        {
            var style = ScriptableObject.CreateInstance<SettlementStreetStyle>();
            var prefab = new GameObject("House");
            try
            {
                var buildings = new List<SettlementStreetPlots.Building>();
                for (int i = 0; i < 9; i++)
                    buildings.Add(new SettlementStreetPlots.Building(prefab, new Rect(-6f + i * 0.3f, -5f, 12f - i * 0.6f, 10f)));
                var rng = new SettlementPlacementUtil.SeededRng(7);
                const float laneHalf = 2f, gap = 0.5f;
                var plaza = SettlementPlaza.Plan(buildings, style, gap, Vector2.zero, laneHalf, ref rng);

                Assert.AreEqual(5, plaza.ring.Count, "60 % of 9, the largest");
                Assert.AreEqual(4, plaza.rest.Count);
                Assert.That(plaza.lanes.Count, Is.InRange(style.plazaMinLanes, style.plazaMaxLanes));
                Assert.GreaterOrEqual(plaza.radius, style.plazaMinRadius);

                for (int i = 0; i < plaza.ring.Count; i++)
                {
                    var plot = plaza.ring[i];
                    Assert.IsTrue(plot.facesPlaza);
                    Vector3 front = plot.rotation * Vector3.forward;
                    Vector2 toCentre = -plot.footprint.center.normalized;
                    Assert.Greater(Vector2.Dot(new Vector2(front.x, front.z), toCentre), 0.99f, $"building {i} faces the centre");
                    for (int j = i + 1; j < plaza.ring.Count; j++)
                        Assert.GreaterOrEqual(SettlementFootprint.Gap(plot.footprint, plaza.ring[j].footprint), 0f, $"buildings {i} and {j} overlap");
                    foreach (var lane in plaza.lanes)
                    {
                        var corridor = SettlementStreetNetwork.Corridor(Vector2.zero, lane * (plaza.radius + 20f), laneHalf);
                        Assert.GreaterOrEqual(SettlementFootprint.Gap(plot.footprint, corridor), 0f, $"building {i} blocks a lane");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(style);
            }
        }

        private static List<Vector2> Positions(List<SettlementStonePath.Stone> stones) => stones.ConvertAll(s => s.position);
    }
}
