using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Draws the cells an item occupies, as a lattice of outlined squares lying on the surface.
    ///
    /// <para>
    /// Two callers, one geometry. <see cref="BuildPlaced"/> makes a permanent child of the surface
    /// for an item already on the mat — the ring of cells the player asked to see around attached
    /// gear. The instance form is the drag preview, which redraws every frame and colours each cell
    /// separately: the free ones grey, the ones that would clash red. That per-cell split is the
    /// whole reason this exists rather than reusing the single footprint quad — one rectangle for
    /// the whole item can say "this does not fit" but never "this corner is what is in the way".
    /// </para>
    /// <para>
    /// <b>Outlines, not filled squares.</b> An item is drawn at true size on top of its cells, so a
    /// filled quad would be almost entirely hidden under the thing it is describing. A ring per
    /// cell survives being sat on. Blocked cells are filled as well, because a refusal has to be
    /// visible through whatever is causing it.
    /// </para>
    /// <para>
    /// Geometry is built in the SURFACE's local frame through
    /// <see cref="PackSurface.ToLocal"/> and the object is parented at identity, so the rig's
    /// centimetre-convention FBX scale cancels exactly the way it does for a display copy. Built in
    /// world space instead, the overlay would be a hundred metres across.
    /// </para>
    /// </summary>
    public sealed class PackGridVisual
    {
        /// <summary>Metres the overlay floats above the surface, clear of z-fighting with canvas.</summary>
        private const float Lift = 0.003f;

        /// <summary>Metres of gutter between one cell's outline and the next. Draws the lattice.</summary>
        private const float Border = 0.005f;

        /// <summary>Metres of line width in the outline itself.</summary>
        private const float Line = 0.006f;

        private const string ShaderName = "SpaceGame/PackDragTint";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BodyOnId = Shader.PropertyToID("_BodyOn");
        private static readonly int OutlineOnId = Shader.PropertyToID("_OutlineOn");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>The lattice under gear already on the mat: the rig's own webbing ochre.</summary>
        private static readonly Color PlacedTint = new(1f, 0.84f, 0.45f, 0.5f);

        /// <summary>A cell the drop would legally use.</summary>
        private static readonly Color ClearTint = new(0.45f, 0.85f, 1f, 0.55f);

        /// <summary>A cell that is off the grid or already taken.</summary>
        private static readonly Color BlockedTint = new(1f, 0.35f, 0.3f, 0.6f);

        // ── The ring around a placed item ────────────────────────────────────

        /// <summary>
        /// The cells one placed item occupies, as a child of its surface. Null when there is
        /// nothing to draw. Owned by the caller, which destroys it with the item's display copy.
        /// </summary>
        public static GameObject BuildPlaced(PackSurface surface, Vector2Int origin, PackShape oriented)
        {
            if (surface == null || oriented.IsEmpty) return null;

            var mesh = new Mesh { name = "PackGridCells", hideFlags = HideFlags.HideAndDontSave };

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    AddCell(verts, tris, surface, new Vector2Int(origin.x + x, origin.y + y), fill: false);
                }
            }

            if (tris.Count == 0)
            {
                Destroy(mesh);
                return null;
            }

            Commit(mesh, verts, tris);

            var go = new GameObject("PackGridVisual");

            go.transform.SetParent(surface.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = PlacedMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            int layer = BackpackItemVisual.ItemLayer;
            if (layer >= 0) go.layer = layer;

            return go;
        }

        private static Material placedMaterial;

        /// <summary>
        /// Shared across every placed item, because <c>RebuildVisuals</c> tears the whole display
        /// down and builds it again on any change — a material per placement would be a leak with
        /// a heartbeat.
        /// </summary>
        private static Material PlacedMaterial()
        {
            if (placedMaterial != null) return placedMaterial;

            placedMaterial = BuildMaterial("PackGridPlaced");
            placedMaterial.SetColor(ColorId, PlacedTint);

            return placedMaterial;
        }

        // ── The per-cell drag preview ────────────────────────────────────────

        private readonly Material clearMaterial;
        private readonly Material blockedMaterial;

        private GameObject clearObject;
        private GameObject blockedObject;
        private Mesh clearMesh;
        private Mesh blockedMesh;

        private readonly List<Vector3> verts = new();
        private readonly List<int> tris = new();

        public PackGridVisual()
        {
            clearMaterial = BuildMaterial("PackGridClear");
            clearMaterial.SetColor(ColorId, ClearTint);

            blockedMaterial = BuildMaterial("PackGridBlocked");
            blockedMaterial.SetColor(ColorId, BlockedTint);
        }

        /// <summary>
        /// Draw the cells this shape would use if it were dropped here, each one coloured by
        /// whether it is actually available.
        ///
        /// <para>
        /// <paramref name="ignoreItemId"/> is the item in the air, which must not count as an
        /// obstacle to itself — the same exclusion <see cref="PackLayout.CanPlace"/> takes.
        /// </para>
        /// </summary>
        public void Show(PackSurface surface, Vector2Int origin, PackShape oriented,
                         PackLayout layout, string ignoreItemId)
        {
            if (surface == null || oriented.IsEmpty || layout == null)
            {
                Hide();
                return;
            }

            Build(ref clearObject, ref clearMesh, "PackGridClearCells", clearMaterial,
                  surface, origin, oriented, layout, ignoreItemId, wantBlocked: false);

            Build(ref blockedObject, ref blockedMesh, "PackGridBlockedCells", blockedMaterial,
                  surface, origin, oriented, layout, ignoreItemId, wantBlocked: true);
        }

        public void Hide()
        {
            if (clearObject != null) clearObject.SetActive(false);
            if (blockedObject != null) blockedObject.SetActive(false);
        }

        /// <summary>Materials and meshes are instances; Unity collects neither on its own.</summary>
        public void Dispose()
        {
            Destroy(clearObject);
            Destroy(blockedObject);
            Destroy(clearMesh);
            Destroy(blockedMesh);
            Destroy(clearMaterial);
            Destroy(blockedMaterial);

            clearObject = null;
            blockedObject = null;
            clearMesh = null;
            blockedMesh = null;
        }

        private void Build(ref GameObject go, ref Mesh mesh, string name, Material material,
                           PackSurface surface, Vector2Int origin, PackShape oriented,
                           PackLayout layout, string ignoreItemId, bool wantBlocked)
        {
            verts.Clear();
            tris.Clear();

            Vector2 size = surface.Size;

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    var cell = new Vector2Int(origin.x + x, origin.y + y);

                    bool blocked = !PackGrid.OnGrid(size, cell)
                                   || !layout.CellIsFree(surface.Id, cell, ignoreItemId);

                    if (blocked != wantBlocked) continue;

                    // A cell hanging off the face has no square to draw on, so it is counted as
                    // blocked above and then skipped here: the outline stops at the edge, which is
                    // exactly the readout "you are over the side".
                    if (!PackGrid.OnGrid(size, cell)) continue;

                    AddCell(verts, tris, surface, cell, fill: wantBlocked);
                }
            }

            if (tris.Count == 0)
            {
                if (go != null) go.SetActive(false);
                return;
            }

            if (mesh == null) mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };

            Commit(mesh, verts, tris);

            if (go == null)
            {
                go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                int layer = BackpackItemVisual.ItemLayer;
                if (layer >= 0) go.layer = layer;
            }

            // Re-parented every call rather than once: a drag crosses faces, and the mesh is in
            // the surface's own frame, so the object has to follow the face it was measured on.
            go.transform.SetParent(surface.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.SetActive(true);
        }

        // ── Geometry ─────────────────────────────────────────────────────────

        /// <summary>
        /// One cell: a square annulus, plus a filled centre when <paramref name="fill"/>. Eight
        /// vertices for the ring and four more for the fill, in the surface's local frame.
        /// </summary>
        private static void AddCell(List<Vector3> verts, List<int> tris, PackSurface surface,
                                    Vector2Int cell, bool fill)
        {
            Vector2 corner = PackGrid.CornerUv(surface.Size, cell);

            float x0 = corner.x + Border;
            float y0 = corner.y + Border;
            float x1 = corner.x + PackGrid.Cell - Border;
            float y1 = corner.y + PackGrid.Cell - Border;

            if (x1 - x0 <= 2f * Line || y1 - y0 <= 2f * Line) return;

            int b = verts.Count;

            // Outer ring, then the same corners pulled in by the line width.
            verts.Add(surface.ToLocal(new Vector2(x0, y0), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1, y0), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1, y1), Lift));
            verts.Add(surface.ToLocal(new Vector2(x0, y1), Lift));

            verts.Add(surface.ToLocal(new Vector2(x0 + Line, y0 + Line), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1 - Line, y0 + Line), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1 - Line, y1 - Line), Lift));
            verts.Add(surface.ToLocal(new Vector2(x0 + Line, y1 - Line), Lift));

            for (int i = 0; i < 4; i++)
            {
                int o0 = b + i;
                int o1 = b + (i + 1) % 4;
                int i0 = b + 4 + i;
                int i1 = b + 4 + (i + 1) % 4;

                tris.Add(o0); tris.Add(i1); tris.Add(o1);
                tris.Add(o0); tris.Add(i0); tris.Add(i1);
            }

            if (!fill) return;

            int f = verts.Count;

            verts.Add(verts[b + 4]);
            verts.Add(verts[b + 5]);
            verts.Add(verts[b + 6]);
            verts.Add(verts[b + 7]);

            tris.Add(f); tris.Add(f + 2); tris.Add(f + 1);
            tris.Add(f); tris.Add(f + 3); tris.Add(f + 2);
        }

        private static void Commit(Mesh mesh, List<Vector3> verts, List<int> tris)
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// The same unlit tint material focus mode's other overlays use, with the same fallback:
        /// without the project shader the colour still reads, it just loses the draw-order control.
        /// </summary>
        private static Material BuildMaterial(string name)
        {
            Shader shader = Shader.Find(ShaderName);

            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

            material.SetFloat(BodyOnId, 1f);
            material.SetFloat(OutlineOnId, 0f);
            material.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.renderQueue = 3000;

            return material;
        }

        private static void Destroy(Object victim)
        {
            if (victim == null) return;

            if (Application.isPlaying) Object.Destroy(victim);
            else Object.DestroyImmediate(victim);
        }
    }
}
