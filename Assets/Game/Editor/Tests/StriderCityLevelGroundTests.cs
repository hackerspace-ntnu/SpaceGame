// The walking city only stands still on fairly flat ground: at its start, and at every stop where
// its crew go ashore. Both are LevelGroundSearch over the city's footprint with the ground injected,
// so the selection is tested here with no terrain, NavMesh or scene at all.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class StriderCityLevelGroundTests
    {
        /// <summary>A rule the size of a small city, round numbers: 100 m square, 2° → 3.49 m of relief.</summary>
        private static readonly LevelGroundRule Rule = new LevelGroundRule
        {
            footprintRadius = 50f,
            maxSlopeDegrees = 2f,
            searchRadius = 100f,
            searchStep = 50f,
            attempts = 4,
            sampleReach = 60f,
            sampleTolerance = 10f,
        };

        private static bool Flat(Vector2 at, out float y) { y = 10f; return true; }

        /// <summary>A 1-in-4 slope everywhere west of x = 150; a flat pan at 37.5 m east of it.</summary>
        private static bool SlopeThenPan(Vector2 at, out float y)
        {
            y = at.x < 150f ? at.x * 0.25f : 37.5f;
            return true;
        }

        private static bool Steep(Vector2 at, out float y) { y = at.x * 0.25f; return true; }

        // ── LevelGroundSearch, generalised ───────────────────────────────────

        [Test]
        public void TheRingSearch_SaysWhetherItsAnswerWasLevel()
        {
            Assert.IsTrue(LevelGroundSearch.TryFind(Vector2.zero, 0f, Rule.Extents, Rule.MaxSpread, 100f, 50f,
                                                    Flat, out _, out _, out bool flatIsLevel));
            Assert.IsTrue(flatIsLevel);

            Assert.IsTrue(LevelGroundSearch.TryFind(Vector2.zero, 0f, Rule.Extents, Rule.MaxSpread, 100f, 50f,
                                                    Steep, out _, out _, out bool steepIsLevel),
                          "the ship's search still settles for the least bad ground");
            Assert.IsFalse(steepIsLevel, "and says so, for a caller that will not settle");
        }

        [Test]
        public void TheRingSearch_AnswersExactlyAsBefore_WhetherOrNotLevelIsAsked()
        {
            LevelGroundSearch.TryFind(Vector2.zero, 0f, Rule.Extents, 0.5f, 200f, 50f, SlopeThenPan,
                                      out Vector2 plainXZ, out float plainY);
            LevelGroundSearch.TryFind(Vector2.zero, 0f, Rule.Extents, 0.5f, 200f, 50f, SlopeThenPan,
                                      out Vector2 levelXZ, out float levelY, out bool _);

            Assert.AreEqual(plainXZ, levelXZ);
            Assert.AreEqual(plainY, levelY);
        }

        [Test]
        public void TheFlattest_IsPicked_NotTheFirstLevelEnough()
        {
            var candidates = new List<Vector2> { new(0f, 0f), new(1000f, 0f), new(2000f, 0f) };
            // Relief grows with distance from x = 1000: all three level enough, the middle one flattest.
            bool Bowl(Vector2 at, out float y) { y = Mathf.Abs(at.x - 1000f) * 0.001f; return true; }

            Assert.IsTrue(LevelGroundSearch.TryFindFlattest(candidates, 0f, Rule.Extents, Rule.MaxSpread, Bowl,
                                                            out int index, out float spread));
            Assert.AreEqual(1, index);
            Assert.LessOrEqual(spread, Rule.MaxSpread);
        }

        [Test]
        public void TheFlattest_RefusesWhenNothingIsLevelEnough_OrMeasurable()
        {
            var candidates = new List<Vector2> { new(0f, 0f), new(500f, 0f) };
            Assert.IsFalse(LevelGroundSearch.TryFindFlattest(candidates, 0f, Rule.Extents, Rule.MaxSpread, Steep,
                                                             out int steepIndex, out _));
            Assert.AreEqual(-1, steepIndex);

            bool Nothing(Vector2 at, out float y) { y = 0f; return false; }
            Assert.IsFalse(LevelGroundSearch.TryFindFlattest(candidates, 0f, Rule.Extents, Rule.MaxSpread, Nothing,
                                                             out _, out _));
        }

        [Test]
        public void TheFlattest_TiesGoToTheEarlierCandidate()
        {
            var candidates = new List<Vector2> { new(0f, 0f), new(500f, 0f), new(900f, 0f) };
            Assert.IsTrue(LevelGroundSearch.TryFindFlattest(candidates, 0f, Rule.Extents, Rule.MaxSpread, Flat,
                                                            out int index, out _));
            Assert.AreEqual(0, index, "the caller's order is its preference, and a re-run must pick the same place");
        }

        // ── Stops ────────────────────────────────────────────────────────────

        [Test]
        public void AStopOnLevelGround_IsKeptWhereItIs()
        {
            Assert.IsTrue(NpcTaskPlanner.TryLevelStop(new Vector3(300f, 10f, 400f), Rule, Flat, out Vector3 stop));
            Assert.AreEqual(new Vector3(300f, 10f, 400f), stop);
        }

        [Test]
        public void AStopOnASlope_MovesOntoThePanBesideIt()
        {
            Assert.IsTrue(NpcTaskPlanner.TryLevelStop(new Vector3(120f, 30f, 0f), Rule, SlopeThenPan, out Vector3 stop));
            HullFootprint.Ground ground = HullFootprint.Measure(new Vector2(stop.x, stop.z), 0f, Rule.Extents, SlopeThenPan);
            Assert.LessOrEqual(ground.Spread, Rule.MaxSpread, "the footprint round the stop is level");
            Assert.GreaterOrEqual(stop.x, 150f, "its centre stands on the pan");
            Assert.AreEqual(37.5f, stop.y, 0.001f, "the stop's height is the ground under its centre");
            Assert.LessOrEqual(Vector2.Distance(new Vector2(stop.x, stop.z), new Vector2(120f, 0f)), Rule.searchRadius);
        }

        [Test]
        public void AStopOnASlopeWithNoPanInReach_IsRejected()
        {
            Assert.IsFalse(NpcTaskPlanner.TryLevelStop(new Vector3(0f, 0f, 0f), Rule, Steep, out _),
                           "the city walks on to somewhere else rather than stopping on a dune face");
        }

        [Test]
        public void AStopOverAHoleInTheNavMesh_IsRejected()
        {
            // A building 60 m wide at the candidate: its corners are measurable, its middle is not.
            bool Holed(Vector2 at, out float y) { y = 10f; return at.magnitude > 30f; }
            var tight = Rule;
            tight.searchRadius = 0f;

            Assert.IsFalse(NpcTaskPlanner.TryLevelStop(Vector3.zero, tight, Holed, out _),
                           "ground nothing can vouch for is where the crew would step off into a wall");
        }

        [Test]
        public void TheCitysRule_IsItsCarriersExtent_AndFairlyFlat()
        {
            LevelGroundRule city = StriderCityBuilder.CityLevelGround;

            Assert.IsTrue(city.Enabled);
            Assert.AreEqual(RosterAuthoring.CityFarthestCarrierSlot, city.footprintRadius, 0.001f,
                            "every house's and barge's slot is on the footprint, whichever way the column arrived");
            Assert.AreEqual(Mathf.Tan(city.maxSlopeDegrees * Mathf.Deg2Rad) * 2f * city.footprintRadius, city.MaxSpread, 0.001f);
            Assert.GreaterOrEqual(city.sampleReach, city.MaxSpread,
                                  "a sample must be able to find ground anywhere the limit accepts");
            Assert.Greater(city.attempts, 0);
            Assert.Greater(city.searchStep, 0f);
        }

        [Test]
        public void ADefaultTask_AsksForNoLevelGround()
        {
            Assert.IsFalse(new NpcTask().levelGround.Enabled, "every other caravan stops on any ground, as before");
        }

        // ── The start ────────────────────────────────────────────────────────

        [Test]
        public void StartCandidates_StayInTheBand_NearestTheStartDistanceFirst()
        {
            Vector2 spawn = new(1000f, 2000f);
            List<Vector2> candidates = StriderCityStartSite.Candidates(spawn);

            Assert.AreEqual(StriderCityStartSite.CityStartDistance, Vector2.Distance(candidates[0], spawn), 0.01f);
            float previous = 0f;
            foreach (Vector2 c in candidates)
            {
                float distance = Vector2.Distance(c, spawn);
                Assert.That(distance,
                            Is.InRange(StriderCityStartSite.CityStartDistance - StriderCityStartSite.CityStartBand - 0.01f,
                                       StriderCityStartSite.CityStartDistance + StriderCityStartSite.CityStartBand + 0.01f));
                float offBand = Mathf.Abs(distance - StriderCityStartSite.CityStartDistance);
                Assert.GreaterOrEqual(offBand, previous - 0.01f, "nearest the start distance first");
                previous = offBand;
            }
            Assert.AreEqual(candidates.Count, candidates.Distinct().Count());
        }

        [Test]
        public void StartCandidates_LeaveNoGapInTheBand()
        {
            // A fan of bearings stepped past the one level pan beside the spawn (2026-10-04): every
            // point of the band must have a candidate within half a grid cell's diagonal.
            Vector2 spawn = new(1000f, 2000f);
            List<Vector2> candidates = StriderCityStartSite.Candidates(spawn);
            float reach = StriderCityStartSite.CityStartBandStep * Mathf.Sqrt(2f) / 2f + 0.01f;

            for (int bearing = 0; bearing < 360; bearing += 7)
                for (float distance = StriderCityStartSite.CityStartDistance - StriderCityStartSite.CityStartBand + reach;
                     distance <= StriderCityStartSite.CityStartDistance + StriderCityStartSite.CityStartBand - reach;
                     distance += 37f)
                {
                    Vector3 turned = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * distance;
                    Vector2 point = spawn + new Vector2(turned.x, turned.z);
                    Assert.LessOrEqual(candidates.Min(c => Vector2.Distance(c, point)), reach, $"gap at {point}");
                }
        }

        [Test]
        public void TheStart_IsTheFlattestAcceptableCandidate()
        {
            var candidates = new List<Vector2> { new(0f, 0f), new(1000f, 0f), new(2000f, 0f), new(3000f, 0f) };
            // Flattest at x = 2000, but that one is refused (the Clanker town's ring, say).
            bool Bowl(Vector2 at, out float y) { y = Mathf.Abs(at.x - 2000f) * 0.001f; return true; }
            bool NotTheTown(Vector2 at) => at.x != 2000f;

            Assert.IsTrue(StriderCityStartSite.TryPickFlattest(candidates, NotTheTown, Bowl, Rule,
                                                               out Vector2 site, out float spread));
            Assert.AreEqual(new Vector2(1000f, 0f), site, "tied with x = 3000; the earlier one is preferred");
            Assert.LessOrEqual(spread, Rule.MaxSpread);
        }

        [Test]
        public void TheStart_IsRefused_WhenNothingAcceptableIsLevel()
        {
            var candidates = new List<Vector2> { new(0f, 0f), new(1000f, 0f) };

            Assert.IsFalse(StriderCityStartSite.TryPickFlattest(candidates, _ => true, Steep, Rule, out _, out _));
            Assert.IsFalse(StriderCityStartSite.TryPickFlattest(candidates, _ => false, Flat, Rule, out _, out _));
        }
    }
}
