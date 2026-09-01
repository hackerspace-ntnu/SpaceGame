using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Lashes a placed item to its surface with procedural strap bands: one to three thin ribbons
    /// across the item's footprint, wrapped over the item's actual silhouette the way a rubber
    /// band wraps whatever it is stretched around.
    ///
    /// <para>
    /// <b>Works for any mesh, because the mesh itself is the input.</b> The display copy's
    /// triangles are sampled directly — no colliders are ever created, so nothing here can appear
    /// in a physics query even for one frame: <see cref="PackPointer.TryHitItem"/> raycasts the
    /// pack-item layer every frame in focus mode, and a temporary MeshCollider on the copy would
    /// resolve to its surface and become a click target. With direct sampling there is no collider
    /// at any instant to guard against.
    /// </para>
    /// <para>
    /// The band shape is the <b>upper convex hull</b> of the sampled height profile, which is the
    /// physics of a taut strap stated as geometry: straight between tangent points, never dipping
    /// into a concavity it cannot reach, flat on the mat beyond the item and sunk just under the
    /// surface at its ends so no cap faces are needed.
    /// </para>
    /// <para>
    /// Built once per placement by <c>BackpackObject.RebuildVisuals</c> and torn down with the
    /// display copy via <see cref="Destroy"/> — no Update, no per-frame cost. Geometry is emitted
    /// in the SURFACE's local frame through <see cref="PackSurface.ToLocal"/> and the object
    /// parented at identity, the same pattern as <see cref="PackGridVisual"/>, so the rig's
    /// centimetre-convention FBX scale cancels instead of making every band a hundred times long.
    /// </para>
    /// </summary>
    public static class PackStrapVisual
    {
        // Every length below is metres ON THE MAT and takes PackScale.Factor with the rig: a band
        // is webbing off the rig's own lash line, and webbing that stayed 30 mm across a 1.5x pack
        // would read as string.

        /// <summary>Metres of webbing across one band.</summary>
        private static readonly float StrapWidth = PackScale.Apply(0.03f);

        /// <summary>Metres between a band's underside and its top face.</summary>
        private static readonly float StrapThickness = PackScale.Apply(0.004f);

        /// <summary>
        /// Metres of clearance a band keeps above the sampled silhouette, so it reads as lying ON
        /// the item rather than shrink-wrapped into its surface detail. Only added where the item
        /// is actually under the band — the flat run on the mat stays on the mat.
        /// </summary>
        private static readonly float StrapPad = PackScale.Apply(0.006f);

        /// <summary>
        /// Metres the band's two ends sink below the surface. An end under the mat needs no cap
        /// face and reads as webbing disappearing into the rig, which is what real lash points do.
        /// </summary>
        private static readonly float EndSink = PackScale.Apply(0.002f);

        /// <summary>
        /// Height samples along one band's span. Enough that the hull's tangent points land within
        /// a few millimetres of the true silhouette on the longest span the rig has; the hull then
        /// throws away every sample that is not a tangent, so this is a sampling density, not a
        /// vertex count.
        /// </summary>
        private const int ProfileSamples = 24;

        /// <summary>
        /// Metres the end anchors sit outside the footprint edge. Sub-visible; exists only so the
        /// anchors never share an exact coordinate with the first and last profile sample, which
        /// would make "leftmost point" ambiguous to the hull walk.
        /// </summary>
        private const float AnchorEpsilon = 1e-5f;

        /// <summary>
        /// Twice-area below which a triangle's uv projection is a line — a vertical wall. Those
        /// cannot be point-sampled (they contain no area to hit) and instead stamp their top
        /// height across the samples they span.
        /// </summary>
        private const float DegenerateArea = 1e-9f;

        /// <summary>Slack on the barycentric inside-test, so a sample exactly on a shared triangle
        /// edge is claimed by at least one of the triangles instead of falling between them.</summary>
        private const float EdgeSlack = 1e-4f;

        /// <summary>
        /// Cells of length at or below which one band is the whole lashing — 2 cells is 0.27 m of
        /// mat, a leash or a scanner, and a second strap on something that size touches the first.
        /// A count, so it does NOT take <see cref="PackScale"/>: the cell scales with the rig and
        /// the item measures in cells, so the metres this stands for move with everything else.
        /// </summary>
        private const int SingleBandCells = 2;

        /// <summary>
        /// Cells of length from which an item earns its third band — 6 cells is 0.81 m, past which
        /// two straps leave an unheld span in the middle.
        /// </summary>
        private const int ThirdBandCells = 6;

        /// <summary>
        /// The palette's strapping fabric, <c>Mat_Fabric_Canvas_Faded</c> <c>#6E6A5A</c> — the
        /// material the rig's own lashings are painted with. Deliberately NOT
        /// <see cref="PackGridVisual"/>'s webbing ochre: that is the unlit UI-overlay tint family,
        /// not a physical surface.
        /// </summary>
        private static readonly Color StrapColour = new(0.431f, 0.416f, 0.353f);

        private static Material strapMaterial;

        /// <summary>
        /// One build's two lifetimes in one hand: the band object in the hierarchy, and the mesh
        /// asset it renders. Handed back together because the two do NOT die together on their
        /// own — Unity tears the GameObject down with its parent, but a
        /// <see cref="HideFlags.HideAndDontSave"/> mesh outlives that until something destroys it
        /// by reference. A caller holding only the GameObject has nothing left to destroy the
        /// mesh through once the hierarchy has gone down first.
        /// </summary>
        public readonly struct Handle
        {
            /// <summary>The band object, parented under its surface. Null once destroyed.</summary>
            public GameObject Object { get; }

            /// <summary>The band mesh, held independently of <see cref="Object"/>.</summary>
            public Mesh Mesh { get; }

            public Handle(GameObject bands, Mesh mesh)
            {
                Object = bands;
                Mesh = mesh;
            }

            /// <summary>True when <see cref="Build"/> had nothing to strap.</summary>
            public bool IsEmpty => Object == null && Mesh == null;
        }

        /// <summary>
        /// Build the bands for the item whose display copy is <paramref name="itemVisual"/>, lying
        /// with its (already overhang-clamped) <paramref name="oriented"/> shape at
        /// <paramref name="origin"/> on <paramref name="surface"/>. Returns an empty handle when
        /// there is nothing to strap. The caller owns the result and destroys it via
        /// <see cref="Destroy"/>.
        /// </summary>
        public static Handle Build(PackSurface surface, Vector2Int origin, PackShape oriented,
                                   GameObject itemVisual)
        {
            if (surface == null || oriented.IsEmpty || itemVisual == null) return default;

            // Every triangle of the copy, as (u, v, h) in surface metres: three list entries per
            // triangle, x = u, y = v, z = height above the surface.
            var silhouette = new List<Vector3>();
            SampleGeometry(surface, itemVisual, silhouette);

            // Bands run PERPENDICULAR to the footprint's long axis — a lashing strap crosses the
            // item's short girth at stations along its length, the way skis lash to a rack. Along
            // the long axis the same straps would run the whole item and read as a net. Tie goes
            // to u so a square item is strapped the same way every time.
            bool longIsU = oriented.Width >= oriented.Height;

            int longCells = longIsU ? oriented.Width : oriented.Height;
            int crossCells = longIsU ? oriented.Height : oriented.Width;
            int longOrigin = longIsU ? origin.x : origin.y;
            int crossOrigin = longIsU ? origin.y : origin.x;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();
            var hull = new List<Vector2>();
            var heights = new float[ProfileSamples];

            // A handful of bands, spread over the item's length — never one per cell boundary,
            // which buried a ten-cell staff under nine straps and read as a net rather than a
            // lashing. The count comes from the length alone, and the stations are the even
            // fractions of it the rig's own holder art already uses: one at the middle, two at
            // 25% / 75%, three at 1/6, 1/2, 5/6. A band is never at an end, so nothing has to
            // special-case a one-cell item.
            int bandCount = BandCount(longCells);

            for (int b = 0; b < bandCount; b++)
            {
                // Cells from the footprint's near edge to this band's centre line.
                float alongCells = (b + 0.5f) / bandCount * longCells;

                // All uv arithmetic goes through PackGrid.CornerUv so the hem's centring is in it —
                // correct today, and still correct if the faces become exact cell multiples.
                float bandCoord =
                    LongAxisUv(surface.Size, longIsU, longOrigin) + alongCells * PackGrid.Cell;

                // The column the band crosses. Stations no longer land on cell boundaries, so
                // coverage is that one column's fill rather than either side of a boundary.
                int column = Mathf.Clamp(Mathf.FloorToInt(alongCells), 0, longCells - 1);

                // A band only exists where the item does: at this station, the contiguous runs of
                // cross-axis cells the shape fills. A masked shape with a gap gets one band per
                // run, each spanning only its own run.
                for (int c = 0; c < crossCells; c++)
                {
                    if (!Filled(oriented, longIsU, column, c)) continue;

                    int runStart = c;
                    while (c + 1 < crossCells && Filled(oriented, longIsU, column, c + 1)) c++;

                    // Anchors at the footprint cell-rect edges. PackShape rounds footprints UP, so
                    // the rect edge sits at or outside the silhouette and the band's flat ends
                    // always land on the mat.
                    float t0 = CrossAxisUv(surface.Size, longIsU, crossOrigin + runStart);
                    float t1 = CrossAxisUv(surface.Size, longIsU, crossOrigin + c + 1);

                    SampleProfile(silhouette, longIsU, bandCoord, t0, t1, heights);
                    BuildHull(t0, t1, heights, hull);
                    EmitBand(surface, longIsU, bandCoord, hull, verts, tris, uvs);
                }
            }

            if (tris.Count == 0) return default;

            var mesh = new Mesh { name = "PackStrapBands", hideFlags = HideFlags.HideAndDontSave };

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("PackStrapVisual");

            go.transform.SetParent(surface.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            // Shadows stay ON: unlike the flat overlays this is a physical object, the same rule
            // the holders follow. No collider, deliberately — the cursor picks items, and a strap
            // that could be hit would shadow the very item it lashes down.
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = StrapMaterial();

            // The pack-item layer, like everything else living on the mat: the focus camera's
            // depth-of-field volume is bound to it, and a strap left on the rig's own layer would
            // be the one sharp thing in a soft frame.
            int layer = BackpackItemVisual.ItemLayer;
            if (layer >= 0) go.layer = layer;

            return new Handle(go, mesh);
        }

        /// <summary>
        /// Tear down what <see cref="Build"/> made, mesh first. The mesh is built fresh per
        /// placement and Unity never collects it on its own, so it is destroyed UNCONDITIONALLY —
        /// through the handle's own reference, never read back off the GameObject. That matters
        /// on the pack's destruction: Unity offers no order between a parent's
        /// <c>OnDestroy</c> and its children going down, so the band object can already be null
        /// here — and a teardown that reached the mesh via its MeshFilter then skipped it,
        /// leaking one HideAndDontSave mesh per placed item until domain reload.
        /// </summary>
        public static void Destroy(Handle straps)
        {
            DestroyResource(straps.Mesh);
            DestroyResource(straps.Object);
        }

        // ── Footprint arithmetic ─────────────────────────────────────────────

        /// <summary>
        /// How many bands lash an item that is <paramref name="longCells"/> cells long. Short gear
        /// is held by one, most of the rack by two, and only the longest — a staff, a rifle — earns
        /// a third; more than that stops reading as lashing.
        /// </summary>
        public static int BandCount(int longCells) =>
            longCells <= SingleBandCells ? 1 : longCells < ThirdBandCells ? 2 : 3;

        /// <summary>Is the oriented shape filled at long-axis column <paramref name="longIdx"/>,
        /// cross-axis row <paramref name="crossIdx"/>?</summary>
        private static bool Filled(PackShape oriented, bool longIsU, int longIdx, int crossIdx) =>
            longIsU ? oriented[longIdx, crossIdx] : oriented[crossIdx, longIdx];

        /// <summary>The long-axis uv coordinate of an absolute cell boundary, hem included.</summary>
        private static float LongAxisUv(Vector2 surfaceSize, bool longIsU, int cell)
        {
            Vector2 corner = PackGrid.CornerUv(surfaceSize, longIsU
                ? new Vector2Int(cell, 0)
                : new Vector2Int(0, cell));

            return longIsU ? corner.x : corner.y;
        }

        /// <summary>The cross-axis uv coordinate of an absolute cell boundary, hem included.</summary>
        private static float CrossAxisUv(Vector2 surfaceSize, bool longIsU, int cell)
        {
            // The cross axis is the OTHER axis: v when the long axis is u, and vice versa.
            Vector2 corner = PackGrid.CornerUv(surfaceSize, longIsU
                ? new Vector2Int(0, cell)
                : new Vector2Int(cell, 0));

            return longIsU ? corner.y : corner.x;
        }

        // ── Silhouette sampling ──────────────────────────────────────────────

        /// <summary>
        /// Every rendered triangle of the display copy, converted to (u, v, height) in surface
        /// metres in one step: surface-local position times the surface's safe lossy scale, which
        /// is <see cref="PackSurface.ToUv"/> plus the height its doc says callers read themselves.
        /// Going through the live transform is what makes the centimetre convention cancel here
        /// exactly as it does for the copy itself.
        /// </summary>
        private static void SampleGeometry(PackSurface surface, GameObject itemVisual,
                                           List<Vector3> silhouette)
        {
            Matrix4x4 toSurface = surface.transform.worldToLocalMatrix;

            // The display scale divides back out, exactly as it does in PackSurface.ToUv, because
            // this samples the DRAWN copy and hands the result back to ToLocal, which will put the
            // enlargement in again. Left in, a band over an item on the gear wall would be built
            // from coordinates 1.06x too large and would wrap thin air beside it.
            Vector3 scale = SafeScale(surface.transform) / surface.DisplayScale;

            // The same renderer filters ItemBounds.Measure applies, so the straps wrap exactly
            // the geometry the copy renders. activeInHierarchy is safe here where ItemBounds
            // needed activeSelf walking: Build runs on a copy already seated live on its surface,
            // never one still parented to the deactivated staging object.
            foreach (MeshFilter filter in itemVisual.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                Matrix4x4 m = toSurface * filter.transform.localToWorldMatrix;

                // A mesh imported without Read/Write cannot be sampled at runtime. Its renderer's
                // local bounds, emitted as twelve triangles into the same rasterizer, degrade that
                // one part to a box profile instead of losing it entirely.
                if (mesh.isReadable) AddMesh(mesh, m, scale, silhouette);
                else AddBox(mesh.bounds, m, scale, silhouette);
            }

            foreach (SkinnedMeshRenderer skinned in
                     itemVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null || !skinned.enabled ||
                    !skinned.gameObject.activeInHierarchy)
                    continue;

                // The copy has no Animator — Strip took it — so the bake is the prefab pose, which
                // is exactly what the copy renders. Bake output is readable regardless of the
                // source asset's import settings, and bakes the transform's scale in, so only
                // position and rotation are applied on top.
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);

                Matrix4x4 m = toSurface * Matrix4x4.TRS(
                    skinned.transform.position, skinned.transform.rotation, Vector3.one);

                AddMesh(baked, m, scale, silhouette);
                DestroyResource(baked);
            }
        }

        private static void AddMesh(Mesh mesh, Matrix4x4 toSurfaceLocal, Vector3 scale,
                                    List<Vector3> silhouette)
        {
            Vector3[] positions = mesh.vertices;
            int[] indices = mesh.triangles;

            for (int i = 0; i < indices.Length; i++)
            {
                Vector3 local = toSurfaceLocal.MultiplyPoint3x4(positions[indices[i]]);

                silhouette.Add(new Vector3(local.x * scale.x, local.z * scale.z, local.y * scale.y));
            }
        }

        /// <summary>Corner index bits: 1 = +x, 2 = +y, 4 = +z. Winding is irrelevant — these
        /// triangles are only ever rasterized for height, never rendered.</summary>
        private static readonly int[] BoxTriangles =
        {
            0, 1, 3,  0, 3, 2,   4, 5, 7,  4, 7, 6,
            0, 1, 5,  0, 5, 4,   2, 3, 7,  2, 7, 6,
            0, 2, 6,  0, 6, 4,   1, 3, 7,  1, 7, 5,
        };

        private static void AddBox(Bounds bounds, Matrix4x4 toSurfaceLocal, Vector3 scale,
                                   List<Vector3> silhouette)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;

            var corners = new Vector3[8];

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));

                Vector3 local = toSurfaceLocal.MultiplyPoint3x4(corner);

                corners[i] = new Vector3(local.x * scale.x, local.z * scale.z, local.y * scale.y);
            }

            foreach (int index in BoxTriangles) silhouette.Add(corners[index]);
        }

        /// <summary>
        /// Mirrors <see cref="PackSurface"/>'s private SafeScale: absolute lossy scale with zeroes
        /// replaced by 1, so a degenerate transform cannot turn the sampling into NaNs. Duplicated
        /// because the guard is three lines and PackSurface keeps its scale handling private on
        /// purpose — everything public there speaks finished uv metres.
        /// </summary>
        private static Vector3 SafeScale(Transform t)
        {
            Vector3 s = t.lossyScale;

            return new Vector3(
                Mathf.Abs(s.x) < 1e-6f ? 1f : Mathf.Abs(s.x),
                Mathf.Abs(s.y) < 1e-6f ? 1f : Mathf.Abs(s.y),
                Mathf.Abs(s.z) < 1e-6f ? 1f : Mathf.Abs(s.z));
        }

        // ── The profile one band sees ────────────────────────────────────────

        /// <summary>
        /// The max height of the silhouette under a band's strip, sampled at
        /// <see cref="ProfileSamples"/> stations along its span and three offsets across its
        /// width — both edges and the centre, so a ridge under one edge of the webbing still
        /// lifts the whole band. Heights start at 0: where nothing is under the strip, the band
        /// lies on the mat.
        /// </summary>
        private static void SampleProfile(List<Vector3> silhouette, bool longIsU, float bandCoord,
                                          float t0, float t1, float[] heights)
        {
            for (int j = 0; j < heights.Length; j++) heights[j] = 0f;

            float span = t1 - t0;
            if (span <= 0f) return;

            float dt = span / (heights.Length - 1);
            float halfWidth = StrapWidth * 0.5f;

            for (int i = 0; i + 2 < silhouette.Count; i += 3)
            {
                Vector3 p0 = silhouette[i];
                Vector3 p1 = silhouette[i + 1];
                Vector3 p2 = silhouette[i + 2];

                // (a, b): a runs along the band's span, b across its width. The silhouette entries
                // are (u, v, h), so which uv component is which depends on the band orientation.
                float a0 = longIsU ? p0.y : p0.x, b0 = longIsU ? p0.x : p0.y;
                float a1 = longIsU ? p1.y : p1.x, b1 = longIsU ? p1.x : p1.y;
                float a2 = longIsU ? p2.y : p2.x, b2 = longIsU ? p2.x : p2.y;

                // Cheap AABB pre-cull against the strip before any barycentric work.
                float aMin = Mathf.Min(a0, Mathf.Min(a1, a2));
                float aMax = Mathf.Max(a0, Mathf.Max(a1, a2));
                float bMin = Mathf.Min(b0, Mathf.Min(b1, b2));
                float bMax = Mathf.Max(b0, Mathf.Max(b1, b2));

                if (bMax < bandCoord - halfWidth || bMin > bandCoord + halfWidth) continue;
                if (aMax < t0 || aMin > t1) continue;

                int jStart = Mathf.Max(0, Mathf.CeilToInt((aMin - t0) / dt));
                int jEnd = Mathf.Min(heights.Length - 1, Mathf.FloorToInt((aMax - t0) / dt));
                if (jStart > jEnd) continue;

                float area2 = (a1 - a0) * (b2 - b0) - (b1 - b0) * (a2 - a0);

                if (Mathf.Abs(area2) < DegenerateArea)
                {
                    // A vertical wall: no uv area to point-sample, but real height the band must
                    // clear. Its top height stamps every sample its AABB spans within the strip.
                    float top = Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z));

                    for (int j = jStart; j <= jEnd; j++)
                        if (heights[j] < top) heights[j] = top;

                    continue;
                }

                float inv = 1f / area2;

                for (int j = jStart; j <= jEnd; j++)
                {
                    float a = t0 + j * dt;

                    for (int k = -1; k <= 1; k++)
                    {
                        float b = bandCoord + k * halfWidth;

                        float w0 = ((a1 - a) * (b2 - b) - (b1 - b) * (a2 - a)) * inv;
                        float w1 = ((a2 - a) * (b0 - b) - (b2 - b) * (a0 - a)) * inv;
                        float w2 = 1f - w0 - w1;

                        if (w0 < -EdgeSlack || w1 < -EdgeSlack || w2 < -EdgeSlack) continue;

                        float h = w0 * p0.z + w1 * p1.z + w2 * p2.z;
                        if (heights[j] < h) heights[j] = h;
                    }
                }
            }
        }

        // ── The taut-band hull ───────────────────────────────────────────────

        /// <summary>
        /// The upper convex hull of the padded profile between two sunk end anchors — the shape a
        /// taut band actually takes. Monotone chain over points already sorted by t; collinear
        /// points are dropped too, so a flat run costs two rows however many samples crossed it.
        /// </summary>
        private static void BuildHull(float t0, float t1, float[] heights, List<Vector2> hull)
        {
            hull.Clear();

            PushHull(hull, new Vector2(t0 - AnchorEpsilon, -EndSink));

            float dt = (t1 - t0) / (heights.Length - 1);

            for (int j = 0; j < heights.Length; j++)
            {
                float h = heights[j] > 0f ? heights[j] + StrapPad : 0f;

                PushHull(hull, new Vector2(t0 + j * dt, h));
            }

            PushHull(hull, new Vector2(t1 + AnchorEpsilon, -EndSink));
        }

        private static void PushHull(List<Vector2> hull, Vector2 p)
        {
            while (hull.Count >= 2 &&
                   Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) >= 0f)
                hull.RemoveAt(hull.Count - 1);

            hull.Add(p);
        }

        /// <summary>Positive when a-b-c turns left. The upper hull keeps only right turns.</summary>
        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        // ── The ribbon ───────────────────────────────────────────────────────

        /// <summary>
        /// One band as a ribbon over its hull: four vertices per hull row — the top face's edges
        /// at h + thickness and the two skirt bottoms at h — quads for the top and both side
        /// skirts, and NO bottom face: it is pressed against the surface or the item, never
        /// visible, and at h = 0 it would z-fight the mat. Hull rows are used directly as ribbon
        /// rows — a taut band is straight between tangent points, so resampling would add
        /// vertices without adding shape.
        /// </summary>
        private static void EmitBand(PackSurface surface, bool longIsU, float bandCoord,
                                     List<Vector2> hull, List<Vector3> verts, List<int> tris,
                                     List<Vector2> uvs)
        {
            if (hull.Count < 2) return;

            int baseIndex = verts.Count;
            float half = StrapWidth * 0.5f;
            float along = 0f;
            Vector2 previous = hull[0];

            for (int r = 0; r < hull.Count; r++)
            {
                Vector2 p = hull[r];   // x = t along the band, y = height.

                // Texture u follows the band's real arc length over the item, so a texture later
                // stretches along the webbing instead of pooling over the climb.
                along += Vector2.Distance(previous, p);
                previous = p;

                Vector2 uvL = longIsU
                    ? new Vector2(bandCoord - half, p.x)
                    : new Vector2(p.x, bandCoord - half);
                Vector2 uvR = longIsU
                    ? new Vector2(bandCoord + half, p.x)
                    : new Vector2(p.x, bandCoord + half);

                verts.Add(surface.ToLocal(uvL, p.y + StrapThickness));   // +0 top left
                verts.Add(surface.ToLocal(uvR, p.y + StrapThickness));   // +1 top right
                verts.Add(surface.ToLocal(uvL, p.y));                    // +2 bottom left
                verts.Add(surface.ToLocal(uvR, p.y));                    // +3 bottom right

                uvs.Add(new Vector2(along, 0f));
                uvs.Add(new Vector2(along, 1f));
                uvs.Add(new Vector2(along, 0f));
                uvs.Add(new Vector2(along, 1f));
            }

            // The windings below were derived for a band running along v (longIsU). A band along u
            // maps the band frame onto the surface axes with the opposite handedness, so every
            // triangle flips — one boolean rather than a second set of index tables.
            bool flip = !longIsU;

            for (int r = 0; r + 1 < hull.Count; r++)
            {
                int i0 = baseIndex + r * 4;
                int i1 = i0 + 4;

                // Top face, normal out of the surface.
                AddTri(tris, flip, i0 + 0, i1 + 1, i0 + 1);
                AddTri(tris, flip, i0 + 0, i1 + 0, i1 + 1);

                // Left skirt, normal toward -width.
                AddTri(tris, flip, i0 + 2, i1 + 0, i0 + 0);
                AddTri(tris, flip, i0 + 2, i1 + 2, i1 + 0);

                // Right skirt, normal toward +width.
                AddTri(tris, flip, i0 + 3, i0 + 1, i1 + 1);
                AddTri(tris, flip, i0 + 3, i1 + 1, i1 + 3);
            }
        }

        private static void AddTri(List<int> tris, bool flip, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(flip ? c : b);
            tris.Add(flip ? b : c);
        }

        // ── Material ─────────────────────────────────────────────────────────

        /// <summary>
        /// One shared lit material for every band on every pack, never destroyed per rebuild —
        /// <c>RebuildVisuals</c> tears the display down on any change, and a material per
        /// placement would be a leak with a heartbeat. Same runtime-material pattern as the cave
        /// decorations: URP Lit with a Standard fallback, colour set through both names.
        /// </summary>
        private static Material StrapMaterial()
        {
            if (strapMaterial != null) return strapMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            strapMaterial = new Material(shader)
            {
                name = "PackStrap",
                hideFlags = HideFlags.HideAndDontSave,
            };

            strapMaterial.SetColor("_BaseColor", StrapColour);
            strapMaterial.color = StrapColour;
            strapMaterial.SetFloat("_Smoothness", 0.1f);

            return strapMaterial;
        }

        private static void DestroyResource(Object victim)
        {
            if (victim == null) return;

            if (Application.isPlaying) Object.Destroy(victim);
            else Object.DestroyImmediate(victim);
        }
    }
}
