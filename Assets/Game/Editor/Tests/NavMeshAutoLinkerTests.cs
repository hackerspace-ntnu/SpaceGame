// Where the bake links separate pieces of NavMesh, on hand-built meshes: two flat boxes a gap apart,
// a box a ledge below another, and the cases that must produce nothing (too far, too deep, a seam
// where a neighbouring tile's mesh begins right at the edge, a drop onto the same island).
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public class NavMeshAutoLinkerTests
    {
        private static readonly WorldNavMeshLinkSettings Settings = new();

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new();
            public readonly List<int> indices = new();

            /// <summary>A flat square of mesh, two triangles, its minimum corner at (x, y, z).</summary>
            public MeshBuilder Square(float x, float y, float z, float size)
            {
                int first = vertices.Count;
                vertices.Add(new Vector3(x, y, z));
                vertices.Add(new Vector3(x + size, y, z));
                vertices.Add(new Vector3(x + size, y, z + size));
                vertices.Add(new Vector3(x, y, z + size));
                indices.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
                return this;
            }

            public NavMeshAutoLinker.Result Link(WorldNavMeshLinkSettings settings = null, NavMeshAutoLinker.LinkBlocked blocked = null)
            {
                settings ??= Settings;
                var graph = new NavMeshGraph(vertices.ToArray(), indices.ToArray(), settings.weldTolerance);
                return NavMeshAutoLinker.Find(graph, settings, blocked);
            }
        }

        [Test]
        public void ASquareIsOneIslandWithFourBoundaryEdges()
        {
            var mesh = new MeshBuilder().Square(0, 0, 0, 10);
            var graph = new NavMeshGraph(mesh.vertices.ToArray(), mesh.indices.ToArray(), 0.01f);

            Assert.AreEqual(1, graph.IslandCount);
            Assert.AreEqual(100f, graph.IslandArea(0), 0.01f);
            Assert.AreEqual(4, graph.BoundaryEdges.Count);
        }

        [Test]
        public void SquaresSharingOnlyACornerAreSeparateIslands()
        {
            var mesh = new MeshBuilder().Square(0, 0, 0, 10).Square(10, 0, 10, 10);
            var graph = new NavMeshGraph(mesh.vertices.ToArray(), mesh.indices.ToArray(), 0.01f);

            Assert.AreEqual(2, graph.IslandCount);
        }

        [Test]
        public void SquaresSharingAnEdgeAreOneIslandEvenWithDuplicatedVertices()
        {
            var mesh = new MeshBuilder().Square(0, 0, 0, 10).Square(10, 0, 0, 10);
            var graph = new NavMeshGraph(mesh.vertices.ToArray(), mesh.indices.ToArray(), 0.01f);

            Assert.AreEqual(1, graph.IslandCount);
            Assert.AreEqual(6, graph.BoundaryEdges.Count);
        }

        [Test]
        public void ALinkCrossesAGapBothWays()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 5).Square(6, 0, 0, 5).Link();

            Assert.AreEqual(1, result.gaps);
            Assert.AreEqual(0, result.drops);
            var link = result.links[0];
            Assert.IsTrue(link.bidirectional);
            Assert.AreEqual(1f, Mathf.Abs(link.end.x - link.start.x), 0.2f);
        }

        // The edges either side of a thin fence look exactly like the edges of a gap, so a wall between the ends rules the link out.
        [Test]
        public void AWallBetweenTheEndsIsNotAGap()
        {
            var mesh = new MeshBuilder().Square(0, 0, 0, 5).Square(6, 0, 0, 5);

            Assert.AreEqual(1, mesh.Link().gaps, "the same two squares are a gap with nothing between them");
            Assert.AreEqual(0, mesh.Link(blocked: (from, to) => true).links.Count);
        }

        [Test]
        public void AGapWiderThanTheLimitIsNotLinked()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 5).Square(7, 0, 0, 5).Link();

            Assert.AreEqual(0, result.links.Count);
        }

        [Test]
        public void ALedgeIsAOneWayDropFromTheHighSideOnly()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 5).Square(6, -1.2f, 0, 30).Link();

            Assert.AreEqual(1, result.drops);
            Assert.AreEqual(0, result.gaps);
            var link = result.links[0];
            Assert.IsFalse(link.bidirectional);
            Assert.Greater(link.start.y, link.end.y);
        }

        [Test]
        public void ADropDeeperThanTheLimitIsNotLinked()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 5).Square(6, -3f, 0, 30).Link();

            Assert.AreEqual(0, result.links.Count);
        }

        [Test]
        public void ADropOntoATinyIslandIsNotLinked()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 5).Square(6, -1.2f, 0, 3).Link();

            Assert.AreEqual(0, result.links.Count, "a landing smaller than minDropLandingArea is a trap");
        }

        [Test]
        public void ASeamWhereAnotherTileStartsAtTheEdgeIsNotAGap()
        {
            // Two tiles meeting along x = 10 whose vertices do not line up: each reads as its own island
            // with a boundary along the seam, and neither has anything to jump.
            var mesh = new MeshBuilder().Square(0, 0, 0, 10).Square(10, 0, 0, 10);
            mesh.vertices[7] += new Vector3(0f, 0f, 0.5f);
            var result = mesh.Link();

            Assert.AreEqual(0, result.links.Count);
        }

        [Test]
        public void ALongFrontierGetsALinkPerSpacingNotPerEdgeSample()
        {
            var result = new MeshBuilder().Square(0, 0, 0, 30).Square(31, 0, 0, 30).Link();

            // 30 m at the 6 m spacing is five links; float rounding at exactly 6 m may add one.
            Assert.That(result.links.Count, Is.InRange(4, 6));
        }

        [Test]
        public void TheCapStopsTheSearchAndSaysSo()
        {
            var settings = new WorldNavMeshLinkSettings { maxLinks = 1, linkSpacing = 1f };
            var result = new MeshBuilder().Square(0, 0, 0, 30).Square(31, 0, 0, 30).Link(settings);

            Assert.AreEqual(1, result.links.Count);
            Assert.IsTrue(result.truncated);
        }

        [Test]
        public void TheLiveNavMeshOfTwoBakedBoxesGivesTheSameGap()
        {
            if (NavMeshGraph.AnyNavMeshLive()) Assert.Ignore("another NavMesh is live in the editor");

            var sources = new List<NavMeshBuildSource>
            {
                Box(new Vector3(5f, -0.5f, 5f), new Vector3(10f, 1f, 10f)),
                Box(new Vector3(16f, -0.5f, 5f), new Vector3(10f, 1f, 10f)),
            };
            var build = NavMesh.GetSettingsByID(0);
            build.agentRadius = 0.1f;
            build.overrideVoxelSize = true;
            build.voxelSize = 0.05f;

            var data = NavMeshBuilder.BuildNavMeshData(build, sources,
                new Bounds(new Vector3(15f, 0f, 5f), new Vector3(40f, 10f, 20f)), Vector3.zero, Quaternion.identity);
            try
            {
                var result = NavMeshAutoLinker.FindOnBakedData(data, Settings);

                Assert.GreaterOrEqual(result.gaps, 1, "the 1 m gap between the boxes should be linked");
                Assert.AreEqual(0, result.drops);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        private static NavMeshBuildSource Box(Vector3 center, Vector3 size) => new()
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
            size = size,
            area = 0,
        };
    }
}
