using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The one rule that lets an item be longer than the face it is strapped to.
    ///
    /// <para>
    /// The rack — the closed pack's back face — takes long gear the way a real pack takes skis:
    /// lashed down the middle, hanging past the top and bottom edges. Width is the real
    /// restriction, because width is what the straps have to reach around; length past the panel
    /// is just air. So a shape whose long side exceeds the rack's own column span occupies the
    /// FULL span — every cell of every column it crosses, so nothing can slide in under the
    /// overhang — and the item is drawn at true size, centred, hanging evenly off both ends.
    /// </para>
    /// <para>
    /// Only the rack, and only along its long (u) axis. The other faces keep the strict rule:
    /// the leaf is a mat things sit on, the lash line is one cell deep, and an overhang that any
    /// face allowed in any direction would stop meaning anything.
    /// </para>
    /// <para>
    /// Only RECTANGLES. A drawn mask is somebody's decision about exactly which cells an item
    /// covers, and slicing cells off a mask silently is worse than refusing it — an author who
    /// wants a masked item to overhang can draw the mask the size of the span.
    /// </para>
    /// </summary>
    public static class PackOverhang
    {
        /// <summary>May items overhang this face's u axis?</summary>
        public static bool Allowed(PackSurfaceId surface) => surface == PackSurfaceId.Rack;

        /// <summary>
        /// The cells an oriented shape actually occupies on this face: the shape itself, or the
        /// full-span block it is clamped to when it is longer than the face allows overhang on.
        ///
        /// <para>
        /// Every consumer of an oriented shape — the layout's fit test, the snap, the drag
        /// feedback — must agree about the clamp or the preview approves a spot the placement
        /// then refuses, which is why this is one function rather than a rule restated per call
        /// site.
        /// </para>
        /// </summary>
        public static PackShape Clamp(PackSurfaceId surface, Vector2 surfaceSize, PackShape oriented)
        {
            if (!Allowed(surface) || !oriented.IsRectangular) return oriented;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);
            if (grid.x <= 0 || oriented.Width <= grid.x) return oriented;

            return PackShape.Rect(grid.x, oriented.Height);
        }

        /// <summary>
        /// The cell a click means on a face that allows overhang. A click on the overhanging end
        /// of an item lands past the grid — <c>ToUv</c> projects the hit point and the point is
        /// genuinely off the panel — so along the overhang axis it is pulled back to the end cell,
        /// which the item occupies. The v axis stays strict: nothing ever hangs off it.
        /// </summary>
        public static Vector2Int ClampCell(PackSurfaceId surface, Vector2 surfaceSize, Vector2Int cell)
        {
            if (!Allowed(surface)) return cell;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);
            if (grid.x <= 0) return cell;

            return new Vector2Int(Mathf.Clamp(cell.x, 0, grid.x - 1), cell.y);
        }
    }
}
