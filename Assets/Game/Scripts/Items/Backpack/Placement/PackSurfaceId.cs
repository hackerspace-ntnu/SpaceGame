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
        /// <c>SURF_LongGoods</c>: the 1.60 x 0.14 m lash line running across the full open width
        /// of the rig, over the leaf and both wings.
        ///
        /// <para>
        /// It exists because nothing else on the pack can take a long tool. The open faces are
        /// 0.86 x 0.92 m, whose <em>diagonal</em> — the longest segment that fits inside a
        /// rectangle at all — is 1.2609 m, so the 1.35 m LaserStaff fits none of them at any yaw.
        /// The lash line's diagonal is 1.6061 m and it takes the staff square on. An early draft of
        /// the spec claimed the staff went on the leaf "on the diagonal"; that was arithmetically
        /// impossible, and this surface is the fix.
        /// </para>
        /// </summary>
        LongGoods = 5,

        /// <summary>
        /// <c>SURF_Rack</c>: the 0.80 x 0.80 m face of the front leaf once it has been flipped up
        /// into a vertical rack — the leaf's UNDERSIDE, which is the side that ends up pointing at
        /// the player when the leaf stands.
        ///
        /// <para>
        /// It is the biggest rectangle on the rig at 0.64 m&#178;, and the only one with both axes
        /// over half a metre. That is what it is for. Length was never the gap —
        /// <see cref="LongGoods"/> already spans 1.60 m — but bulk was: every other face is at most
        /// 0.70 m deep, so a wing panel or a crate that is 0.6 m across fits nowhere at any yaw.
        /// </para>
        /// <para>
        /// It grew from 0.60 to 0.80 m deep on 2026-08-25, with the board it is the underside of,
        /// when <c>ItemScaleLadder</c> roughly doubled the gear: at 8 x 6 cells a single 1.25 m
        /// launcher took half the face and two of them filled it.
        /// </para>
        /// <para>
        /// <b>It only exists while the leaf is up.</b> With the mat down this face is underneath it,
        /// against the sand, so <see cref="BackpackObject.Reaches"/> refuses to first-fit or drag
        /// anything onto it. An explicit placement — a save, a client adopting the server's list —
        /// still lands, because losing gear on a load is worse than gear that is out of reach until
        /// the player flips the leaf back up.
        /// </para>
        /// </summary>
        Rack = 6
    }
}
