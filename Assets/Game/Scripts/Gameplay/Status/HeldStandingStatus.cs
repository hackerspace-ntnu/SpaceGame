using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// A condition that leaves a body helpless where it stands: frozen solid, tied up.
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
    /// standing, not limp. See there for why these are the holds in this game that must not go
    /// through the ragdoll.
    /// </para>
    /// <para>
    /// The look is not this class's business: each kind's source draws its own (ice for a freeze,
    /// rope for a tie). The condition itself only says the body cannot act.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class HeldStandingStatus : StatusBehaviour
    {
        private readonly BodyHold hold = new BodyHold();

        protected HeldStandingStatus(float defaultSeconds) : base(defaultSeconds) { }

        /// <summary>The body does nothing at all until it is let go.</summary>
        public override bool Suppresses => true;

        public override void OnApplied(StatusReceiver body) => hold.TakeStanding(body);

        public override void OnCleared(StatusReceiver body) => hold.Release();
    }
}
