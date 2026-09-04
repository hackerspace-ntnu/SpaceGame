// The rule that decides which chunks a view DRAWS around somebody, as opposed to which ones the
// streamer loads under them (ChunkStreamingAnchorTests).
//
// Written because the ship's map projector was charting a window built as "the player's chunk plus
// or minus a radius". That window is centred on the CHUNK: it reaches up to half a chunk further
// on one side of the player than the other and swaps sides at every boundary, so the terrain sat
// visibly off the centre of a hologram whose whole job is to say "you are here".
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World.Streaming;

namespace SpaceGame.Tests
{
    public class ChunkWindowTests
    {
        // Mirrors Assets/Game/Settings/WorldStreamingConfig.asset: 8 x 6 chunks of 500 m from
        // (0, -1000), so the world rectangle is X [0..4000], Z [-1000..2000].
        private static ChunkGrid Shipped()
            => new ChunkGrid(new Vector3(0f, 0f, -1000f), new Vector2(500f, 500f), new Vector2Int(8, 6));

        // The ship's projector: viewRadius 3, so 7 chunks = 3500 m across.
        private static readonly Vector2 ProjectorView = new Vector2(3500f, 3500f);

        private const float ChunkSize = 500f;
        private const float Tolerance = 0.001f;

        /// <summary>The window's world-space rectangle on XZ, as (min, max) corners.</summary>
        private static void Rect(ChunkGrid grid, Vector2Int min, Vector2Int max,
                                 out Vector2 lo, out Vector2 hi)
        {
            lo = new Vector2(grid.Origin.x + min.x * grid.ChunkSize.x,
                             grid.Origin.z + min.y * grid.ChunkSize.y);
            hi = new Vector2(grid.Origin.x + max.x * grid.ChunkSize.x,
                             grid.Origin.z + max.y * grid.ChunkSize.y);
        }

        [Test]
        public void TheWindowReachesTheWholeViewOnEverySideOfThePosition()
        {
            var grid = Shipped();
            Vector2 half = ProjectorView * 0.5f;

            // Deliberately includes positions at both ends of a chunk and dead on a boundary —
            // the old window gave all three of those the same answer.
            foreach (var pos in new[]
            {
                new Vector3(2000f, 90f, 500f),    // mid-world
                new Vector3(1501f, 90f, 1f),      // just inside a chunk
                new Vector3(1999f, 90f, 499f),    // just short of the next boundary
                new Vector3(1500f, 90f, 0f),      // exactly on a boundary
                new Vector3(120f, 90f, -960f),    // in the corner chunk
            })
            {
                grid.WindowAround(pos, ProjectorView, out var min, out var max);
                Rect(grid, min, max, out var lo, out var hi);

                Assert.LessOrEqual(lo.x, pos.x - half.x + Tolerance, $"west edge at {pos}");
                Assert.LessOrEqual(lo.y, pos.z - half.y + Tolerance, $"south edge at {pos}");
                Assert.GreaterOrEqual(hi.x, pos.x + half.x - Tolerance, $"east edge at {pos}");
                Assert.GreaterOrEqual(hi.y, pos.z + half.y - Tolerance, $"north edge at {pos}");

                // And no more than the one chunk of overhang that squaring off to chunk edges
                // costs — otherwise the window is quietly drawing terrain nobody asked for.
                Assert.Greater(lo.x, pos.x - half.x - ChunkSize, $"west overhang at {pos}");
                Assert.Less(hi.x, pos.x + half.x + ChunkSize, $"east overhang at {pos}");
            }
        }

        [Test]
        public void TwoPositionsInOneChunk_GetDifferentWindows()
        {
            var grid = Shipped();
            var west = new Vector3(1510f, 90f, 300f);
            var east = new Vector3(1990f, 90f, 300f);

            Assert.AreEqual(grid.ToCoord(west), grid.ToCoord(east), "both should sit in one chunk");

            grid.WindowAround(west, ProjectorView, out var westMin, out var westMax);
            grid.WindowAround(east, ProjectorView, out var eastMin, out var eastMax);

            Assert.AreNotEqual(westMin, eastMin);
            Assert.AreNotEqual(westMax, eastMax);
        }

        [Test]
        public void AViewOfWholeChunks_CentredOnABoundary_IsExactlyThatViewAndNothingMore()
        {
            var grid = Shipped();
            // 3500 m is seven chunks; centred on a corner it takes three whole chunks either side
            // and splits the seventh, so eight chunks with half a chunk of overhang each way.
            // (1500, 0) is chunk corner (3, 2) — X counts from the origin, Z from z = -1000.
            grid.WindowAround(new Vector3(1500f, 90f, 0f), ProjectorView, out var min, out var max);

            Assert.AreEqual(new Vector2Int(3 - 4, 2 - 4), min);
            Assert.AreEqual(new Vector2Int(3 + 4, 2 + 4), max);
        }

        [Test]
        public void AGridWithNoChunks_WindowsNothing()
        {
            var empty = new ChunkGrid(Vector3.zero, Vector2.zero, Vector2Int.zero);
            empty.WindowAround(Vector3.zero, ProjectorView, out var min, out var max);

            Assert.AreEqual(Vector2Int.zero, min);
            Assert.AreEqual(Vector2Int.zero, max);
        }
    }
}
