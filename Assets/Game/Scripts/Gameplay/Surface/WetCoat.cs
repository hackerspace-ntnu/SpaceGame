using System;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// Ground the rain has been falling on. A little slippery, and the surface
    /// <see cref="IceCoat"/> can be laid over.
    ///
    /// <para>
    /// <b>Its life is the cloud's, not its own.</b> A storm re-sprays the ground under it on every
    /// rain tick, which refreshes the same patch rather than laying a second one, so the coat lasts
    /// exactly as long as the cloud does — plus the linger below, which is the "and a little" the
    /// design asks for. Nothing has to tell this coat how long a storm is going to last, and a
    /// storm cut short leaves ground that dries a few seconds later rather than ground that stays
    /// wet for the half-minute somebody once typed into a flask.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WetCoat : SurfaceCoatBehaviour
    {
        /// <summary>
        /// How long wet ground outlives the last drop of rain. Comfortably longer than the storm's
        /// 2.5 s bolt interval, so a cloud that is still raining never lets its own ground dry.
        /// </summary>
        private const float LingerSeconds = 8f;

        /// <summary>
        /// A patch big enough to be worth one message. A storm names its own cloud radius; this is
        /// what anything else that wets the ground gets.
        /// </summary>
        private const float DefaultPatchRadius = 4f;

        /// <summary>
        /// Damp, not frictionless. Enough that a run across it overshoots and a braking turn washes
        /// out, and far enough from the Slick film's 0.05 that a player can tell the two apart by
        /// how the ground behaves as well as by how it looks (GDC-L1-SYS-0006).
        /// </summary>
        private const float DefaultGrip = 0.55f;

        public WetCoat() : base(LingerSeconds, DefaultPatchRadius, DefaultGrip) { }

        public override SurfaceCoatKind Kind => SurfaceCoatKind.Wet;
    }
}
