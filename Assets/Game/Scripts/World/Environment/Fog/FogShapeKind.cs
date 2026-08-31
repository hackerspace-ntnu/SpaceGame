namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The shape of a body of fog.
    ///
    /// <para>
    /// Every kind is evaluated in the volume's own space, where it occupies the unit region, so
    /// rotation and non-uniform scale come free from the transform and none of them needs a special
    /// case in the shader. A cylinder can lean; a box can be a wedge stood on one corner.
    /// </para>
    ///
    /// <para>
    /// The numbers are uploaded to the shader as-is, so they are part of the shader contract.
    /// Reordering them changes what every authored volume in every scene looks like.
    /// </para>
    /// </summary>
    public enum FogShapeKind
    {
        /// <summary>
        /// A rounded bank of fog. The default, and the one to reach for outdoors: it has no
        /// direction and no corner to give away that a shape was placed there.
        /// </summary>
        Ellipsoid = 0,

        /// <summary>
        /// A rectangular body. What a room, a corridor or a canyon full of fog wants, because those
        /// are the shapes the architecture already has.
        /// </summary>
        Box = 1,

        /// <summary>
        /// A column. Stood upright it is a geyser or a shaft of dust; tipped over it is a vent
        /// blowing sideways.
        /// </summary>
        Cylinder = 2,

        /// <summary>
        /// Low-lying mist. Rectangular in plan, but the density decays upward from the floor and is
        /// never cut off by a top face — so there is no ceiling hanging over the player's head, which
        /// is the single thing that gives a box of ground fog away.
        /// </summary>
        GroundLayer = 3,
    }
}
