using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Builds the mesh every tuft in a meadow shares: a few broad, leaf-shaped blades fanned
    /// around a centre. Each blade is widest a third of the way up and tapers to a point, is
    /// folded along its spine so its two halves catch the light differently, and bows outward as
    /// it rises. Authored at height 1 — the shader scales it per tuft and reads the vertex height
    /// straight off <c>positionOS.y</c> for the colour ramp.
    ///
    /// Four detail levels: the near tuft has every blade in several folded segments; the mid
    /// tuft has the same blades as one folded segment each; the far tuft has a few flat
    /// two-triangle blades, which is all a tuft is at 20 m; the distant tuft is a single
    /// triangle that GrassCull.compute turns to face the camera.
    /// </summary>
    public static class GrassTuftMesh
    {
        private const float GoldenAngle = 2.39996f;
        private const float MinBladeHeight = 0.7f;

        // The leaf outline: sin over [Start, 1] of half a turn is 0.7 at the root, 1 at a third
        // of the way up and 0 at the tip.
        private const float OutlineStart = 0.25f;

        public static Mesh BuildNear(GrassFieldProfile profile)
        {
            if (profile.TuftMesh != null)
                return Normalised(profile.TuftMesh);
            return Build(profile.BladesPerTuft, profile.BladeSegments, profile.BladeWidth, profile.BladeFold,
                         profile, "GrassTuft_Near");
        }

        public static Mesh BuildMid(GrassFieldProfile profile)
        {
            if (profile.TuftMesh != null)
                return Normalised(profile.TuftMesh);
            return Build(profile.BladesPerTuft, 1, profile.BladeWidth, profile.BladeFold, profile, "GrassTuft_Mid");
        }

        public static Mesh BuildFar(GrassFieldProfile profile) =>
            Build(profile.FarBladesPerTuft, 1, profile.BladeWidth * profile.FarBladeWidthScale, 0f,
                  profile, "GrassTuft_Far");

        /// <summary>
        /// One upright triangle in the XY plane, facing +Z — the side the compute pass turns
        /// toward the camera. Its blade seed sits mid-range, so its colour is the tuft's average.
        /// </summary>
        public static Mesh BuildDistant(GrassFieldProfile profile)
        {
            float halfWidth = profile.DistantTuftWidth * 0.5f;
            var mesh = new Mesh { name = "GrassTuft_Distant", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-halfWidth, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(halfWidth, 0f, 0f) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Build(int blades, int segments, float width, float fold, GrassFieldProfile profile,
                                  string name)
        {
            // Each row across a blade is left, spine, right; the tip is one vertex.
            int verticesPerBlade = segments * 3 + 1;
            int indicesPerBlade = (segments * 4 - 2) * 3;
            var vertices = new Vector3[blades * verticesPerBlade];
            var seeds = new Vector2[vertices.Length];
            var indices = new int[blades * indicesPerBlade];

            for (int b = 0; b < blades; b++)
            {
                float angle = b * GoldenAngle;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-outward.z, 0f, outward.x);
                Vector3 root = outward * (profile.TuftSpread * Mathf.Sqrt((b + 0.5f) / blades));

                // Fixed per-blade variation, so the tuft looks hand-made without being random.
                float height = Mathf.Lerp(MinBladeHeight, 1f, Fract(b * 0.618f + 0.3f));
                float lean = profile.BladeLean * Mathf.Lerp(0.5f, 1f, Fract(b * 0.382f + 0.25f));

                int v = b * verticesPerBlade;
                // Each blade's own colour seed, read by the shader from UV0.x.
                var seed = new Vector2(Fract(b * 0.754877f + 0.13f), 0f);
                for (int k = 0; k < verticesPerBlade; k++)
                    seeds[v + k] = seed;
                for (int s = 0; s < segments; s++)
                {
                    float t = s / (float)segments;
                    Vector3 spine = BladePoint(root, outward, height, lean, t);
                    float halfWidth = width * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Lerp(OutlineStart, 1f, t));
                    Vector3 crease = outward * (fold * halfWidth);
                    vertices[v + s * 3] = spine - side * halfWidth;
                    vertices[v + s * 3 + 1] = spine + crease;
                    vertices[v + s * 3 + 2] = spine + side * halfWidth;
                }
                vertices[v + segments * 3] = BladePoint(root, outward, height, lean, 1f);

                int i = b * indicesPerBlade;
                for (int s = 0; s < segments - 1; s++, i += 12)
                {
                    int row = v + s * 3;
                    int next = row + 3;
                    SetTriangle(indices, i, row, next, row + 1);
                    SetTriangle(indices, i + 3, row + 1, next, next + 1);
                    SetTriangle(indices, i + 6, row + 1, next + 1, row + 2);
                    SetTriangle(indices, i + 9, row + 2, next + 1, next + 2);
                }
                int last = v + (segments - 1) * 3;
                int tip = v + segments * 3;
                SetTriangle(indices, i, last, tip, last + 1);
                SetTriangle(indices, i + 3, last + 1, tip, last + 2);
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.uv = seeds;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The blade rises and bows outward, most of the bow near the tip.</summary>
        private static Vector3 BladePoint(Vector3 root, Vector3 outward, float height, float lean, float t) =>
            root + outward * (lean * height * t * t) + Vector3.up * (height * t);

        /// <summary>
        /// A copy of an authored tuft scaled so its tallest point is at height 1, which is the
        /// contract with the shader. Its normals are kept: the shader blends them with the ground
        /// normal by the material's Blade Facing amount.
        /// </summary>
        private static Mesh Normalised(Mesh authored)
        {
            Vector3[] vertices = authored.vertices;
            float top = authored.bounds.max.y;
            if (top <= 0f)
            {
                Debug.LogError($"[GrassField] Tuft mesh '{authored.name}' has no height above its origin; " +
                               "author it Y-up with the origin at the root.", authored);
                top = 1f;
            }
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] /= top;

            var mesh = new Mesh { name = authored.name + "_Normalised", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.normals = authored.normals;
            mesh.uv = authored.uv;
            mesh.triangles = authored.triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SetTriangle(int[] indices, int at, int a, int b, int c)
        {
            indices[at] = a;
            indices[at + 1] = b;
            indices[at + 2] = c;
        }

        private static float Fract(float x) => x - Mathf.Floor(x);
    }
}
