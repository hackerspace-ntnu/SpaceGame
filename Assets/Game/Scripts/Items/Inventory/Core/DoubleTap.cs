namespace SpaceGame.Items
{
    /// <summary>
    /// Two presses of the same button inside a short window.
    ///
    /// <para>
    /// Pure so the window is testable without a scene. The caller supplies the clock — unscaled
    /// time in play, whatever a test likes — because a gesture that reads the game clock would
    /// stretch and shrink with slow-motion, and a gesture whose window depends on the situation is
    /// one the player cannot learn (GDC-L1-FEEL-0003: forgiveness windows stay fixed).
    /// </para>
    /// <para>
    /// A hit consumes both presses: the third press of a fast triple starts a fresh count rather
    /// than pairing with the second, so mashing the key cannot fire the gesture twice.
    /// </para>
    /// </summary>
    public sealed class DoubleTap
    {
        private readonly float window;
        private float last = float.NegativeInfinity;

        public DoubleTap(float windowSeconds)
        {
            window = windowSeconds;
        }

        /// <summary>Record a press at <paramref name="now"/>; true when it completes a double tap.</summary>
        public bool Press(float now)
        {
            if (now - last <= window)
            {
                last = float.NegativeInfinity;
                return true;
            }

            last = now;
            return false;
        }
    }
}
