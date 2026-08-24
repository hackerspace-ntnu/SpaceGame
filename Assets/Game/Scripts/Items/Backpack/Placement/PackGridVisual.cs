using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Draws the cells an item occupies, as a lattice of outlined squares lying on the surface.
    ///
    /// <para>
    /// Three callers, one geometry. <see cref="BuildPlaced"/> makes a permanent child of the
    /// surface for an item already on the mat — the ring of cells the player asked to see around
    /// attached gear. The instance form has two passes during a drag: <see cref="Show"/> draws the
    /// magnet-snapped ghost's own cells, legal by construction now that the search never offers an
    /// illegal spot; <see cref="ShowLattice"/> draws the WHOLE hovered face underneath it, free
    /// cells barely-there and occupied ones filled in the rig's webbing ochre, so free space reads
    /// at a glance through the gear sitting on it.
    /// </para>
    /// <para>
    /// <b>Outlines, not filled squares.</b> An item is drawn at true size on top of its cells, so a
    /// filled quad would be almost entirely hidden under the thing it is describing. A ring per
    /// cell survives being sat on.
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

        /// <summary>The rig's own webbing ochre, full strength. <see cref="PlacedTint"/> and
        /// <see cref="LatticeTakenTint"/> below are this RGB at two different alphas — by
        /// reference rather than by two matching literals, so they cannot drift apart.</summary>
        private static readonly Color WebbingOchre = new(1f, 0.84f, 0.45f, 1f);

        /// <summary>The lattice under gear already on the mat: the rig's own webbing ochre.</summary>
        private static readonly Color PlacedTint = WithAlpha(WebbingOchre, 0.5f);

        /// <summary>A cell the drop would legally use.</summary>
        private static readonly Color ClearTint = new(0.45f, 0.85f, 1f, 0.55f);

        /// <summary>A free cell of the hovered face while something is in hand: barely there.</summary>
        private static readonly Color LatticeFreeTint = new(1f, 1f, 1f, 0.10f);

        /// <summary>A cell already under placed gear: the webbing ochre, filled, readable
        /// through the item sitting on it.</summary>
        private static readonly Color LatticeTakenTint = WithAlpha(WebbingOchre, 0.30f);

        private static Color WithAlpha(Color colour, float alpha) =>
            new(colour.r, colour.g, colour.b, alpha);

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

        // ── The magnet-snapped ghost's own cells ─────────────────────────────

        private readonly Material clearMaterial;

        private GameObject clearObject;
        private Mesh clearMesh;

        /// <summary>What <see cref="Show"/> last drew, so a frame where the ghost sits on the same
        /// spot costs nothing. Reset to a surface of null by <see cref="HideGhost"/> and
        /// <see cref="Dispose"/> so the NEXT Show always rebuilds rather than trusting stale
        /// geometry a hidden pass never gets to overwrite.</summary>
        private PackSurface ghostSurface;
        private Vector2Int ghostOrigin;
        private PackShape ghostOriented;

        private readonly List<Vector3> verts = new();
        private readonly List<int> tris = new();

        // ── The drag-time lattice ────────────────────────────────────────────

        private readonly Material latticeFreeMaterial;
        private readonly Material latticeTakenMaterial;

        private GameObject latticeFreeObject;
        private GameObject latticeTakenObject;
        private Mesh latticeFreeMesh;
        private Mesh latticeTakenMesh;

        /// <summary>What the lattice currently shows, so a frame where nothing changed costs
        /// nothing. <see cref="MarkLatticeDirty"/> stales it on any layout change; a showing
        /// flag rather than an activeSelf test, because a fully-taken face leaves the free
        /// half's object deactivated even while the lattice is legitimately up.</summary>
        private PackSurface latticeSurface;
        private bool latticeShowing;
        private bool latticeDirty = true;

        public PackGridVisual()
        {
            clearMaterial = BuildMaterial("PackGridClear");
            clearMaterial.SetColor(ColorId, ClearTint);

            latticeFreeMaterial = BuildMaterial("PackLatticeFree");
            latticeFreeMaterial.SetColor(ColorId, LatticeFreeTint);

            latticeTakenMaterial = BuildMaterial("PackLatticeTaken");
            latticeTakenMaterial.SetColor(ColorId, LatticeTakenTint);

            // One queue step behind the ghost cells and the placed ring (both left at the 3000
            // BuildMaterial sets): all three share the same Lift height, so two transparent passes
            // at equal queue and equal depth blend in whichever order the renderer happens to
            // submit them — flicker, not colour. Pinning the lattice a step earlier guarantees the
            // more specific readout, "here is where THIS placement lands", always wins the pixel.
            latticeFreeMaterial.renderQueue = 2999;
            latticeTakenMaterial.renderQueue = 2999;
        }

        /// <summary>
        /// Draw the cells the magnet-snapped ghost would use. The spot is legal by
        /// construction — see <c>PackLayout.TryFindNearest</c> — so there is exactly one
        /// colour: this is "here is where it will land", not a verdict.
        /// </summary>
        public void Show(PackSurface surface, Vector2Int origin, PackShape oriented)
        {
            if (surface == null || oriented.IsEmpty)
            {
                HideGhost();
                return;
            }

            // Rebuilt only when the ghost actually moved. Compared field-by-field rather than via
            // oriented.Equals(ghostOriented): PackShape has no Equals override, so that call would
            // box through ValueType.Equals every single frame. A masked (non-rectangular) shape is
            // excluded from the early-out outright and always rebuilds — Rotated allocates a fresh
            // backing array on every call, so a same-Width/Height mask could still be a DIFFERENT
            // pattern (a rotated L keeps its bounding box but not its cells), and Width/Height alone
            // cannot tell them apart. Rectangles, the common case, have no such array to distinguish
            // and cache cleanly.
            if (surface == ghostSurface && origin == ghostOrigin &&
                oriented.IsRectangular && ghostOriented.IsRectangular &&
                oriented.Width == ghostOriented.Width && oriented.Height == ghostOriented.Height)
                return;

            ghostSurface = surface;
            ghostOrigin = origin;
            ghostOriented = oriented;

            verts.Clear();
            tris.Clear();

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    var cell = new Vector2Int(origin.x + x, origin.y + y);
                    if (!PackGrid.OnGrid(surface.Size, cell)) continue;

                    AddCell(verts, tris, surface, cell, fill: false);
                }
            }

            CommitTo(ref clearObject, ref clearMesh, "PackGridClearCells", clearMaterial, surface);
        }

        /// <summary>Something changed under the lattice — rebuild it next ShowLattice.</summary>
        public void MarkLatticeDirty() => latticeDirty = true;

        /// <summary>
        /// The hovered face's whole grid, drawn only while an item is in hand: free cells as a
        /// faint lattice, occupied cells filled in the webbing ochre so free space reads at a
        /// glance through the gear sitting on it.
        ///
        /// <para>
        /// Rebuilt only when the face or the layout changes, not per frame — up to 48 cells of
        /// ring geometry is cheap, but not so cheap it should be built sixty times a second
        /// for nothing.
        /// </para>
        /// <para>
        /// <paramref name="ignoreItemId"/> is the item in the air: its cells draw as free,
        /// because for this drag they are.
        /// </para>
        /// <para>
        /// <b>Expected overlap, not a bug.</b> A pack-drag's origin still carries its own
        /// <see cref="BuildPlaced"/> ring for as long as the drag is undecided — nothing here is
        /// optimistic, so the placed copy stays exactly where it was until the server answers — and
        /// the lattice draws that same face's cells free, because with <paramref name="ignoreItemId"/>
        /// excluded they are. The two rings sit congruent on top of each other for that one item:
        /// correct, if a reader traces both passes over the same cell and expects to see only one.
        /// </para>
        /// </summary>
        public void ShowLattice(PackSurface surface, PackLayout layout, string ignoreItemId)
        {
            if (surface == null || layout == null)
            {
                HideLattice();
                return;
            }

            if (surface == latticeSurface && !latticeDirty && latticeShowing) return;

            latticeSurface = surface;
            latticeShowing = true;
            latticeDirty = false;

            Vector2Int grid = PackGrid.CellsOn(surface.Size);

            BuildLatticeHalf(ref latticeFreeObject, ref latticeFreeMesh, "PackLatticeFree",
                             latticeFreeMaterial, surface, layout, ignoreItemId, grid,
                             wantTaken: false);
            BuildLatticeHalf(ref latticeTakenObject, ref latticeTakenMesh, "PackLatticeTaken",
                             latticeTakenMaterial, surface, layout, ignoreItemId, grid,
                             wantTaken: true);
        }

        public void HideLattice()
        {
            latticeSurface = null;
            latticeShowing = false;

            if (latticeFreeObject != null) latticeFreeObject.SetActive(false);
            if (latticeTakenObject != null) latticeTakenObject.SetActive(false);
        }

        private void BuildLatticeHalf(ref GameObject go, ref Mesh mesh, string name,
                                      Material material, PackSurface surface, PackLayout layout,
                                      string ignoreItemId, Vector2Int grid, bool wantTaken)
        {
            verts.Clear();
            tris.Clear();

            for (int y = 0; y < grid.y; y++)
            {
                for (int x = 0; x < grid.x; x++)
                {
                    var cell = new Vector2Int(x, y);

                    bool taken = !layout.CellIsFree(surface.Id, cell, ignoreItemId);
                    if (taken != wantTaken) continue;

                    AddCell(verts, tris, surface, cell, fill: wantTaken);
                }
            }

            CommitTo(ref go, ref mesh, name, material, surface);
        }

        /// <summary>Hides the ghost's own cells, and only those — see the caller in
        /// <c>PackDragController.UpdateDrag</c> for why a face with no room still keeps its
        /// lattice up.</summary>
        public void HideGhost()
        {
            // Forces the next Show to rebuild rather than trust geometry a hidden pass never
            // touched — the cheap early-out above only helps when the ghost stayed put, never
            // when it went away and might come back somewhere else.
            ghostSurface = null;

            if (clearObject != null) clearObject.SetActive(false);
        }

        /// <summary>Everything this draws, off. The exit every drag shares.</summary>
        public void Hide()
        {
            HideGhost();
            HideLattice();
        }

        /// <summary>Materials and meshes are instances; Unity collects neither on its own.</summary>
        public void Dispose()
        {
            // Resets the ghost and lattice caches, not just their objects: without this a
            // PackGridVisual that outlived its GameObjects — unlikely, but Dispose is the one
            // place that has to be paranoid about it — would answer a post-Dispose ShowLattice with
            // a silent early-out instead of rebuilding against the (destroyed) state it remembers.
            HideGhost();
            HideLattice();

            Destroy(clearObject);
            Destroy(latticeFreeObject);
            Destroy(latticeTakenObject);
            Destroy(clearMesh);
            Destroy(latticeFreeMesh);
            Destroy(latticeTakenMesh);
            Destroy(clearMaterial);
            Destroy(latticeFreeMaterial);
            Destroy(latticeTakenMaterial);

            clearObject = null;
            latticeFreeObject = null;
            latticeTakenObject = null;
            clearMesh = null;
            latticeFreeMesh = null;
            latticeTakenMesh = null;
        }

        /// <summary>Commits the scratch verts/tris into one overlay object on the surface.</summary>
        private void CommitTo(ref GameObject go, ref Mesh mesh, string name, Material material,
                              PackSurface surface)
        {
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
