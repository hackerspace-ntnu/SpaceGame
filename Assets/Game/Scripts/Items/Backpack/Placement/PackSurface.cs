using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// One flat, rectangular region of the deployed pack that items can be laid on. Sits on a
    /// <c>SURF_</c> empty in the rig.
    ///
    /// <para>
    /// Local axes: <b>+X is width, +Z is depth, +Y is out of the surface.</b> The transform's
    /// origin is the rect's <c>(0,0)</c> corner, so a uv — always metres, never normalised — runs
    /// from <c>(0,0)</c> up to <see cref="Size"/>.
    /// </para>
    /// <para>
    /// <b>The rectangle carries a grid.</b> <see cref="PackGrid"/> divides it into whole cells of
    /// the rig's own webbing pitch, rounding down and centring the leftover as a hem, and every
    /// item on the face snaps to those cells. The surface itself stores nothing about it — the
    /// grid is a function of <see cref="Size"/> alone, so a face resized in the inspector re-cells
    /// itself with no second field to keep in step.
    /// </para>
    /// <para>
    /// <b>Two frames, and only one of them is written down anywhere.</b> The LOGICAL frame is
    /// <see cref="Size"/> and its cells: it is what a placement, a save file and the wire are all
    /// written in, and it never moves. The DRAWN frame is that times <see cref="DisplayScale"/>,
    /// and it is what <see cref="ToLocal"/>, <see cref="ToWorld"/> and <see cref="ToUv"/> speak.
    /// The two are the same thing on the rig, where the scale is 1; the ship's gear wall is drawn
    /// 6% larger than it reasons.
    /// </para>
    /// </summary>
    public sealed class PackSurface : MonoBehaviour
    {
        [Tooltip("Which face of the deployed rig this is. Persisted and sent on the wire.")]
        [SerializeField] private PackSurfaceId id;

        // The default is a whole 8 x 8 cells rather than a round number of metres, so a face added
        // by hand starts with zero hem and re-sizes itself if PackGrid.Cell ever moves again. The
        // wiring scripts overwrite it for every shipped face; this is only what a fresh component
        // reads in the inspector.
        [Tooltip("Metres. X spans local +X, Y spans local +Z. Author it as a whole number of " +
                 "PackGrid cells, or the grid is inset by a hem and the face loses a row.")]
        [SerializeField] private Vector2 size = new(8f * PackGrid.Cell, 8f * PackGrid.Cell);

        [Tooltip("Leave EMPTY for an ordinary face, which takes anything that fits. A face with " +
                 "entries is a socket rather than a shelf and accepts only these items.")]
        [SerializeField] private InventoryItem[] acceptsOnly = new InventoryItem[0];

        public PackSurfaceId Id => id;
        public Vector2 Size => size;

        /// <summary>The items this face is reserved for, or empty when it takes anything.</summary>
        public IReadOnlyList<InventoryItem> AcceptsOnly => acceptsOnly;

        /// <summary>
        /// Is this face willing to hold <paramref name="item"/> at all? The question that comes
        /// BEFORE the geometry — a reserved face refuses a perfectly well-fitting item.
        ///
        /// <para>
        /// Only one face in the game is reserved: the rig's centre back strip, which is the
        /// oxygen bottle's socket and is plumbed into whatever stands there
        /// (<see cref="PackSurfaceId.BackPanelCentre"/>). Everything else answers true for
        /// everything, which is why this is a cheap early-out and not a rule callers have to
        /// think about.
        /// </para>
        /// <para>
        /// Reference first, then ID — the same test <c>PackContainer</c> uses everywhere else,
        /// because every slot resolves through the registry to the same Resources asset the
        /// inspector points at, and an ID compare is the fallback for a runtime copy.
        /// </para>
        /// </summary>
        public bool AcceptsItem(InventoryItem item)
        {
            if (!AllowsPlacementOf(item)) return false;

            if (acceptsOnly == null || acceptsOnly.Length == 0) return true;
            if (item == null) return false;

            foreach (InventoryItem allowed in acceptsOnly)
            {
                if (allowed == null) continue;
                if (allowed == item) return true;

                if (!string.IsNullOrEmpty(allowed.ID) && allowed.ID == item.ID) return true;
            }

            return false;
        }

        /// <summary>
        /// The same question asked of an item ID, for the paths that hold one rather than an
        /// asset — a placement off the wire, or a record out of a save.
        /// </summary>
        public bool AcceptsItemId(string itemId)
        {
            if (!AllowsPlacementOf(string.IsNullOrEmpty(itemId) ? null : Registry<InventoryItem>.Get(itemId)))
                return false;

            if (acceptsOnly == null || acceptsOnly.Length == 0) return true;
            if (string.IsNullOrEmpty(itemId)) return false;

            foreach (InventoryItem allowed in acceptsOnly)
                if (allowed != null && allowed.ID == itemId) return true;

            return false;
        }

        /// <summary>
        /// The other half of "does this face take this item" — can the ITEM be here at all,
        /// regardless of what this face is itself willing to hold.
        ///
        /// <para>
        /// Most gear is sized for a hand and fits wherever its footprint fits. A few items are
        /// sized against ONE specific face instead (<see cref="ItemGrip.PackSize"/>'s own note) —
        /// the wing pack fills the rack edge to edge — and <see cref="PackOverhang"/>'s back-panel
        /// rule, deliberately permissive on both axes for realistic gear like a bedroll, would
        /// otherwise clamp that same oversized item down to a 3x6 panel and let it be stowed
        /// somewhere it was never sized for. <see cref="ItemGrip.ConfinedToSurfaces"/> is empty for
        /// every item but that one, so this is a cheap early-out for everything else — same shape
        /// as the whitelist beside it, the other direction.
        /// </para>
        /// </summary>
        private bool AllowsPlacementOf(InventoryItem item)
        {
            if (item == null || item.itemPrefab == null) return true;

            ItemGrip grip = item.itemPrefab.GetComponentInChildren<ItemGrip>(true);
            if (grip == null) return true;

            IReadOnlyList<PackSurfaceId> confined = grip.ConfinedToSurfaces;
            if (confined == null || confined.Count == 0) return true;

            for (int i = 0; i < confined.Count; i++)
                if (confined[i] == id) return true;

            return false;
        }

        /// <summary>Whole cells this face holds, across and along. See <see cref="PackGrid"/>.</summary>
        public Vector2Int Cells => PackGrid.CellsOn(size);

        /// <summary>
        /// How much bigger than its own grid this face is DRAWN, from the container it belongs to.
        /// 1 on the rig, <see cref="PackScale.WallDisplay"/> on the ship's gear wall.
        ///
        /// <para>
        /// <b>Nothing above this line knows about it.</b> <see cref="Size"/>, <see cref="Cells"/>
        /// and <see cref="Accepts"/> are the LOGICAL frame — the one a placement, a save file and
        /// the wire are all written in — and they must stay exactly as they were whatever this
        /// says. Only <see cref="ToLocal"/>, <see cref="ToWorld"/> and <see cref="ToUv"/>, the
        /// three functions that convert a uv into somewhere on screen, apply it. That is what lets
        /// the wall be enlarged with no save version and no migration: the drawing changed and the
        /// arithmetic did not.
        /// </para>
        /// <para>
        /// Walked up to on first use rather than pushed in from the container, and cached, because
        /// there is no moment at which a push would be reliable — a container is routinely built
        /// by <c>Instantiate</c> or <c>AddComponent</c> outside play mode, where no <c>Awake</c>
        /// runs, and <c>PackContainer.ResolvedSurfaces</c> refuses to cache for exactly that
        /// reason. Faces are not re-parented here; a face that ever is must call
        /// <see cref="ForgetContainer"/>.
        /// </para>
        /// </summary>
        public float DisplayScale
        {
            get
            {
                if (!containerResolved)
                {
                    container = GetComponentInParent<PackContainer>(true);
                    containerResolved = true;
                }

                return container != null ? container.DisplayScale : 1f;
            }
        }

        /// <summary>Drop the cached container, for a face moved under a different one.</summary>
        public void ForgetContainer() => containerResolved = false;

        private PackContainer container;
        private bool containerResolved;

        /// <summary>
        /// The surface-local offset a uv sits at, lifted <paramref name="heightAboveSurface"/>
        /// metres along the surface's own normal.
        ///
        /// <para>
        /// The uv is in finished, on-screen metres, so the surface's own scale has to come out
        /// before the transform puts it back. It is not 1: the pack's FBX arrives on the centimetre
        /// convention, mesh data 100x small under transforms 100x large, which cancels for the pack
        /// itself and multiplies anything measured against a socket under it. Without this divide a
        /// uv 0.4 m across the panel lands 40 m away.
        /// </para>
        /// <para>
        /// Public because anything building a MESH under this transform — the cell overlay — needs
        /// vertices in exactly this frame, and re-deriving the divide in a second place is how the
        /// two drift apart.
        /// </para>
        /// </summary>
        public Vector3 ToLocal(Vector2 uv, float heightAboveSurface)
        {
            Vector3 s = SafeScale();

            // The display scale goes in BEFORE the lossy scale comes out, and it goes onto the
            // height as well as the uv, or the frame is not a similarity and an item lying on the
            // face is drawn bigger than the gap it is drawn in. It is not the same quantity as the
            // divide beside it: the divide undoes the model's own units, this is the deliberate
            // enlargement of a whole container. See DisplayScale.
            float d = DisplayScale;

            return new Vector3(uv.x * d / s.x, heightAboveSurface * d / s.y, uv.y * d / s.z);
        }

        /// <summary>
        /// The world point a uv sits at, lifted <paramref name="heightAboveSurface"/> metres along
        /// the surface's own normal.
        /// </summary>
        public Vector3 ToWorld(Vector2 uv, float heightAboveSurface) =>
            transform.TransformPoint(ToLocal(uv, heightAboveSurface));

        /// <summary>
        /// Where a world point falls on the surface, in metres from the <c>(0,0)</c> corner. The
        /// height above the surface is dropped; callers that want it read the local Y themselves.
        /// </summary>
        public Vector2 ToUv(Vector3 worldPoint)
        {
            Vector3 s = SafeScale();
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float d = DisplayScale;

            // The exact mirror of ToLocal: multiply the scale back in and divide the display scale
            // back out, so a point picked off the DRAWN board comes back as the uv of the cell the
            // player is looking at. Drop the divide and the wall's aim lands short of the
            // crosshair by 6% of the way across the board — a wall that looks right and cannot be
            // pointed at.
            return new Vector2(local.x * s.x / d, local.z * s.z / d);
        }

        /// <summary>
        /// World rotation for an item lying flat at <paramref name="yaw"/> degrees, matching the
        /// sense of <see cref="PackPlacement.Yaw"/>.
        /// </summary>
        public Quaternion WorldRotation(float yaw)
        {
            // A placement's yaw turns uv +X toward uv +Y, which here is local +X toward local +Z.
            // Unity's Y rotation turns +Z toward +X, the other way round — hence the negation.
            return transform.rotation * Quaternion.Euler(0f, -yaw, 0f);
        }

        /// <summary>
        /// Does this shape, dropped here, lie wholly on the surface's grid? The placement question
        /// minus the clash test — <see cref="PackLayout.CanPlace"/> is the one that asks both.
        /// </summary>
        public bool Accepts(PackShape shape, Vector2 uv, float yaw)
        {
            PackShape oriented = shape.Rotated(PackGrid.QuarterTurns(yaw));

            if (oriented.IsEmpty) return false;

            Vector2Int origin = PackGrid.BlockOrigin(size, uv, oriented.Size);

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    if (!PackGrid.OnGrid(size, new Vector2Int(origin.x + x, origin.y + y))) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Absolute lossy scale, with zeroes replaced by 1 so a degenerate transform cannot turn a
        /// placement into a NaN.
        /// </summary>
        private Vector3 SafeScale()
        {
            Vector3 s = transform.lossyScale;

            return new Vector3(
                Mathf.Abs(s.x) < 1e-6f ? 1f : Mathf.Abs(s.x),
                Mathf.Abs(s.y) < 1e-6f ? 1f : Mathf.Abs(s.y),
                Mathf.Abs(s.z) < 1e-6f ? 1f : Mathf.Abs(s.z));
        }

        /// <summary>Draw the rect and its cells, so the region can be authored by eye.</summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 a = ToWorld(Vector2.zero, 0f);
            Vector3 b = ToWorld(new Vector2(size.x, 0f), 0f);
            Vector3 c = ToWorld(size, 0f);
            Vector3 d = ToWorld(new Vector2(0f, size.y), 0f);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);

            // The lattice items actually snap to, inset by its hem. Drawn because the difference
            // between the rectangle and the grid is exactly the thing that is easy to get wrong
            // when a face is resized — a face 5 mm narrower can lose a whole column.
            Vector2Int cells = Cells;
            Vector2 hem = PackGrid.Hem(size);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.35f);

            for (int x = 0; x <= cells.x; x++)
            {
                float u = hem.x + x * PackGrid.Cell;

                Gizmos.DrawLine(ToWorld(new Vector2(u, hem.y), 0f),
                                ToWorld(new Vector2(u, hem.y + cells.y * PackGrid.Cell), 0f));
            }

            for (int y = 0; y <= cells.y; y++)
            {
                float v = hem.y + y * PackGrid.Cell;

                Gizmos.DrawLine(ToWorld(new Vector2(hem.x, v), 0f),
                                ToWorld(new Vector2(hem.x + cells.x * PackGrid.Cell, v), 0f));
            }

            // A stub along the normal, so it is obvious which way is "out of" the surface.
            Vector3 centre = ToWorld(size * 0.5f, 0f);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawLine(centre, ToWorld(size * 0.5f, 0.1f));
        }
    }
}
