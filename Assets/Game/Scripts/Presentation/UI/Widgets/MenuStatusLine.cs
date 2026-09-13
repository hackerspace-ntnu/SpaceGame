using TMPro;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The one line along the bottom of a menu page that says what just happened.
    ///
    /// <para>
    /// Three kinds of thing land on it and they do not have equal claim. A <see cref="Say"/> is
    /// transient — "Copied ABC123" — and the next thing written replaces it. A <see cref="Warn"/>
    /// is sticky: a page that redraws itself twice a second from a poll would otherwise replace
    /// "Could not change the session's privacy" with a line saying everything was fine before it
    /// could be read, so a <see cref="Polled"/> write is refused while a warning stands. A
    /// <see cref="BeginWait"/> animates a caption and is sticky for the same reason.
    /// </para>
    ///
    /// <para>
    /// Every write stops the animated caption first, and the stop is not optional:
    /// <see cref="MenuBusy.Dots"/> owns the label's text while it runs and rewrites it every time
    /// the dot count changes, so anything written underneath it survives at most a third of a
    /// second — which is how a failure reported mid-join used to vanish before it could be read.
    /// </para>
    /// </summary>
    public sealed class MenuStatusLine
    {
        private readonly TextMeshProUGUI label;

        private MenuBusy dots;
        private bool sticky;

        public MenuStatusLine(TextMeshProUGUI label)
        {
            this.label = label;
        }

        public string Text => label != null ? label.text : string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(Text);

        /// <summary>A transient line. The next write, of any kind, replaces it.</summary>
        public void Say(string message)
        {
            StopDots();
            Write(message);
            sticky = false;
        }

        /// <summary>A failure the player has to actually read. Survives every <see cref="Polled"/> write after it.</summary>
        public void Warn(string message)
        {
            StopDots();
            Write(message);
            sticky = true;
        }

        /// <summary>A line redrawn from a poll, which yields to anything sticky already there.</summary>
        public void Polled(string message)
        {
            if (sticky) return;
            Write(message);
        }

        /// <summary>
        /// Animates a caption while something is in flight. <paramref name="stem"/> is the caption
        /// without its ellipsis — the dots are animated, and a caption that already ends in one
        /// gets two.
        /// </summary>
        public void BeginWait(string stem)
        {
            StopDots();
            dots = MenuBusy.Dots(label, stem);
            sticky = true;
        }

        /// <summary>
        /// Ends the wait. The line is only cleared when the animated caption is still the thing on
        /// it: a failure reported mid-flight has already gone through <see cref="Warn"/>, which
        /// stops the dots — so finding them already stopped is how this knows to leave the reason
        /// where the player can read it.
        /// </summary>
        public void EndWait()
        {
            bool captionWasShowing = dots != null;

            StopDots();

            if (captionWasShowing) Say(string.Empty);
        }

        /// <summary>Stops the animation ahead of the label being destroyed with its page.</summary>
        public void Stop() => StopDots();

        private void Write(string message)
        {
            if (label != null) label.text = message ?? string.Empty;
        }

        private void StopDots()
        {
            if (dots == null) return;

            dots.Stop();
            dots = null;
        }
    }
}
