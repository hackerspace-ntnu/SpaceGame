using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="PackSurface"/> is the only conversion between a uv in metres and a world point,
    /// so every drag, every placement preview and every seated item goes through these three
    /// behaviours.
    /// </summary>
    public class PackSurfaceTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            host = null;
        }

        /// <summary>
        /// A surface at the origin, unrotated and unscaled, with a known size. The id and size are
        /// authored in the inspector on the real rig, so the test writes them the same way rather
        /// than making <see cref="PackSurface"/> grow a setter nothing else would use.
        /// </summary>
        private PackSurface Surface(Vector2 size)
        {
            host = new GameObject("SURF_Test");
            var surface = host.AddComponent<PackSurface>();

            var so = new UnityEditor.SerializedObject(surface);
            so.FindProperty("id").enumValueIndex = (int)PackSurfaceId.BackPanelLeft;
            so.FindProperty("size").vector2Value = size;
            so.ApplyModifiedPropertiesWithoutUndo();

            return surface;
        }

        [Test]
        public void CentreUvLandsAtTheMiddleOfTheRect()
        {
            var size = new Vector2(0.86f, 0.72f);
            PackSurface surface = Surface(size);

            Vector3 world = surface.ToWorld(size * 0.5f, 0f);

            Assert.AreEqual(0.43f, world.x, 1e-4f);
            Assert.AreEqual(0f, world.y, 1e-4f);
            Assert.AreEqual(0.36f, world.z, 1e-4f);
        }

        [Test]
        public void AUvRoundTripsThroughWorld()
        {
            PackSurface surface = Surface(new Vector2(0.86f, 0.72f));

            // Move and turn the surface: the round trip must not depend on it sitting at identity.
            host.transform.SetPositionAndRotation(new Vector3(3f, 1.2f, -4f), Quaternion.Euler(15f, 40f, 0f));

            var uv = new Vector2(0.2f, 0.55f);

            Vector2 back = surface.ToUv(surface.ToWorld(uv, 0.03f));

            Assert.AreEqual(uv.x, back.x, 1e-3f);
            Assert.AreEqual(uv.y, back.y, 1e-3f);
        }

        /// <summary>
        /// The trap the round trip above cannot catch. It goes out through <c>SafeScale</c> and
        /// straight back in through the same <c>SafeScale</c>, so it passes whether the lossyScale
        /// divide is right, wrong, or deleted — the error cancels itself.
        ///
        /// <para>
        /// This one asserts a WORLD distance in metres, which is the thing that actually has to be
        /// true. The pack's FBX arrives on the centimetre convention: mesh data 100x small under
        /// transforms 100x large, which cancels for the pack itself and multiplies anything
        /// measured against a socket under it. So a surface at lossyScale 100 is the real case, and
        /// a uv 0.50 m across it must still land 0.50 m away in world space. Without the divide the
        /// local offset would be 0.50 and the transform would put it 50 m away.
        /// </para>
        /// </summary>
        [Test]
        public void AUvIsMetresInTheWorldEvenOnACentimetreScaledRig()
        {
            PackSurface surface = Surface(new Vector2(0.86f, 0.72f));

            host.transform.localScale = Vector3.one * 100f;

            Vector3 corner = surface.ToWorld(Vector2.zero, 0f);
            Vector3 across = surface.ToWorld(new Vector2(0.50f, 0f), 0f);
            Vector3 along  = surface.ToWorld(new Vector2(0f, 0.25f), 0f);
            Vector3 lifted = surface.ToWorld(Vector2.zero, 0.03f);

            Assert.AreEqual(0.50f, Vector3.Distance(corner, across), 1e-3f,
                            "0.50 m across the surface is 0.50 m in the world, not 50 m");
            Assert.AreEqual(0.25f, Vector3.Distance(corner, along), 1e-3f);
            Assert.AreEqual(0.03f, Vector3.Distance(corner, lifted), 1e-4f,
                            "the lift off the surface is metres too");

            // The far corner of the whole face: 0.86 x 0.72 m, diagonal 1.1216 m.
            Assert.AreEqual(1.1216f, Vector3.Distance(corner, surface.ToWorld(new Vector2(0.86f, 0.72f), 0f)), 1e-3f);
        }

        [Test]
        public void AShapeHangingOverTheEdgeIsRefused()
        {
            PackSurface surface = Surface(new Vector2(0.86f, 0.72f));

            PackShape shape = PackShape.Rect(2, 2);

            Assert.IsTrue(surface.Accepts(shape, new Vector2(0.43f, 0.36f), 0f));
            Assert.IsFalse(surface.Accepts(shape, new Vector2(0.82f, 0.36f), 0f),
                           "snapped against the far edge, one of its cells is off the grid");
        }

        /// <summary>
        /// The grid is a function of the rectangle alone, and it rounds DOWN — a face 0.86 m across
        /// holds nine 90 mm cells and keeps 25 mm of hem at each end. Getting this wrong is how a
        /// face resized by half a centimetre silently loses a whole column of storage.
        /// </summary>
        [Test]
        public void TheFaceIsDividedIntoWholeCellsWithTheRemainderAsHem()
        {
            PackSurface surface = Surface(new Vector2(0.86f, 0.72f));

            Assert.AreEqual(new Vector2Int(9, 8), surface.Cells);

            Vector2 hem = PackGrid.Hem(surface.Size);

            Assert.AreEqual(0.025f, hem.x, 1e-4f, "0.86 - 9 x 0.09 = 0.05, split between both ends");
            Assert.AreEqual(0f, hem.y, 1e-4f, "0.72 is exactly eight cells");
        }
    }
}
