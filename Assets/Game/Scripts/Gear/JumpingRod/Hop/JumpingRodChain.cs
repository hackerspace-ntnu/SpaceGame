using UnityEngine;

namespace SpaceGame.Gear.JumpingRod
{
    /// <summary>
    /// The rhythm the player plays on the rod: hit Jump as it meets the ground and that hop is
    /// multiplied, keep hitting it and the multiplier compounds, miss once and it is gone.
    ///
    /// <para>
    /// Pure, and the clock is the caller's — the same shape as <c>DoubleTap</c>, and for the same
    /// reason: a timing window is exactly the kind of thing that is impossible to check in a scene
    /// and trivial to check with numbers. Nothing here touches a Rigidbody or a transform; it
    /// answers "how many links" and the item turns that into a speed.
    /// </para>
    /// <para>
    /// The window has two halves and they are not the same size. The <b>early</b> half is an input
    /// buffer (GDC-L1-FEEL-0003): a press a few frames before touchdown is what the player meant,
    /// and throwing it away is the classic way a timing mechanic gets called broken rather than
    /// hard. The <b>late</b> half is narrower, because it is paid out by topping the hop up after
    /// it has already left the ground — every millisecond of it is visible.
    /// </para>
    /// <para>
    /// Two rules make it a skill rather than a habit, and both are here rather than in the item so
    /// that they are testable:
    /// </para>
    /// <list type="bullet">
    /// <item>A landing with no press in the window is a <b>miss</b>, and a miss costs
    /// <c>LinksLostOnMiss</c>. Stop playing and the height goes away.</item>
    /// <item><b>One press per hop.</b> A second press before the landing has been judged spoils
    /// that landing outright, so mashing Jump cannot buy the boost — which it otherwise would,
    /// since a mash always lands a press inside the early window.</item>
    /// </list>
    /// </summary>
    public sealed class JumpingRodChain
    {
        private readonly JumpingRodConfig cfg;

        private float pressedAt = float.NegativeInfinity;
        private bool pressPending;
        private bool spoiled;

        private float landedAt = float.NegativeInfinity;
        private bool lateOpen;
        private int linksBeforeLanding;

        public JumpingRodChain(JumpingRodConfig config)
        {
            cfg = config;
        }

        /// <summary>Links banked, 0 to <c>MaxChainLinks</c>. Feeds <see cref="JumpingRodHopModel.TakeoffSpeed"/>.</summary>
        public int Links { get; private set; }

        /// <summary>
        /// Whether the hop currently in the air was launched on the beat. Read for feedback only —
        /// a boosted hop must not sound like a cruise hop (GDC-L1-FEEL-0004).
        /// </summary>
        public bool OnBeat { get; private set; }

        /// <summary>
        /// The rod has touched down. Returns the links this hop leaves the ground with.
        ///
        /// <para>
        /// A landing with no valid press is settled as a miss <i>here</i>, not deferred until the
        /// late window shuts, because the hop's speed has to be decided on the frame it leaves the
        /// ground. A press that arrives inside the late window afterwards restores what the miss
        /// took and <see cref="Press"/> reports it, so the item can pay the difference into a body
        /// that is already rising.
        /// </para>
        /// </summary>
        public int Land(float now)
        {
            bool hit = pressPending && !spoiled && now - pressedAt <= cfg.BoostWindowEarly;

            linksBeforeLanding = Links;
            Links = hit
                ? Mathf.Min(Links + 1, cfg.MaxChainLinks)
                : Mathf.Max(0, Links - cfg.LinksLostOnMiss);

            // A spoiled hop is not offered the late half either, or mashing buys the boost after
            // all: a mash puts a press a few milliseconds after every touchdown as reliably as it
            // puts one before it, and only closing both halves makes the one-press rule bite.
            lateOpen = !hit && !spoiled;

            pressPending = false;
            spoiled = false;
            landedAt = now;
            OnBeat = hit;

            return Links;
        }

        /// <summary>
        /// Jump was pressed. True when that press rescues the landing just past — the caller owes
        /// the holder the difference between the hop they got and the hop they have now earned.
        /// False means the press was buffered for the landing to come, or spoiled it.
        /// </summary>
        public bool Press(float now)
        {
            if (lateOpen && now - landedAt <= cfg.BoostWindowLate)
            {
                Links = Mathf.Min(linksBeforeLanding + 1, cfg.MaxChainLinks);
                lateOpen = false;
                OnBeat = true;
                return true;
            }

            // Second press of the same hop: it cannot be the one that was meant, so the landing it
            // was aimed at is lost whatever its timing turns out to be.
            if (pressPending || spoiled)
            {
                pressPending = false;
                spoiled = true;
                return false;
            }

            pressPending = true;
            pressedAt = now;
            return false;
        }

        /// <summary>
        /// Forget everything — no links, no pending press, no open window.
        ///
        /// Called when the rod is planted, so a chain cannot survive being stowed and taken back
        /// out, and so a press made while the rod was in the pack is not left waiting to be judged.
        /// </summary>
        public void Reset()
        {
            Links = 0;
            pressPending = false;
            spoiled = false;
            lateOpen = false;
            OnBeat = false;
            pressedAt = float.NegativeInfinity;
            landedAt = float.NegativeInfinity;
            linksBeforeLanding = 0;
        }
    }
}
