namespace SpaceGame.Items
{
    /// <summary>
    /// Which of the deployed rig's faces an item is placed on.
    ///
    /// <para>
    /// Values are persisted in saves and sent on the wire as a byte, so DO NOT renumber or reorder
    /// existing entries. New faces are APPENDED.
    /// </para>
    /// </summary>
    public enum PackSurfaceId : byte
    {
        BackPanelLeft = 0,
        BackPanelRight = 1,
        Leaf = 2,
        WingLeft = 3,
        WingRight = 4,

        /// <summary>
        /// <c>SURF_LongGoods</c>: the 18 x 1 cell lash line running across the full open width of
        /// the rig, over the leaf and both wings — 1.701 x 0.0945 m at the current
        /// <see cref="PackScale.Factor"/>.
        ///
        /// <para>
        /// It exists because nothing else on the pack can take a long tool. Measured in the
        /// original frame, where the cell is 0.090 m: the open faces were 0.86 x 0.92 m, whose
        /// <em>diagonal</em> — the longest segment that fits inside a rectangle at all — is
        /// 1.2609 m, so the 1.35 m LaserStaff fit none of them at any yaw, while the lash line's
        /// 1.6061 m diagonal took it square on. Every length in that argument, the staff's
        /// included, rides the same factor, so the conclusion holds at any value of it.
        /// An early draft of the spec claimed the staff went on the leaf "on the diagonal"; that
        /// was arithmetically impossible, and this surface is the fix.
        /// </para>
        /// </summary>
        LongGoods = 5,

        /// <summary>
        /// <c>SURF_Rack</c>: the 9 x 9 cell face of the front leaf once it has been flipped up
        /// into a vertical rack — the leaf's UNDERSIDE, which is the side that ends up pointing at
        /// the player when the leaf stands. 0.8505 x 0.8505 m at the current
        /// <see cref="PackScale.Factor"/>.
        ///
        /// <para>
        /// It is the biggest rectangle on the rig, and the only one with both axes over nine cells.
        /// That is what it is for. Length was never the gap — <see cref="LongGoods"/> already spans
        /// 18 cells — but bulk was: every other face is at most 8 cells deep, so a wing panel or a
        /// crate that is 7 cells across fits nowhere at any yaw.
        /// </para>
        /// <para>
        /// It grew from 6 to 9 cells deep on 2026-08-25, with the board it is the underside of,
        /// when <c>ItemScaleLadder</c> roughly doubled the gear: at 8 x 6 cells a single launcher
        /// took half the face and two of them filled it.
        /// </para>
        /// <para>
        /// <b>It only exists while the leaf is up.</b> With the mat down this face is underneath it,
        /// against the sand, so <see cref="BackpackObject.Reaches"/> refuses to first-fit or drag
        /// anything onto it. An explicit placement — a save, a client adopting the server's list —
        /// still lands, because losing gear on a load is worse than gear that is out of reach until
        /// the player flips the leaf back up.
        /// </para>
        /// </summary>
        Rack = 6,

        /// <summary>
        /// <c>SURF_WallGrid</c>: the face of the ship's inventory wall — 30 x 22 cells, against
        /// the rig's biggest face at 9 x 9. Re-cut to that count on 2026-09-01 so the fitting
        /// clears the lander's aft room. Its metres follow <see cref="PackGrid.Cell"/> like every
        /// other face; its DRAWN size does not, and is pinned by <see cref="PackScale.WallDrawn"/>.
        ///
        /// <para>
        /// Not a face of the rig at all, which is why this enum's name has outgrown it slightly:
        /// it identifies a face of any <see cref="PackContainer"/>, and the wall is the second
        /// one. Appended rather than renumbered, because these values are persisted and sent as a
        /// byte.
        /// </para>
        /// <para>
        /// The wall is one surface and not five panel-shaped ones, even though the model reads as
        /// five bays. Bay boundaries would be real walls that an item could not straddle, invisible
        /// from three metres away, and every one of them would waste up to five cells of the row it
        /// cut. The bays are decoration on a continuous grid.
        /// </para>
        /// </summary>
        WallGrid = 7,

        /// <summary>
        /// <c>SURF_Back_C</c>: the 3 x 6 cell strip between the two back panels, on the pack's
        /// centre line — the spot the rig's own modelled oxygen bottle used to be bolted to.
        ///
        /// <para>
        /// It exists because that bottle was <b>geometry</b>, authored as "a fixed fitting, not an
        /// item", so nothing could ever take it off. It is gone (see
        /// <c>expedition_rig_BUILD.md</c>), and this is the face that gives the real bottle its
        /// place: same plane, same rotation and the same 3 x 6 cells as its two neighbours, with
        /// 15 mm of clearance to each of them.
        /// </para>
        /// <para>
        /// <b>The only RESERVED face on any container.</b> A face normally takes anything that
        /// fits; this one accepts the oxygen bottle alone (<see cref="PackSurface.AcceptsOnly"/>),
        /// because it is a socket rather than a shelf — the pack plumbs into whatever stands here,
        /// and a rifle in the bottle's cradle would be plumbed into nothing. Overhang is strict
        /// for the same reason: an item hanging past this strip is hanging over the two panels
        /// beside it.
        /// </para>
        /// <para>
        /// Appended rather than renumbered, because these values are persisted and sent as a byte.
        /// </para>
        /// </summary>
        BackPanelCentre = 8
    }
}
