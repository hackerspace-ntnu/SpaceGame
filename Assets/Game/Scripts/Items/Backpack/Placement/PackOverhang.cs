using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The one rule that lets an item be bigger than the face it is strapped to.
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
    /// The two back panels take gear the way a bedroll rides under a rucksack lid: lashed to the
    /// outside with open air past every edge, so they allow overhang on BOTH axes. They are the
    /// smallest wired faces on the rig — a strict rule there would refuse most real gear
    /// outright, which makes them decoration, not storage. The clamp works per axis exactly as
    /// on the rack: each overhung axis occupies the panel's full span on that axis, so nothing
    /// slides in under the hanging part.
    /// </para>
    /// <para>
    /// Every other face keeps the strict rule: the leaf is a mat things sit on, the lash line is
    /// one cell deep, and an overhang that any face allowed in any direction would stop meaning
    /// anything.
    /// </para>
    /// <para>
    /// Only RECTANGLES. A drawn mask is somebody's decision about exactly which cells an item
    /// covers, and slicing cells off a mask silently is worse than refusing it — an author who
    /// wants a masked item to overhang can draw the mask the size of the span.
    /// </para>
    /// </summary>
    public static class PackOverhang
    {
        /// <summary>
        /// Which axes of this face items may overhang, u then v.
        ///
        /// <para>
        /// Keyed on the face's IDENTITY, never its size: the rule is about what the straps of
        /// that particular face can hold onto, and a face that is resized keeps its nature. The
        /// rack takes overhang along u only — its v span is what the lashing reaches around; the
        /// back panels take it on both axes — see the class note; everything else is strict.
        /// </para>
        /// </summary>
        public static (bool u, bool v) Axes(PackSurfaceId surface) => surface switch
        {
            PackSurfaceId.Rack           => (true,  false),
            PackSurfaceId.BackPanelLeft  => (true,  true),
            PackSurfaceId.BackPanelRight => (true,  true),
            _                            => (false, false),
        };

        /// <summary>
        /// The cells an oriented shape actually occupies on this face: the shape itself, or the
        /// block it is clamped to — per axis — when it is longer than the face allows overhang on.
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
            (bool u, bool v) = Axes(surface);
            if ((!u && !v) || !oriented.IsRectangular) return oriented;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);

            int w = u && grid.x > 0 ? Mathf.Min(oriented.Width, grid.x)  : oriented.Width;
            int h = v && grid.y > 0 ? Mathf.Min(oriented.Height, grid.y) : oriented.Height;

            return w == oriented.Width && h == oriented.Height ? oriented : PackShape.Rect(w, h);
        }

        /// <summary>
        /// The cell a click means on a face that allows overhang. A click on the overhanging end
        /// of an item lands past the grid — <c>ToUv</c> projects the hit point and the point is
        /// genuinely off the panel — so along each axis that allows overhang it is pulled back to
        /// the end cell, which the item occupies. A strict axis stays strict: off its edge is off
        /// the pack.
        /// </summary>
        public static Vector2Int ClampCell(PackSurfaceId surface, Vector2 surfaceSize, Vector2Int cell)
        {
            (bool u, bool v) = Axes(surface);
            if (!u && !v) return cell;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);

            return new Vector2Int(
                u && grid.x > 0 ? Mathf.Clamp(cell.x, 0, grid.x - 1) : cell.x,
                v && grid.y > 0 ? Mathf.Clamp(cell.y, 0, grid.y - 1) : cell.y);
        }
    }
}
