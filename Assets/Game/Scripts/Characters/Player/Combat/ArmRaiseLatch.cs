namespace SpaceGame.Characters
{
    /// <summary>
    /// When a gauntlet arm is up. A press raises it for a short linger; a continuous item keeps
    /// it up for as long as its hold stream is active and then lingers on the release.
    ///
    /// <para>
    /// Pure, clocked by the caller, so the three timings — tap, hold, release — can be pinned
    /// without a scene. The linger exists because a tap is over in a frame and an arm that came
    /// up and went straight back down would read as a twitch rather than a shot.
    /// </para>
    /// </summary>
    public sealed class ArmRaiseLatch
    {
        private readonly float linger;
        private float raisedUntil = float.NegativeInfinity;
        private bool held;

        public ArmRaiseLatch(float lingerSeconds)
        {
            linger = lingerSeconds;
        }

        /// <summary>The item fired. <paramref name="continuous"/> keeps the arm up until <see cref="Hold"/> says otherwise.</summary>
        public void Press(float now, bool continuous)
        {
            raisedUntil = now + linger;
            held = continuous;
        }

        /// <summary>A hold tick. The final one (<paramref name="active"/> false) starts the linger.</summary>
        public void Hold(bool active, float now)
        {
            if (held && !active) raisedUntil = now + linger;
            held = active;
        }

        /// <summary>The arm has nothing on it any more: down at once.</summary>
        public void Clear()
        {
            held = false;
            raisedUntil = float.NegativeInfinity;
        }

        public bool Raised(float now) => held || now < raisedUntil;
    }
}
