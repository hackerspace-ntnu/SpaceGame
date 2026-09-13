using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How much bigger the physical inventory is than the rig it was first measured off.
    ///
    /// <para>
    /// <b>One number, applied to a whole world.</b> The cell, every surface rectangle, the rig's
    /// own geometry, the size every item is drawn at on the mat and the focus camera's standoff
    /// are all <see cref="Factor"/> times the length the rig was first modelled at. Because it is
    /// a <em>similarity</em> transform — one factor on every length, none on any count — the pack
    /// holds exactly the cells it held before (255 on the rig), every item occupies exactly the
    /// cells it occupied before, and every authored <see cref="PackShape"/> mask stays valid.
    /// Nothing about capacity has ever moved with it. What moves is how big the rig is: how much
    /// of the screen it fills in focus mode, and how much of the player it is on their back.
    /// </para>
    /// <para>
    /// It has moved twice. <b>2026-09-01</b> took it from 1 to 1.5, to buy readability on the mat.
    /// <b>2026-09-02</b> took it to <b>1.05</b> — 30% off the 1.5 rig, which had come out too big
    /// to wear: the stowed board was 1.215 m and swung into the wearer's own first-person camera
    /// (see <c>BackpackController</c>'s worn-hidden note), and the lash line took a 2.43 m item.
    /// Each move is a similarity transform, so neither cost a single cell of capacity.
    /// </para>
    /// <para>
    /// The ship's gear wall is the one surface this factor does NOT size, and
    /// <see cref="WallDrawn"/> is why: the wall is sized by the room it stands in, so it is
    /// pinned in the ORIGINAL frame and is drawn at exactly the same size at any
    /// <see cref="Factor"/>. Its grid was re-cut from 60 x 30 to 30 x 22 on 2026-09-01 — 1800
    /// cells to 660 — in <c>InventoryWallBuilder.SurfaceCellsAcross</c>/<c>Up</c> and in
    /// <c>inventory_wall.py</c>. That is a decision about a room, not about this factor.
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
    /// <b>Every save file is in the frame of the factor it was written at.</b> A placement's uv is
    /// metres, not a normalised coordinate and not a cell index, so moving this constant restates
    /// every uv already on disk. <c>PackSaveCodec</c> therefore stamps a version and multiplies an
    /// old payload's uvs by <c>today's cell / that version's cell</c> on load, rather than letting
    /// first-fit rearrange the player's gear behind their back. Scaling a face and the uvs on it by
    /// the same factor is exactly cell-preserving, so nothing moves and nothing is lost — see
    /// <see cref="LegacyCell"/>, <see cref="EnlargedCell"/> and the codec's <c>Version</c> field.
    /// </para>
    /// <para>
    /// Nothing here touches UnityEngine beyond <see cref="Vector2"/>, so the EditMode tests drive it
    /// as plain C#.
    /// </para>
    /// </summary>
    public static class PackScale
    {
        /// <summary>
        /// The rig's size. Every length on the backpack is this many times the length the rig was
        /// originally modelled at.
        ///
        /// <para>
        /// Moving it is a similarity transform and costs no capacity, but it is NOT free: it
        /// restates <see cref="PackGrid.Cell"/>, <c>ExpeditionRigWiring.SurfaceTable</c>,
        /// <c>expedition_rig_scale.py</c>'s <c>SCALE</c> and every uv already in a save file. All
        /// five have to move in the same change — <c>PackScaleTests</c>,
        /// <c>PackSurfaceTests.SurfaceTable_MatchesTheRigsCellCounts</c> and the codec's version
        /// are what notice when one of them does not.
        /// </para>
        /// </summary>
        public const float Factor = 1.05f;

        /// <summary>
        /// The cell at <see cref="Factor"/> 1, in metres: the pitch of the webbing ladder on the
        /// rig as it was originally modelled.
        ///
        /// <para>
        /// Kept because it is the frame every save file written before 2026-09-01 is in, because
        /// it is the number <see cref="PackGrid.Cell"/> is derived from — a test asserts the two
        /// still agree, rather than the derivation being done in <c>const</c> arithmetic whose
        /// last bit nobody can predict — and because it is the frame the gear wall's own size is
        /// pinned in (<see cref="WallDrawn"/>).
        /// </para>
        /// </summary>
        public const float LegacyCell = 0.09f;

        /// <summary>
        /// The cell between 2026-09-01 and 2026-09-02, in metres: <see cref="LegacyCell"/> at the
        /// 1.5 rig.
        ///
        /// <para>
        /// Kept for one reason only — it is the frame every <c>v3</c> save file's uvs are in, and
        /// the codec brings them forward by <see cref="PackGrid.Cell"/> over this. A frame with no
        /// save files left in it can be deleted; this one has the player's current world in it.
        /// </para>
        /// </summary>
        public const float EnlargedCell = 0.135f;

        /// <summary>
        /// The scale <c>inventory_wall.blend</c> is BAKED at: <c>inventory_wall_scale.py</c>'s
        /// <c>TOTAL</c>, stamped into the mesh data itself and carried by the exported FBX.
        ///
        /// <para>
        /// Kept separate from <see cref="WallDrawn"/> so the drawn size can move without the
        /// <c>.blend</c> — which carries hand edits the generator would destroy — being
        /// regenerated. <c>InventoryWallBuilder</c> applies the residual
        /// <c>WallDrawn / WallModel</c> as a uniform scale on the prefab root, and everything
        /// that maps uvs divides a transform's lossy scale back out, so the two frames meet on
        /// the same webbing lines. Change this only when the model is actually re-baked at a new
        /// <c>TOTAL</c>.
        /// </para>
        /// </summary>
        public const float WallModel = 1.59f;

        /// <summary>
        /// How much bigger than the ORIGINAL <see cref="LegacyCell"/> frame the ship's gear wall
        /// is DRAWN — the model's baked <see cref="WallModel"/> plus the 20% enlargement decided
        /// on 2026-09-02. The wall's real, on-screen size.
        ///
        /// <para>
        /// <b>The gear wall is sized by decision, not by the rig.</b> Its drawn size cannot ride
        /// <see cref="Factor"/>: shrinking the backpack must not shrink the ship's fitting.
        /// Stating it here, in the one frame that never moves, is what makes it invariant —
        /// <see cref="WallDisplay"/> below re-derives itself against whatever <see cref="Factor"/>
        /// is, so the board stays exactly where it is when the rig resizes.
        /// </para>
        /// <para>
        /// <b>It stands over the aft room's measured budget, deliberately.</b> The fitting is
        /// 2.580 m tall in this frame (all-mesh bounds of <c>inventory_wall.blend</c> at
        /// <see cref="LegacyCell"/>), and rays cast up from the deck over its footprint against
        /// the ship's BAKED COLLISION, at <c>PlayerShipBuilder.WallRibClearance</c>, find 4.383 m
        /// of headroom on the 2026-09-02 ship — capped by the hull skin's convex-decomposition
        /// fill, not by the visible deckhead. With the 0.25 m gap the old guard required, that
        /// budget allows 1.602; the wall stood at 1.59 under it until 2026-09-02, when the user
        /// chose the bigger board over the clearance — and then hand-placed the fitting
        /// (<c>PlayerShipBuilder.WallPlacementNudge</c>) with its back tucked into that fill. The
        /// guards (<c>WallInventoryTests.TheWallIsDrawnAtItsDecidedSize</c> and
        /// <c>PlayerShipTests.PlayerShip_InventoryWallFaceIsAimableFromTheRoom</c>) pin the
        /// decision and the face's usability instead of the old clearance.
        /// </para>
        /// <para>
        /// A future resize needs no <c>.blend</c> work — move this constant, re-run the wall and
        /// ship builders, and re-measure the two guards' numbers. Shrinking the grid instead
        /// (<c>InventoryWallBuilder.SurfaceCellsUp</c> with <c>inventory_wall.py</c>'s
        /// <c>GRID_H</c>) is the lever that DOES need the model regenerated, hand edits and all.
        /// </para>
        /// </summary>
        public const float WallDrawn = WallModel * 1.2f;

        /// <summary>
        /// How much bigger than its own logical frame the ship's gear wall is DRAWN.
        ///
        /// <para>
        /// <b>This is not <see cref="Factor"/> and it is not the same kind of number.</b>
        /// <see cref="Factor"/> is a similarity transform of the whole rig, counts and all: it
        /// moves the cell, so 255 cells stay 255 cells of a different size, and every uv on disk
        /// has to be brought forward with it. <c>WallDisplay</c> moves nothing but pixels. The
        /// wall's face is still 30 x 22 cells of <see cref="PackGrid.Cell"/> and still 660 cells;
        /// what this multiplies is only the mapping from a uv to the point on screen it is drawn
        /// at, in <see cref="PackSurface.ToLocal"/> and back out in <see cref="PackSurface.ToUv"/>.
        /// Nothing it touches is persisted or sent, so a save written at any value of this loads
        /// onto byte-identically the same cells at any other. See <c>PackSurface.DisplayScale</c>.
        /// </para>
        /// <para>
        /// <b>Derived, not typed</b>, and that is the whole point: the wall's drawn size is
        /// <see cref="WallDrawn"/> and belongs to the room, so this has to be whatever turns the
        /// wall's LOGICAL frame — which does ride <see cref="Factor"/>, because the cell does —
        /// back into it. Type a number here instead and the next change to <see cref="Factor"/>
        /// resizes the ship's fitting silently.
        /// </para>
        /// <para>
        /// <b>The drawn board and the drawn uvs still have to agree</b>: the bay dividers are six
        /// cells apart and the webbing is on a two-cell pitch, so a model out of step with the
        /// mapping puts every line the player drops gear onto in the wrong place. The model's
        /// geometry carries <see cref="WallModel"/> baked in, and <c>InventoryWallBuilder</c>
        /// scales the prefab root by the residual <c>WallDrawn / WallModel</c> — which is safe
        /// precisely because <see cref="PackSurface.ToLocal"/>, <c>BackpackItemVisual</c> and
        /// <c>HolderBuilder</c> all divide a transform's lossy scale back out and follow THIS
        /// number instead. Scaling anything they do not divide out (the surface's own local
        /// scale, say) IS a double application, and it fails silently.
        /// </para>
        /// </summary>
        public const float WallDisplay = WallDrawn / Factor;

        /// <summary>A length authored in the pre-enlargement frame, in the frame the pack is drawn in.</summary>
        public static float Apply(float metres) => metres * Factor;

        /// <summary>A rectangle authored in the pre-enlargement frame, in the frame the pack is drawn in.</summary>
        public static Vector2 Apply(Vector2 metres) => metres * Factor;
    }
}
