using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The lattice every item on the pack snaps to, and the arithmetic that turns a uv in metres
    /// into a cell index and back.
    ///
    /// <para>
    /// <b>The cell is 90 mm, and it is a measurement of the rig rather than a number somebody
    /// liked.</b> <c>expedition_rig.py</c> builds the back panel's webbing ladder with its rungs at
    /// <c>s = 0.090, 0.180, 0.270, 0.360, 0.450, 0.540</c> — a 90 mm pitch — and lays the lash
    /// line's two webbing runs at <c>RAIL_Y = (-0.805, -0.715)</c>, which is the same 90 mm again
    /// from a completely different part of the model. Those are the only two places the rig states
    /// a rung spacing, and they agree.
    /// </para>
    /// <para>
    /// The rig's coarser anchor fields are all close to twice it, which is what makes 90 mm the
    /// pitch rather than one of them: the rack ladder's rungs are 185 mm apart
    /// (<c>RACK_RUNGS</c>), the leaf's grommet field is 200 x 190 mm, the wings' is 260 x 170 mm.
    /// Halved, those are 92.5, 100, 95, 130 and 85 mm — 90 mm sits inside that cluster, so a cell
    /// is either one rung or (for the grommet fields) half a hole, and no field is more than about
    /// 6% out. Anything much larger cannot express the ladder; anything much smaller makes the
    /// smallest item on the roster nine cells long.
    /// </para>
    /// <para>
    /// <b>One global cell, not one per surface.</b> Seven cell sizes would mean an item's authored
    /// shape meant a different physical size on every face, so a mask drawn for the leaf would be a
    /// different object on the rack — which defeats the point of authoring a shape at all. The cost
    /// is that no face divides evenly, and each one therefore rounds DOWN to whole cells:
    /// </para>
    /// <list type="table">
    /// <item><description>BackPanelLeft / Right, 0.26 x 0.50 m -> 2 x 5 cells</description></item>
    /// <item><description>Leaf, 0.78 x 0.50 m -> 8 x 5</description></item>
    /// <item><description>WingLeft / Right, 0.38 x 0.40 m -> 4 x 4</description></item>
    /// <item><description>LongGoods, 1.60 x 0.14 m -> 17 x 1</description></item>
    /// <item><description>Rack, 0.80 x 0.60 m -> 8 x 6</description></item>
    /// </list>
    /// <para>
    /// 157 cells over the rig, covering 77% of the seven rectangles. The remainder becomes a
    /// <see cref="Hem"/>: the grid is CENTRED on its surface rather than pushed into the (0,0)
    /// corner, so the leftover is split evenly on both sides and reads as the inset border the
    /// surface rectangles already are. On the back panel that lands exactly right — 2 columns of
    /// 90 mm is 180 mm, and the ladder's two vertical webbing tapes are at x = 0.196 and 0.378,
    /// i.e. 182 mm apart. The grid IS the ladder there.
    /// </para>
    /// <para>
    /// Nothing here allocates or touches UnityEngine beyond <see cref="Vector2"/> and
    /// <see cref="Mathf"/>, so the EditMode tests drive it as plain C#.
    /// </para>
    /// </summary>
    public static class PackGrid
    {
        /// <summary>Metres. One rung of the rig's webbing ladder. See the class note.</summary>
        public const float Cell = 0.09f;

        /// <summary>
        /// Slack when counting whole cells, so a surface authored at an exact multiple of
        /// <see cref="Cell"/> is not robbed of its last column by a float that landed at 4.999998.
        /// </summary>
        private const float Slack = 1e-4f;

        /// <summary>How many whole cells fit on a surface, rounding DOWN on both axes.</summary>
        public static Vector2Int CellsOn(Vector2 surfaceSize) => new(
            Mathf.Max(0, Mathf.FloorToInt(surfaceSize.x / Cell + Slack)),
            Mathf.Max(0, Mathf.FloorToInt(surfaceSize.y / Cell + Slack)));

        /// <summary>
        /// Metres of surface left over after the whole cells, halved — the border the grid is
        /// inset by on each side. Never negative.
        /// </summary>
        public static Vector2 Hem(Vector2 surfaceSize)
        {
            Vector2Int cells = CellsOn(surfaceSize);

            return new Vector2(
                Mathf.Max(0f, (surfaceSize.x - cells.x * Cell) * 0.5f),
                Mathf.Max(0f, (surfaceSize.y - cells.y * Cell) * 0.5f));
        }

        /// <summary>The (0,0) corner of one cell, in metres from the surface's own (0,0) corner.</summary>
        public static Vector2 CornerUv(Vector2 surfaceSize, Vector2Int cell) =>
            Hem(surfaceSize) + new Vector2(cell.x * Cell, cell.y * Cell);

        /// <summary>The middle of one cell, in metres from the surface's own (0,0) corner.</summary>
        public static Vector2 CentreUv(Vector2 surfaceSize, Vector2Int cell) =>
            CornerUv(surfaceSize, cell) + new Vector2(Cell * 0.5f, Cell * 0.5f);

        /// <summary>
        /// The middle of a block of <paramref name="size"/> cells whose lowest cell is
        /// <paramref name="origin"/>. This is what goes into <see cref="PackPlacement.Uv"/>, which
        /// is why the save and the wire never had to learn what a cell is: a snapped uv already
        /// names one.
        /// </summary>
        public static Vector2 BlockCentreUv(Vector2 surfaceSize, Vector2Int origin, Vector2Int size) =>
            CornerUv(surfaceSize, origin) + new Vector2(size.x * Cell * 0.5f, size.y * Cell * 0.5f);

        /// <summary>
        /// The lowest cell a block of <paramref name="size"/> cells lands on when its middle is
        /// dropped at <paramref name="uv"/>. May be off the grid; callers test that themselves.
        /// </summary>
        public static Vector2Int BlockOrigin(Vector2 surfaceSize, Vector2 uv, Vector2Int size)
        {
            Vector2 hem = Hem(surfaceSize);

            return new Vector2Int(
                Mathf.RoundToInt((uv.x - hem.x - size.x * Cell * 0.5f) / Cell),
                Mathf.RoundToInt((uv.y - hem.y - size.y * Cell * 0.5f) / Cell));
        }

        /// <summary>
        /// The nearest legal uv for a block of this many cells.
        ///
        /// <para>
        /// <b>Idempotent, and that is load-bearing rather than tidy.</b> A placement is snapped
        /// when it goes into the layout, saved snapped, sent snapped, and snapped again by whoever
        /// reads it back — so if a second pass could move an item by half a cell, an item would
        /// walk across the pack one save at a time. It cannot: the round-trip through
        /// <see cref="BlockOrigin"/> of a uv this function produced recovers the same integer.
        /// </para>
        /// </summary>
        public static Vector2 Snap(Vector2 surfaceSize, Vector2 uv, Vector2Int size) =>
            BlockCentreUv(surfaceSize, BlockOrigin(surfaceSize, uv, size), size);

        /// <summary>
        /// The cell a point falls in. Outside the grid this returns an out-of-range index rather
        /// than clamping — a click on the hem is a click on nothing, not a click on the edge cell.
        /// </summary>
        public static Vector2Int CellAt(Vector2 surfaceSize, Vector2 uv)
        {
            Vector2 hem = Hem(surfaceSize);

            return new Vector2Int(
                Mathf.FloorToInt((uv.x - hem.x) / Cell),
                Mathf.FloorToInt((uv.y - hem.y) / Cell));
        }

        /// <summary>Is this cell on the surface at all?</summary>
        public static bool OnGrid(Vector2 surfaceSize, Vector2Int cell)
        {
            Vector2Int cells = CellsOn(surfaceSize);

            return cell.x >= 0 && cell.y >= 0 && cell.x < cells.x && cell.y < cells.y;
        }

        /// <summary>
        /// Quarter turns, 0..3, for a yaw in degrees.
        ///
        /// <para>
        /// A grid has four orientations and no others, so the free system's 24-degree wheel notches
        /// and its 15/30/45-degree first-fit search are gone. Nothing is lost that mattered: the
        /// one item that needed a diagonal was the 1.35 m LaserStaff, and
        /// <see cref="PackSurfaceId.LongGoods"/> was built to take it square on.
        /// </para>
        /// </summary>
        public static int QuarterTurns(float yaw) => ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;

        /// <summary>The yaw a placement is actually stored at: 0, 90, 180 or 270.</summary>
        public static float SnapYaw(float yaw) => QuarterTurns(yaw) * 90f;
    }
}
