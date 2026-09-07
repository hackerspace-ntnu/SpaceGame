using System;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// A frictionless film sprayed onto the ground. Nothing that walks onto it can stop, and a
    /// slope becomes impossible.
    ///
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

        /// <summary>One dab of the can's fan, per the Slick Can's own numbers.</summary>
        private const float DefaultDabRadius = 1.2f;

        /// <summary>
        /// About a twentieth of normal, matching <c>SlickStatus</c>. A body can still steer but can
        /// barely accelerate or brake, which is what a frictionless surface IS.
        /// </summary>
        private const float DefaultGrip = 0.05f;

        public SlickCoat() : base(DefaultDuration, DefaultDabRadius, DefaultGrip) { }

        public override SurfaceCoatKind Kind => SurfaceCoatKind.Slick;
    }
}
