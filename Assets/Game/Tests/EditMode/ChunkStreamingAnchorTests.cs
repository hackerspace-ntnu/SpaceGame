// The rule that decides which chunks a moving player needs.
//
// Written against the shipped world's real numbers, because the bug these cover is a
// collision between the streaming rule and where the DuneFoil is berthed: the craft sits
// 202 m from the east edge of the grid and does 45 m/s, so five seconds of sailing puts the
// player outside the grid rectangle. The old rule discarded any tracker outside that
// rectangle, which meant the player stopped requiring chunks entirely — nothing new loaded,
// and after the 10 s grace period every loaded chunk unloaded from under them.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World.Streaming;

namespace SpaceGame.Tests
{
    public class ChunkStreamingAnchorTests
    {
        // Mirrors Assets/Game/Settings/WorldStreamingConfig.asset: 8 x 6 chunks of 500 m from
        // (0, -1000), so the world rectangle is X [0..4000], Z [-1000..2000].
        private static ChunkGrid Shipped()
            => new ChunkGrid(new Vector3(0f, 0f, -1000f), new Vector2(500f, 500f), new Vector2Int(8, 6));

        private const float OffWorld = 2000f;
        private const float FoilTopSpeed = 45f;

        private static readonly Vector3 Berth = new Vector3(3796f, 100f, 1576f);
        private static readonly Vector3 Arena = new Vector3(20600f, 101f, 400f);

        [Test]
        public void InsideTheGrid_AnchorsToItsOwnChunk()
        {
            Assert.IsTrue(Shipped().TryGetStreamingCoord(new Vector3(1200f, 90f, 300f), OffWorld, out var coord));
            Assert.AreEqual(new Vector2Int(2, 2), coord);
        }

        [Test]
        public void TheFoilBerth_SitsInTheCornerChunkWithinSecondsOfTheEdge()
        {
            var grid = Shipped();
            Assert.IsTrue(grid.Contains(Berth));
            Assert.AreEqual(new Vector2Int(7, 5), grid.ToCoord(Berth));

            // What makes this craft the one that finds the bug: the edge is seconds away.
            float toEastEdge = 4000f - Berth.x;
            Assert.Less(toEastEdge / FoilTopSpeed, 6f, "berth is within six seconds of the east edge");
        }

        [Test]
        public void JustPastTheEdge_StillAnchorsToTheEdgeChunk()
        {
            var grid = Shipped();
            var justOutside = new Vector3(4001f, 100f, 1576f);

            Assert.IsFalse(grid.Contains(justOutside), "this position is outside the grid rectangle");
            Assert.IsTrue(grid.TryGetStreamingCoord(justOutside, OffWorld, out var coord),
                          "a metre past the beach is still the world — it must keep streaming");
            Assert.AreEqual(new Vector2Int(7, 5), coord);
        }

        [Test]
        public void SailingStraightOffTheEdge_NeverLosesTheAnchor()
        {
            var grid = Shipped();
            Vector3 pos = Berth;

            // 40 seconds of sailing due east at top speed, sampled at the streamer's own cadence.
            for (int tick = 0; tick < 80; tick++)
            {
                pos += Vector3.right * (FoilTopSpeed * 0.5f);
                Assert.IsTrue(grid.TryGetStreamingCoord(pos, OffWorld, out var coord),
                              $"lost the streaming anchor at {pos}");
                Assert.AreEqual(new Vector2Int(7, 5), coord);
            }
        }

        [Test]
        public void FarOffWorld_DoesNotAnchor()
        {
            var grid = Shipped();

            // The minigame arena is placed off-grid on purpose. Someone fighting there must not
            // hold a world chunk loaded — which is what the strict-bounds test used to buy us.
            Assert.Greater(grid.DistanceOutside(Arena), OffWorld);
            Assert.IsFalse(grid.TryGetStreamingCoord(Arena, OffWorld, out _));
        }

        [Test]
        public void DistanceOutside_IsZeroInsideAndGrowsWithTheGap()
        {
            var grid = Shipped();

            Assert.AreEqual(0f, grid.DistanceOutside(Berth), 1e-3f);
            Assert.AreEqual(200f, grid.DistanceOutside(new Vector3(4200f, 0f, 1576f)), 1e-3f);
            Assert.AreEqual(300f, grid.DistanceOutside(new Vector3(1200f, 0f, 2300f)), 1e-3f);
            Assert.AreEqual(500f, grid.DistanceOutside(new Vector3(-300f, 0f, -1400f)), 1e-3f); // 3-4-5 corner
        }

        [Test]
        public void Lookahead_ReachesTheNextChunkAtSailingSpeed()
        {
            var grid = Shipped();

            // 60 m inside the chunk boundary at x = 3500, sailing west at top speed: two seconds
            // of travel crosses into chunk 6 and that is the chunk the loader has to start on.
            var pos = new Vector3(3560f, 100f, 1576f);
            Vector3 ahead = grid.PredictAhead(pos, Vector3.left * FoilTopSpeed, 2f);

            Assert.AreEqual(new Vector2Int(7, 5), grid.ToCoord(pos));
            Assert.AreEqual(new Vector2Int(6, 5), grid.ToCoord(ahead));
        }

        [Test]
        public void Lookahead_IgnoresVerticalSpeedAndStandsStill()
        {
            var grid = Shipped();

            // Falling off the mast is not travel across the grid.
            Assert.AreEqual(Berth, grid.PredictAhead(Berth, Vector3.down * 50f, 2f));
            Assert.AreEqual(Berth, grid.PredictAhead(Berth, Vector3.zero, 2f));
        }

        [Test]
        public void Lookahead_IsSkippedForTeleports()
        {
            var grid = Shipped();

            // A teleport reads as an enormous one-tick velocity, and its direction says nothing
            // about where anyone is going. Predicting from it queues a load nobody will stand on.
            Assert.AreEqual(Berth, grid.PredictAhead(Berth, Vector3.right * 20000f, 2f));

            // The boundary: a whole chunk of travel in one lookahead is the most that counts.
            Assert.AreEqual(Berth, grid.PredictAhead(Berth, Vector3.right * 251f, 2f));
            Assert.AreEqual(Berth + Vector3.right * 490f, grid.PredictAhead(Berth, Vector3.right * 245f, 2f));
        }

        [Test]
        public void ClampedCoordsStayInsideTheGrid()
        {
            var grid = Shipped();

            Assert.AreEqual(new Vector2Int(0, 0), grid.ToCoord(new Vector3(-9999f, 0f, -9999f)));
            Assert.AreEqual(new Vector2Int(7, 5), grid.ToCoord(new Vector3(9999f, 0f, 9999f)));
            Assert.IsTrue(grid.IsValidCoord(new Vector2Int(7, 5)));
            Assert.IsFalse(grid.IsValidCoord(new Vector2Int(8, 5)));
        }
    }
}
