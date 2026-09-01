using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How much bigger the physical inventory is than the rig it was first measured off.
    ///
    /// <para>
    /// <b>One number, applied to a whole world.</b> On 2026-09-01 the pack was enlarged uniformly:
    /// the cell, every surface rectangle, the rig's own geometry, the ship's gear wall, the size
    /// every item is drawn at on the mat and the focus camera's standoff all grew by
    /// <see cref="Factor"/> together. Because it is a <em>similarity</em> transform — one factor on
    /// every length, none on any count — the pack holds exactly the cells it held before (255 on
    /// the rig), every item occupies exactly the cells it occupied before, and every authored
    /// <see cref="PackShape"/> mask stays valid. Nothing about capacity moved. What moved is how
    /// much of the screen the thing fills in focus mode, which is the whole point: the gear is
    /// easier to tell apart and easier to aim at.
    /// </para>
    /// <para>
    /// The ship's gear wall is the one place a COUNT did move, and this factor is why: enlarged
    /// in place its fitting came out 8.46 x 4.95 m, and the lander's aft room does not have that.
    /// Its grid was re-cut from 60 x 30 to 30 x 22 the same day — 1800 cells to 660 — in
    /// <c>InventoryWallBuilder.SurfaceCellsAcross</c>/<c>Up</c> and in <c>inventory_wall.py</c>.
    /// That is a decision about a room, not about this factor, and nothing here knows about it.
    /// </para>
    /// <para>
    /// <b>Why a factor rather than 30 re-typed numbers.</b> Half of the lengths involved are baked
    /// into assets a generator owns — the <c>.blend</c>, the FBX, ten item prefabs rebuilt wholesale
    /// by their builder scripts — and re-typing each of them means the enlargement is only as
    /// complete as the last person's list. The lengths that are <em>authored</em> (a surface
    /// rectangle, the cell) are written at their new value directly, because they are read by eye
    /// off a model; the lengths that are <em>derived</em> from authored item data
    /// (<see cref="ItemGrip.PackSize"/>) go through <see cref="Apply"/> at the one place they are
    /// turned into on-mat metres, so an item cannot be missed and a builder cannot silently undo it.
    /// </para>
    /// <para>
    /// <b>Saves written before the enlargement are in the old frame.</b> A placement's uv is metres,
    /// so every uv in a pre-<c>v3</c> payload is two-thirds of where it belongs. <c>PackSaveCodec</c>
    /// multiplies those uvs by <see cref="Factor"/> on load rather than letting first-fit rearrange
    /// the player's gear behind their back — see its <c>version</c> field.
    /// </para>
    /// <para>
    /// Nothing here touches UnityEngine beyond <see cref="Vector2"/>, so the EditMode tests drive it
    /// as plain C#.
    /// </para>
    /// </summary>
    public static class PackScale
    {
        /// <summary>
        /// The enlargement. Every length in the physical inventory is this many times the length
        /// the pre-2026-09-01 rig was built at.
        /// </summary>
        public const float Factor = 1.5f;

        /// <summary>
        /// The cell before the enlargement, in metres: the pitch of the webbing ladder on the rig
        /// as it was originally modelled.
        ///
        /// <para>
        /// Kept because it is the frame every save file written before the enlargement is in, and
        /// because it is the number <see cref="PackGrid.Cell"/> is derived from — a test asserts
        /// the two still agree, rather than the derivation being done in <c>const</c> arithmetic
        /// whose last bit nobody can predict.
        /// </para>
        /// </summary>
        public const float LegacyCell = 0.09f;

        /// <summary>
        /// How much bigger than its own logical frame the ship's gear wall is DRAWN — 1.219.
        ///
        /// <para>
        /// <b>This is not <see cref="Factor"/> and it is not the same kind of number.</b>
        /// <see cref="Factor"/> is a similarity transform of the whole physical inventory, counts
        /// and all: it moved the cell, so 255 cells stayed 255 cells of a bigger size.
        /// <c>WallDisplay</c> moves nothing but pixels. The wall's face is still 30 x 22 cells of
        /// <see cref="PackGrid.Cell"/>, still 4.05 x 2.97 m of stored uv, still 660 cells; what
        /// this multiplies is only the mapping from a uv to the point on screen it is drawn at,
        /// in <see cref="PackSurface.ToLocal"/> and back out in <see cref="PackSurface.ToUv"/>.
        /// Nothing it touches is persisted or sent, so a save written at any value of this loads
        /// onto byte-identically the same cells at any other. See <c>PackSurface.DisplayScale</c>.
        /// </para>
        /// <para>
        /// <b>1.219 exceeds what the lander's aft room geometrically allows, deliberately.</b>
        /// The room's own ceiling on this number is <b>1.065</b>, and that is not a soft limit:
        /// the fitting is 3.870 m tall (all-mesh bounds of <c>inventory_wall.blend</c>, z 0 ..
        /// 3.870), and rays cast up from the deck over its footprint against the ship's BAKED
        /// COLLISION, at <c>PlayerShipBuilder.WallRibClearance</c>, find only 4.372 m of headroom
        /// — capped by an arch-rib buttress (<c>Cube.020</c> to starboard, <c>Cube.007</c> to
        /// port), not by the deckhead, which is 4.79-4.87 m and is not what the wall meets. With
        /// the 0.25 m gap <c>PlayerShipTests.PlayerShip_InventoryWallStopsShortOfTheOverhead</c>
        /// requires, the budget is 4.372 - 0.25 = 4.122 m, so 4.122 / 3.870 = 1.065.
        /// <para>
        /// At 1.219 the fitting is 4.717 m tall and stands roughly <b>0.35 m THROUGH the
        /// buttress</b>. This was asked for on 2026-09-01 with that intersection stated and
        /// understood, and it is a decision about how the room should look, not an oversight —
        /// which is why the geometric tests that guard the gap are left FAILING rather than
        /// relaxed. They are still telling the truth; the truth is now an accepted cost. Do not
        /// "fix" them by widening the tolerance: if the intersection is ever unwanted, the lever
        /// is this number (back to 1.065 or below), a shorter grid
        /// (<c>InventoryWallBuilder.SurfaceCellsUp</c>, 22 rows to 18 buys the height back at 120
        /// cells), or a taller aft room.
        /// </para>
        /// </para>
        /// <para>
        /// <b>The model carries the same 1.219 and must keep carrying it.</b>
        /// <c>inventory_wall_scale.py</c> scales the geometry by exactly this, because the drawn
        /// board and the drawn uvs have to agree: the bay dividers are six cells apart and the
        /// webbing is on a two-cell pitch, so a model left behind puts every line the player drops
        /// gear onto out of step. That is not the factor applied twice — it is one factor stated
        /// in the two frames that must match. Applying it anywhere else as WELL (the prefab's root
        /// scale, the surface's local scale) IS the double application, and it is the one thing
        /// here that fails silently.
        /// </para>
        /// <para>
        /// Recovering the clearance without shrinking the board means re-cutting the grid: 22 rows
        /// to 18 makes the fitting 4.11 m and clears the buttress, at 660 cells to 540. That is a
        /// capacity decision, and it belongs in <c>InventoryWallBuilder.SurfaceCellsUp</c> and
        /// <c>inventory_wall.py</c>'s <c>GRID_H</c> together, exactly as the 1.5x re-cut was.
        /// </para>
        /// </summary>
        public const float WallDisplay = 1.219f;

        /// <summary>A length authored in the pre-enlargement frame, in the frame the pack is drawn in.</summary>
        public static float Apply(float metres) => metres * Factor;

        /// <summary>A rectangle authored in the pre-enlargement frame, in the frame the pack is drawn in.</summary>
        public static Vector2 Apply(Vector2 metres) => metres * Factor;
    }
}
