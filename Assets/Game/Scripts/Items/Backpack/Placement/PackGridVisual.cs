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
    /// attached gear. The instance form has two passes while an item is in hand:
    /// <see cref="Show"/> draws the held item's own cells, green where the placement is legal and
    /// red where it is not; <see cref="ShowLattice"/> draws the WHOLE hovered face underneath it,
    /// free cells barely-there and occupied ones filled in the rig's webbing ochre, so free space
    /// reads at a glance through the gear sitting on it.
    /// </para>
    /// <para>
    /// <b>Outlines for the lattice and the placed ring; filled quads for the ghost.</b> The lattice
    /// and the placed ring lie UNDER an item drawn at true size, so a filled quad there would be
    /// almost entirely hidden by the thing it is describing — a ring per cell survives being sat
    /// on. The ghost is the exception the other way round: the carried copy is lifted clear of the
    /// surface and depth-tested like anything else (see <see cref="PackHandVisuals"/>), so a solid
    /// quad lying on the surface is hidden exactly where the copy covers it and reads as whole
    /// verdict colour everywhere around the silhouette — behind the item, never painted over it.
    /// See <see cref="AddGhostCell"/> and the queue note in the constructor.
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

        /// <summary>
        /// Metres of gutter left on each side of a GHOST cell's filled quad, so the footprint still
        /// reads as a grid of cells rather than one solid slab.
        ///
        /// <para>
        /// Deliberately not <see cref="Border"/>. Inset by that 5 mm, a 90 mm cell drew as an 80 mm
        /// mark and the highlight read visibly smaller than the rig's own webbing cell it is
        /// supposed to be naming. At 2 mm the quad is 98% of the real cell — the same size as the
        /// backpack's grid for every practical purpose — while adjacent cells still stop short of
        /// touching, which also means coplanar neighbours never share an edge to z-fight over.
        /// </para>
        /// </summary>
        private const float GhostGutter = 0.002f;

        /// <summary>
        /// Render queue for the ghost's two verdict materials: one step past the placed ring's
        /// 3000, which is itself a step past the lattice's 2999. All three passes sit at the same
        /// <see cref="Lift"/> with depth writes off, so at equal depth the queue is the only thing
        /// deciding the pixel — see the constructor for the full ordering argument.
        /// </summary>
        private const int GhostQueue = 3001;

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

        /// <summary>A cell the placement would legally use: a solid green fill of the whole cell,
        /// lying on the surface UNDER the carried copy. The copy is lifted and depth-tested, so it
        /// hides exactly the cells it covers and the verdict reads as whole colour around its
        /// silhouette. The alpha is strong rather than a wash, because the fill no longer has to
        /// be seen through the item — the item occludes it — it has to be SEEN.</summary>
        private static readonly Color LegalTint = new(0.38f, 0.92f, 0.45f, 0.5f);

        /// <summary>
        /// A cell the placement is refused on — clashing with placed gear, or hanging off an edge
        /// this face does not allow overhang on. Same solid full-cell fill as
        /// <see cref="LegalTint"/>, under the carried copy, at the same alpha for the same
        /// reason.
        ///
        /// Drawn on the WHOLE footprint rather than only the offending cells. The question the
        /// player is asking is "can this go here", which has one answer for the whole item; a
        /// footprint that was part green and part red would read as a partial placement, which is
        /// not a thing that can happen.
        ///
        /// <para>
        /// <b>Known gap, not fixed here.</b> The cell loop in <see cref="Show"/> skips any cell that
        /// falls off-grid, on the reasoning that the refusal is still carried by every OTHER cell of
        /// the footprint reading red. That reasoning has no cell left to lean on for a 1x1 item: a
        /// cursor in the hem band can put its single-cell origin one column outside the grid, the
        /// loop skips the only cell there is, and the player sees neither colour. Left for a later
        /// task to cure at the controller level, not by touching the cell loop here.
        /// </para>
        /// </summary>
        private static readonly Color RefusedTint = new(1f, 0.30f, 0.28f, 0.5f);

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

        // ── The held item's own cells ────────────────────────────────────────

        private readonly Material legalMaterial;
        private readonly Material refusedMaterial;

        private GameObject ghostObject;
        private Mesh ghostMesh;

        /// <summary>What <see cref="Show"/> last drew, so a frame where the ghost sits on the same
        /// spot costs nothing. Reset to a surface of null by <see cref="HideGhost"/> and
        /// <see cref="Dispose"/> so the NEXT Show always rebuilds rather than trusting stale
        /// geometry a hidden pass never gets to overwrite.</summary>
        private PackSurface ghostSurface;
        private Vector2Int ghostOrigin;
        private PackShape ghostOriented;

        /// <summary>Which of the two tints the cached geometry is currently painted with. Part of
        /// the early-out key, because legality can flip without the ghost moving a pixel — another
        /// player placing something under it does exactly that.</summary>
        private bool ghostLegal;

        private readonly List<Vector3> verts = new();
        private readonly List<int> tris = new();

        // ── The carry-time lattice ───────────────────────────────────────────

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
            legalMaterial = BuildMaterial("PackGridLegal");
            legalMaterial.SetColor(ColorId, LegalTint);

            refusedMaterial = BuildMaterial("PackGridRefused");
            refusedMaterial.SetColor(ColorId, RefusedTint);

            // The ghost's two verdict materials stay depth-tested, exactly as BuildMaterial hands
            // them out. The carried copy renders normally in the item's own opaque materials,
            // lifted CarryLift off the surface (see PackHandVisuals), so it writes depth ABOVE
            // these cells: every pixel the copy covers fails the cells' depth test, and the
            // verdict colour survives only around the silhouette — solid green or red BEHIND the
            // item, which is the whole readout. The previous design drew the cells ZTest-Always
            // over a grey-painted copy instead, and was imperceptible by construction: a
            // low-alpha wash on top of the very thing it was describing.
            legalMaterial.renderQueue = GhostQueue;
            refusedMaterial.renderQueue = GhostQueue;

            latticeFreeMaterial = BuildMaterial("PackLatticeFree");
            latticeFreeMaterial.SetColor(ColorId, LatticeFreeTint);

            latticeTakenMaterial = BuildMaterial("PackLatticeTaken");
            latticeTakenMaterial.SetColor(ColorId, LatticeTakenTint);

            // One queue step behind the placed ring (left at the 3000 BuildMaterial sets): both
            // share the same Lift height, so two transparent passes at equal queue and equal depth
            // blend in whichever order the renderer happens to submit them — flicker, not colour.
            // Pinning the lattice a step earlier guarantees the more specific readout always wins
            // the pixel. The ghost sits one step past the ring again at GhostQueue, so where the
            // held item's own cells land on either, the verdict wins the pixel.
            latticeFreeMaterial.renderQueue = 2999;
            latticeTakenMaterial.renderQueue = 2999;
        }

        /// <summary>
        /// Draw the cells the held item would occupy, green when the placement is legal and red
        /// when it is not.
        ///
        /// <para>
        /// This is the whole refusal readout. There is no message and no cursor change: the cells
        /// the player is aiming at say yes or no. A click on red turns the item rather than doing
        /// nothing; a SYMMETRIC shape, whose quarter turn occupies the very same cells, has no
        /// turn to offer and gets a timed refusal flash on the held copy instead — this readout
        /// still carries the verdict either way, only the response to a refused click differs.
        /// </para>
        /// </summary>
        public void Show(PackSurface surface, Vector2Int origin, PackShape oriented, bool legal)
        {
            if (surface == null || oriented.IsEmpty)
            {
                HideGhost();
                return;
            }

            // Rebuilt only when the ghost actually moved or changed verdict. Compared field-by-field
            // rather than via oriented.Equals(ghostOriented): PackShape has no Equals override, so
            // that call would box through ValueType.Equals every single frame. A masked
            // (non-rectangular) shape is excluded from the early-out outright and always rebuilds —
            // Rotated allocates a fresh backing array on every call, so a same-Width/Height mask
            // could still be a DIFFERENT pattern (a rotated L keeps its bounding box but not its
            // cells), and Width/Height alone cannot tell them apart. Rectangles, the common case,
            // have no such array to distinguish and cache cleanly.
            if (surface == ghostSurface && origin == ghostOrigin && legal == ghostLegal &&
                oriented.IsRectangular && ghostOriented.IsRectangular &&
                oriented.Width == ghostOriented.Width && oriented.Height == ghostOriented.Height)
                return;

            ghostSurface = surface;
            ghostOrigin = origin;
            ghostOriented = oriented;
            ghostLegal = legal;

            verts.Clear();
            tris.Clear();

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    var cell = new Vector2Int(origin.x + x, origin.y + y);
                    if (!PackGrid.OnGrid(surface.Size, cell)) continue;

                    AddGhostCell(verts, tris, surface, cell);
                }
            }

            CommitTo(ref ghostObject, ref ghostMesh, "PackGridGhostCells",
                     legal ? legalMaterial : refusedMaterial, surface);
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
        /// because for this carry they are.
        /// </para>
        /// <para>
        /// <b>Expected overlap, not a bug.</b> The held item's origin still carries its own
        /// <see cref="BuildPlaced"/> ring for as long as the placement is undecided — nothing here
        /// is optimistic, so the placed copy stays exactly where it was until the server answers —
        /// and the lattice draws that same face's cells free, because with
        /// <paramref name="ignoreItemId"/> excluded they are. The two rings sit congruent on top of
        /// each other for that one item: correct, if a reader traces both passes over the same cell
        /// and expects to see only one.
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

        /// <summary>Hides the ghost's own cells, and only those — <see cref="Hide"/> and
        /// <see cref="Dispose"/> route through here, and <see cref="ShowLattice"/>'s free/taken
        /// lattice is untouched. <see cref="Show"/>'s <c>oriented.IsEmpty</c> guard also lands
        /// here, but purely defensively: <see cref="PackOverhang.Clamp"/> never returns an empty
        /// shape and no shape for a real item is empty, so nothing reaches it today.</summary>
        public void HideGhost()
        {
            // Forces the next Show to rebuild rather than trust geometry a hidden pass never
            // touched — the cheap early-out above only helps when the ghost stayed put, never
            // when it went away and might come back somewhere else.
            ghostSurface = null;

            if (ghostObject != null) ghostObject.SetActive(false);
        }

        /// <summary>Everything this draws, off. Called every frame the cursor is off a face while
        /// something is held, not only when the carry ends.</summary>
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

            Destroy(ghostObject);
            Destroy(latticeFreeObject);
            Destroy(latticeTakenObject);
            Destroy(ghostMesh);
            Destroy(latticeFreeMesh);
            Destroy(latticeTakenMesh);
            Destroy(legalMaterial);
            Destroy(refusedMaterial);
            Destroy(latticeFreeMaterial);
            Destroy(latticeTakenMaterial);

            ghostObject = null;
            latticeFreeObject = null;
            latticeTakenObject = null;
            ghostMesh = null;
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

            MeshRenderer renderer;

            if (go == null)
            {
                go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                int layer = BackpackItemVisual.ItemLayer;
                if (layer >= 0) go.layer = layer;
            }
            else
            {
                renderer = go.GetComponent<MeshRenderer>();
            }

            // Assigned on every commit, not only on the first: the ghost swaps between the legal
            // and refused materials on the same object, and a material set once at construction
            // would leave it stuck on whichever verdict happened to be first. The two lattice
            // halves pass the same material every time, so for them this is a harmless no-op —
            // one caller needs the write, so all three get it, rather than threading a "did the
            // verdict change" flag down from Show alone.
            renderer.sharedMaterial = material;

            // Re-parented every call rather than once: carrying the held item crosses faces, and
            // the mesh is in the surface's own frame, so the object has to follow the face it was
            // measured on.
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

        /// <summary>
        /// One GHOST cell: a single quad covering the cell's whole footprint bar
        /// <see cref="GhostGutter"/> on each side, in the surface's local frame.
        ///
        /// <para>
        /// Separate from <see cref="AddCell"/> rather than another flag on it, because the two
        /// shapes answer different questions. <see cref="AddCell"/>'s ring is inset by
        /// <see cref="Border"/> and hollowed by <see cref="Line"/> so it can be read THROUGH an
        /// item sitting on it; the ghost lies UNDER the lifted carried copy and is hidden by it
        /// wherever they overlap, so it states the footprint at full size as a solid fill and
        /// lets the depth test do the cutting-out.
        /// </para>
        /// </summary>
        private static void AddGhostCell(List<Vector3> verts, List<int> tris, PackSurface surface,
                                         Vector2Int cell)
        {
            Vector2 corner = PackGrid.CornerUv(surface.Size, cell);

            float x0 = corner.x + GhostGutter;
            float y0 = corner.y + GhostGutter;
            float x1 = corner.x + PackGrid.Cell - GhostGutter;
            float y1 = corner.y + PackGrid.Cell - GhostGutter;

            if (x1 <= x0 || y1 <= y0) return;

            int b = verts.Count;

            verts.Add(surface.ToLocal(new Vector2(x0, y0), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1, y0), Lift));
            verts.Add(surface.ToLocal(new Vector2(x1, y1), Lift));
            verts.Add(surface.ToLocal(new Vector2(x0, y1), Lift));

            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
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
