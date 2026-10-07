using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Frozen solid: helpless while it lasts, and nothing else.
    ///
    /// <para>
    /// <b>The suppression is derived, never written.</b> Nothing here disables a motor, a
    /// NavMeshAgent or a behaviour module. A creature is helpless because
    /// <see cref="StatusBehaviour.Suppresses"/> is true and <c>AgentController</c> reads
    /// <see cref="StatusReceiver.Suppressed"/> every frame, and the frame the flag goes away the
    /// creature moves again with nothing to restore. The alternative — switching the brain off —
    /// is a world save capturing a switched-off brain and a creature that reloads frozen forever
    /// with a clean console.
    /// </para>
    /// <para>
    /// A player has no behaviour module to starve, so they are held by <see cref="BodyHold"/> —
    /// standing, not limp. See there for why a freeze is the one hold in this game that must not
    /// go through the ragdoll.
    /// </para>
    /// <para>
    /// <b>It deals no damage and it cannot kill.</b> Ten seconds of not playing is already the
    /// whole price, and a condition that also billed damage would make the sprayer an execution
    /// rather than a control tool — the victim can see it coming for a second and a half and can
    /// do nothing about it once it lands, which is exactly the shape that must not be lethal
    /// (GDC-L1-MP-0002, GDC-L1-BAL-0004). An ordinary hit lands on a frozen body as it would on
    /// any other, through the ordinary damage path; the condition simply has no opinion about it.
    /// </para>
    /// <para>
    /// The pose is not this class's business. A frozen body reads as a statue because the Cryo
    /// Sprayer freezes its animator and grows ice over it; the condition itself only says the body
    /// cannot act.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FrozenStatus : StatusBehaviour
    {
        /// <summary>Ten seconds, and it is not refreshed by being sprayed harder.</summary>
        private const float DefaultDuration = 10f;

        public FrozenStatus() : base(DefaultDuration) { }

        private readonly BodyHold hold = new BodyHold();

        public override StatusKind Kind => StatusKind.Frozen;

        /// <summary>A frozen body does nothing at all until it thaws.</summary>
        public override bool Suppresses => true;

        public override void OnApplied(StatusReceiver body) => hold.TakeStanding(body);

        public override void OnCleared(StatusReceiver body) => hold.Release();
    }
}
