using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Frozen solid: helpless while it lasts, and nothing else. How it holds the body is
    /// <see cref="HeldStandingStatus"/>'s.
    ///
    /// <para>
    /// <b>It deals no damage and it cannot kill.</b> Ten seconds of not playing is already the
    /// whole price, and a condition that also billed damage would make the sprayer an execution
    /// rather than a control tool — the victim can see it coming for a second and a half and can
    /// do nothing about it once it lands, which is exactly the shape that must not be lethal
    /// (GDC-L1-MP-0002, GDC-L1-BAL-0004). An ordinary hit lands on a frozen body as it would on
    /// any other, through the ordinary damage path; the condition simply has no opinion about it.
    /// </para>
    /// <para>
    /// A frozen body reads as a statue because the Cryo Sprayer freezes its animator and grows ice
    /// over it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FrozenStatus : HeldStandingStatus
    {
        /// <summary>Ten seconds, and it is not refreshed by being sprayed harder.</summary>
        private const float DefaultDuration = 10f;

        public FrozenStatus() : base(DefaultDuration) { }

        public override StatusKind Kind => StatusKind.Frozen;
    }
}
