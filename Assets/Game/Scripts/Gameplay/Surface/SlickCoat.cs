using System;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// A film of frost on the ground. Nothing that walks onto it can stop, and a slope becomes
    /// impossible.
    ///
    /// <para>
    /// What the cryo sprayer leaves on every surface that is not liquid. <see cref="IceCoat"/> is
    /// the other half of the same plume — where there is water to freeze the cold makes standable
    /// geometry, and where there is not it makes this, which changes only what the ground DOES.
    /// </para>
    /// <para>
    /// The ground half of a pair: <c>SlickStatus</c> is the same film on a BODY, and the two report
    /// the same interface so that a mover asking "how much grip do I have" never learns which of
    /// them is the reason (GDC-L1-SYS-0005). That one question is also what makes the film work for
    /// a walker, a wheel and a leg alike instead of only for the player.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SlickCoat : SurfaceCoatBehaviour
    {
        /// <summary>Twenty seconds — the same clock the Slick status on a body runs.</summary>
        private const float DefaultDuration = 20f;

        /// <summary>A dab about the width of the plume's landing burst.</summary>
        private const float DefaultDabRadius = 1.2f;

        /// <summary>
        /// The same near-nothing <see cref="IceCoat"/> leaves, and one number rather than two on
        /// purpose: both are the cryo sprayer's cold, and a player who has learnt what frozen
        /// ground does to them should not have to learn it twice because one patch happened to
        /// land on water (GDC-L1-SYS-0006). A body can still steer but can barely accelerate or
        /// brake, which is what a frictionless surface IS. Not zero — see the base class on why
        /// nothing here ever is.
        /// </summary>
        private const float DefaultGrip = 0.03f;

        public SlickCoat() : base(DefaultDuration, DefaultDabRadius, DefaultGrip) { }

        public override SurfaceCoatKind Kind => SurfaceCoatKind.Slick;
    }
}
