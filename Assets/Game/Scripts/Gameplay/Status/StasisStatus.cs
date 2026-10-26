using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Held out of time: helpless for a few seconds inside a shimmering shell.
    ///
    /// <para>
    /// The same contract as <see cref="FrozenStatus"/> — the body cannot act, it takes no damage
    /// from the condition and the condition cannot kill — and it is a kind of its own rather than a
    /// second source of <c>Frozen</c> for one reason: the LOOK belongs to the kind here. A freeze
    /// is drawn by the sprayer that caused it, ice growing out of a plume; stasis is laid by a bolt
    /// and by the sprite hamlet's dome alike, and both have to draw the same shell on every machine,
    /// including the one that never saw the bolt fly. So this condition puts its own
    /// <see cref="StasisShell"/> on the body, and the shell reads the flag every frame.
    /// </para>
    /// <para>
    /// <b>It is never extended.</b> A body standing against the dome touches it every scan, and a
    /// refresh on every touch would hold it there for as long as it stays — which is a stasis nobody
    /// escapes, not a defence. The condition runs its time once; whatever applied it may apply it
    /// afresh after it ends, and that is a decision the applier makes in the open
    /// (GDC-L1-MP-0002).
    /// </para>
    /// <para>
    /// <b>The suppression is derived, never written</b>, for the reason <see cref="FrozenStatus"/>
    /// gives: <c>AgentController</c> reads <see cref="StatusReceiver.Suppressed"/> every frame, and a
    /// player is held standing through <see cref="BodyHold"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StasisStatus : StatusBehaviour
    {
        /// <summary>
        /// Five seconds: long enough for the sprite hamlet's keepers to reach a witch frozen near
        /// them, short enough that one held at range thaws before they arrive.
        /// </summary>
        private const float DefaultDuration = 5f;

        public StasisStatus() : base(DefaultDuration) { }

        private readonly BodyHold hold = new BodyHold();

        public override StatusKind Kind => StatusKind.Stasis;

        public override bool Suppresses => true;

        /// <summary>Once per stasis. See the class summary.</summary>
        public override bool CanApply(StatusReceiver body, bool running) => !running;

        public override void OnApplied(StatusReceiver body)
        {
            hold.TakeStanding(body);
            StasisShell.Ensure(body);
        }

        // The shell takes itself down: it reads the flag every frame, so a machine that never hears
        // the end of the condition still loses the shell the frame its own clock runs out.
        public override void OnCleared(StatusReceiver body) => hold.Release();
    }
}
