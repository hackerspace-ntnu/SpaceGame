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
    /// tuft has the same blades as one folded triangle each, of the same area; the far tuft has a
    /// few flat two-triangle blades, which is all a tuft is at 20 m; the distant tuft is a flat
    /// row of spikes, one per far blade, that GrassCull.compute turns to face the camera. Each level but the last also
    /// carries the next level's shape (UV1 positions, UV2 normals), which the shader morphs into
    /// before the switch, so the switch itself shows nothing.
    /// </summary>
    public static class GrassTuftMesh
    {
        private const float GoldenAngle = 2.39996f;
        private const float MinBladeHeight = 0.7f;

        // The leaf outline: sin over [Start, 1] of half a turn is 0.7 at the root, 1 at a third
        // of the way up and 0 at the tip.
        private const float OutlineStart = 0.25f;

        // A one-segment blade is a triangle with the leaf's area, so a coarser level covers as
        // much ground as the leaf it replaces: the leaf outline averages 0.7245 of its widest.
        private const float TriangleHalfBase = 0.7245f;

        // UV1 holds each vertex's position on the next level's tuft and UV2 that tuft's normal
        // there; StylizedGrass morphs toward them as the switch to that level nears.
        private const int MorphPositionChannel = 1;
        private const int MorphNormalChannel = 2;

        // Below this summed face area a vertex has no surface to take a normal from.
        private const float MinNormalArea = 1e-12f;

        public static Mesh BuildNear(GrassFieldProfile profile)
        {
            if (profile.TuftMesh != null)
                return Normalised(profile.TuftMesh);
            return Build(Near(profile), Mid(profile), profile, "GrassTuft_Near");
        }

        public static Mesh BuildMid(GrassFieldProfile profile)
        {
            if (profile.TuftMesh != null)
                return Normalised(profile.TuftMesh);
            return Build(Mid(profile), Far(profile), profile, "GrassTuft_Mid");
        }

        public static Mesh BuildFar(GrassFieldProfile profile) =>
            Build(Far(profile), null, profile, "GrassTuft_Far");

        /// <summary>
        /// A row of flat spikes in the XY plane, facing +Z — the side the compute pass turns
        /// toward the camera — one per far blade, with that blade's height and colour seed, so the
        /// far tuft morphs into it blade for blade and the jagged top keeps a grass-like rhythm.
        /// It is lit as the ground (normal up): turned to the camera, its own facing would light
        /// the far field brighter or darker depending on which way the camera looks.
        /// </summary>
        public static Mesh BuildDistant(GrassFieldProfile profile)
        {
            int blades = Far(profile).Blades;
            var vertices = new Vector3[blades * 3];
            var seeds = new Vector2[vertices.Length];
            var indices = new int[vertices.Length];
            for (int b = 0; b < blades; b++)
            {
                int v = b * 3;
                vertices[v] = SpikeVertex(profile, blades, b, 0f, -1);
                vertices[v + 1] = SpikeVertex(profile, blades, b, 1f, 0);
                vertices[v + 2] = SpikeVertex(profile, blades, b, 0f, 1);
                seeds[v] = seeds[v + 1] = seeds[v + 2] = BladeSeed(b);
                SetTriangle(indices, v, v, v + 1, v + 2);
            }
            Vector3[] normals = Filled(Vector3.up, vertices.Length);
            var mesh = new Mesh { name = "GrassTuft_Distant", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = seeds;
            mesh.triangles = indices;
            // The last level never morphs; it carries its own shape so every level has the same inputs.
            SetMorph(mesh, vertices, normals);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The shape of every blade of one detail level.</summary>
        private readonly struct Level
        {
            public readonly int Blades;
            public readonly int Segments;
            public readonly float Width;
            public readonly float Fold;

            public Level(int blades, int segments, float width, float fold)
            {
                Blades = blades;
                Segments = segments;
                Width = width;
                Fold = fold;
            }
        }

        private static Level Near(GrassFieldProfile profile) =>
            new Level(profile.BladesPerTuft, profile.BladeSegments, profile.BladeWidth, profile.BladeFold);

        // The near blades as one folded segment each.
        private static Level Mid(GrassFieldProfile profile) =>
            new Level(profile.BladesPerTuft, 1, profile.BladeWidth, profile.BladeFold);

        private static Level Far(GrassFieldProfile profile) =>
            new Level(profile.FarBladesPerTuft, 1, profile.BladeWidth * profile.FarBladeWidthScale, 0f);

        /// <param name="next">The level this one morphs into; null morphs into the distant spikes.</param>
        private static Mesh Build(Level level, Level? next, GrassFieldProfile profile, string name)
        {
            // Each row across a blade is left, spine, right; the tip is one vertex.
            int segments = level.Segments;
            int verticesPerBlade = segments * 3 + 1;
            int indicesPerBlade = (segments * 4 - 2) * 3;
            var vertices = new Vector3[level.Blades * verticesPerBlade];
            var morph = new Vector3[vertices.Length];
            var seeds = new Vector2[vertices.Length];
            var indices = new int[level.Blades * indicesPerBlade];

            for (int b = 0; b < level.Blades; b++)
            {
                int v = b * verticesPerBlade;
                for (int k = 0; k < verticesPerBlade; k++)
                    seeds[v + k] = BladeSeed(b);
                for (int s = 0; s < segments; s++)
                {
                    float t = s / (float)segments;
                    for (int side = -1; side <= 1; side++)
                        PlaceVertex(v + s * 3 + side + 1, t, side);
                }
                PlaceVertex(v + segments * 3, 1f, 0);

                int i = b * indicesPerBlade;
                for (int s = 0; s < segments - 1; s++, i += 12)
                {
                    int row = v + s * 3;
                    int above = row + 3;
                    SetTriangle(indices, i, row, above, row + 1);
                    SetTriangle(indices, i + 3, row + 1, above, above + 1);
                    SetTriangle(indices, i + 6, row + 1, above + 1, row + 2);
                    SetTriangle(indices, i + 9, row + 2, above + 1, above + 2);
                }
                int last = v + (segments - 1) * 3;
                int tip = v + segments * 3;
                SetTriangle(indices, i, last, tip, last + 1);
                SetTriangle(indices, i + 3, last + 1, tip, last + 2);

                void PlaceVertex(int index, float t, int side)
                {
                    vertices[index] = BladeVertex(profile, level, b, t, side);
                    morph[index] = next.HasValue
                        ? MorphVertex(profile, level, next.Value, b, t, side)
                        : SpikeVertex(profile, level.Blades, b, t, side);
                }
            }

            Vector3[] normals = VertexNormals(vertices, indices, null);
            // The distant spikes are lit as the ground; a blade that narrows to nothing keeps its own.
            Vector3[] morphNormals = next.HasValue
                ? VertexNormals(morph, indices, normals)
                : Filled(Vector3.up, vertices.Length);

            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = seeds;
            mesh.triangles = indices;
            SetMorph(mesh, morph, morphNormals);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A point on blade <paramref name="b"/> of <paramref name="level"/>, <paramref name="t"/>
        /// of the way up, on its left edge (-1), spine (0) or right edge (1). Several segments
        /// trace the bowed leaf, widest a third of the way up; one segment is a straight triangle
        /// of the same area.
        /// </summary>
        private static Vector3 BladeVertex(GrassFieldProfile profile, Level level, int b, float t, int side)
        {
            float angle = b * GoldenAngle;
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var across = new Vector3(-outward.z, 0f, outward.x);
            Vector3 root = outward * (profile.TuftSpread * Mathf.Sqrt((b + 0.5f) / level.Blades));

            float height = BladeHeight(b);
            float lean = profile.BladeLean * Mathf.Lerp(0.5f, 1f, Fract(b * 0.382f + 0.25f));

            bool triangle = level.Segments == 1;
            Vector3 spine = triangle
                ? Vector3.Lerp(BladePoint(root, outward, height, lean, 0f), BladePoint(root, outward, height, lean, 1f), t)
                : BladePoint(root, outward, height, lean, t);
            float halfWidth = triangle
                ? level.Width * TriangleHalfBase * (1f - t)
                : level.Width * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Lerp(OutlineStart, 1f, t));
            return side == 0
                ? spine + outward * (level.Fold * halfWidth)
                : spine + across * (side * halfWidth);
        }

        /// <summary>
        /// Where a vertex of <paramref name="level"/> goes on <paramref name="next"/>: the same
        /// point of the same blade, or — for a blade the next level drops — its own spine, so the
        /// blade narrows away.
        /// </summary>
        private static Vector3 MorphVertex(GrassFieldProfile profile, Level level, Level next, int b, float t, int side) =>
            b < next.Blades
                ? BladeVertex(profile, next, b, t, side)
                : BladeVertex(profile, new Level(level.Blades, level.Segments, 0f, 0f), b, t, side);

        /// <summary>
        /// A point on spike <paramref name="b"/> of the distant tuft's row of <paramref name="blades"/>,
        /// <paramref name="t"/> of the way up its blade's height, on its left edge (-1), centre (0)
        /// or right edge (1). The spikes stand side by side, so together they never overlap.
        /// </summary>
        private static Vector3 SpikeVertex(GrassFieldProfile profile, int blades, int b, float t, int side)
        {
            float halfRow = profile.DistantTuftWidth * 0.5f;
            float left = Mathf.Lerp(-halfRow, halfRow, b / (float)blades);
            float right = Mathf.Lerp(-halfRow, halfRow, (b + 1f) / blades);
            float centre = (left + right) * 0.5f;
            float edge = side < 0 ? left : side > 0 ? right : centre;
            return new Vector3(Mathf.Lerp(edge, centre, t), BladeHeight(b) * t, 0f);
        }

        // Fixed per-blade variation, so the tuft looks hand-made without being random; the same
        // blade index keeps its height and colour at every detail level.
        private static float BladeHeight(int b) => Mathf.Lerp(MinBladeHeight, 1f, Fract(b * 0.618f + 0.3f));

        // Each blade's own colour seed, read by the shader from UV0.x.
        private static Vector2 BladeSeed(int b) => new Vector2(Fract(b * 0.754877f + 0.13f), 0f);

        /// <summary>The blade rises and bows outward, most of the bow near the tip.</summary>
        private static Vector3 BladePoint(Vector3 root, Vector3 outward, float height, float lean, float t) =>
            root + outward * (lean * height * t * t) + Vector3.up * (height * t);

        /// <summary>
        /// Area-weighted vertex normals, computed the same way for a level and for the shape it
        /// morphs into so the light does not jump at the switch. A vertex whose triangles have
        /// no area — a blade narrowed to nothing — takes <paramref name="fallback"/>'s normal.
        /// </summary>
        private static Vector3[] VertexNormals(Vector3[] vertices, int[] indices, Vector3[] fallback)
        {
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }
            for (int i = 0; i < normals.Length; i++)
                normals[i] = fallback == null || normals[i].sqrMagnitude > MinNormalArea
                    ? normals[i].normalized
                    : fallback[i];
            return normals;
        }

        private static void SetMorph(Mesh mesh, Vector3[] positions, Vector3[] normals)
        {
            mesh.SetUVs(MorphPositionChannel, positions);
            mesh.SetUVs(MorphNormalChannel, normals);
        }

        private static Vector3[] Filled(Vector3 value, int count)
        {
            var filled = new Vector3[count];
            for (int i = 0; i < count; i++)
                filled[i] = value;
            return filled;
        }

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
            if (authored.normals.Length != vertices.Length)
                mesh.RecalculateNormals();
            // No other level shares an authored tuft's blades, so it morphs into itself.
            SetMorph(mesh, vertices, mesh.normals);
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
