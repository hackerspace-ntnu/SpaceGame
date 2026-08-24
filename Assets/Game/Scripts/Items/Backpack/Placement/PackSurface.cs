using UnityEngine;

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
    /// the rig's own 90 mm webbing pitch, rounding down and centring the leftover as a hem, and
    /// every item on the face snaps to those cells. The surface itself stores nothing about it —
    /// the grid is a function of <see cref="Size"/> alone, so a face resized in the inspector
    /// re-cells itself with no second field to keep in step.
    /// </para>
    /// </summary>
    public sealed class PackSurface : MonoBehaviour
    {
        [Tooltip("Which face of the deployed rig this is. Persisted and sent on the wire.")]
        [SerializeField] private PackSurfaceId id;

        [Tooltip("Metres. X spans local +X, Y spans local +Z.")]
        [SerializeField] private Vector2 size = new(0.86f, 0.72f);

        public PackSurfaceId Id => id;
        public Vector2 Size => size;

        /// <summary>Whole cells this face holds, across and along. See <see cref="PackGrid"/>.</summary>
        public Vector2Int Cells => PackGrid.CellsOn(size);

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

            return new Vector3(uv.x / s.x, heightAboveSurface / s.y, uv.y / s.z);
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

            // The mirror of ToLocal: multiply the scale back in to get metres again.
            return new Vector2(local.x * s.x, local.z * s.z);
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
