// How hard something caught in a net is fighting, on a scale that cannot be cheated by pressing
// faster.
//
// The cap is not a balance tweak, it is the design. A struggle that rewards raw input rate is a
// mechanic that excludes anyone who cannot spam a key and rewards anyone who binds an autofire
// macro — the same players, penalised and rewarded for a property of their hardware rather than of
// their play (GDC-L1-UX-0006). Saturating the meter removes both at once.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A 0..1 measure of how hard a captive is fighting, saturating at a fixed input rate.
    ///
    /// <para>
    /// Pure: it holds no input, no transform and no network. What counts as a struggle input is the
    /// caller's business — a netted player's Jump press and Move direction reversal both count —
    /// and this only decides whether one is worth anything and what it adds up to.
    /// </para>
    /// <para>
    /// <b>The cooldown throttles the wire as well as the meter.</b> <see cref="Push"/> answering
    /// false is the caller's signal not to send, so a netted player emits at most
    /// <c>maxUsefulRate</c> messages a second however fast they hammer the key.
    /// </para>
    /// </summary>
    public class SnareStruggleMeter
    {
        private readonly float cooldown;
        private readonly float decaySeconds;

        private float level;
        private float sinceAccepted;

        /// <summary>0 when the captive is still, 1 when they are fighting as hard as counts.</summary>
        public float Level => level;

        /// <param name="maxUsefulRate">
        /// Inputs per second beyond which nothing more is gained. 2.5 is the shipped value: fast
        /// enough to read as a struggle, slow enough that a player can hold it indefinitely without
        /// hurting their hands.
        /// </param>
        /// <param name="decaySeconds">
        /// How long the level takes to fall away once the captive stops. Without it, one burst of
        /// struggling drains the net for the rest of its life.
        /// </param>
        public SnareStruggleMeter(float maxUsefulRate, float decaySeconds)
        {
            // Bounded on both sides, not just floored. An unbounded maxUsefulRate — a misconfigured
            // Inspector field, or float.PositiveInfinity — drives cooldown to zero, and at cooldown
            // zero Push never returns false: no rate limit on the level, and no send throttle on the
            // caller, at all. An upper bound (60, an already-silly input rate) is as cheap as the
            // lower one this already had.
            //
            // Built from Max then Min rather than Mathf.Clamp, because Mathf.Clamp compares with
            // `<` and `>`, both of which are false when the left side is NaN, so a NaN maxUsefulRate
            // would sail through unclamped and poison cooldown with NaN (and NaN in cooldown means
            // `sinceAccepted < cooldown` is also always false — the same unthrottled failure as the
            // infinity case, reached a different way). Mathf.Max and Mathf.Min are both written as
            // `a > b ? a : b` / `a < b ? a : b`, so a NaN first argument loses that comparison and
            // the method returns the second, non-NaN one instead — the same trick decaySeconds below
            // already relies on for the same reason.
            cooldown = 1f / Mathf.Min(Mathf.Max(maxUsefulRate, 0.01f), 60f);
            this.decaySeconds = Mathf.Max(decaySeconds, 0.01f);

            // Born past the cooldown so the very first input counts. Starting at zero would eat it,
            // and the one input a player notices being ignored is the first.
            sinceAccepted = cooldown;
        }

        /// <summary>
        /// Offer one struggle input.
        /// </summary>
        /// <returns>
        /// True if it counted. False means it landed inside the cooldown and was discarded — the
        /// caller should not send it either.
        /// </returns>
        public bool Push()
        {
            if (sinceAccepted < cooldown) return false;

            // Leaky bucket: carry the overshoot forward instead of resetting to zero.
            //
            // A press that lands late (sinceAccepted > cooldown, because the caller's own input rate
            // is close to but not an exact multiple of the cooldown) has already earned this
            // cooldown *and* banked whatever elapsed on top of it. Resetting that surplus to zero —
            // `sinceAccepted = 0f`, this line's previous form — throws the banked remainder away, so
            // the next input has to wait out a full fresh cooldown regardless of how much of one had
            // already elapsed. At a steady 2.6 Hz input that discarded remainder means only every
            // other press ever lands: accepted rate collapses to 1.3 Hz, half the cap, and the
            // sustained level with it — worse than struggling at a slower, honest 2.0 Hz, and worse
            // than a 20 Hz macro's alias, which only happens to come out unharmed because 0.05 s
            // divides the 0.4 s cooldown exactly. Both properties Push exists to guarantee — spamming
            // doesn't help, and it doesn't hurt either — were inverted for every rate in between.
            //
            // Keeping the remainder (capped by the Min so nobody can go quiet for a while and bank a
            // burst for later) makes the accepted rate exactly min(inputRate, maxUsefulRate) at every
            // input rate, not just the ones that happen to divide the cooldown evenly.
            sinceAccepted = Mathf.Min(sinceAccepted - cooldown, cooldown);

            // Peaks at exactly this amount after every accepted push, at every rate — what changes
            // with rate is not how far a single push climbs, but how little of that climb decays away
            // before the next one arrives. See Advance for the rest of the shape.
            level = Mathf.Min(1f, level + cooldown / decaySeconds);
            return true;
        }

        /// <summary>
        /// Let time pass. Drives the cooldown and the decay.
        ///
        /// <para>
        /// The decay is exponential, not a flat per-second rate. A flat rate (the form this used to
        /// have, <c>level -= delta / decaySeconds</c>) removes a fixed *amount* of level per second
        /// regardless of how much is there, which reaches exactly zero after one decaySeconds' worth
        /// of elapsed time no matter how high the level started — a 1.2 s hitch, load spike or
        /// breakpoint would silently wipe a struggling captive's meter outright. Multiplying by
        /// <see cref="Mathf.Exp"/> instead removes a fixed *fraction* of whatever remains — 63% over
        /// one decaySeconds, asymptotically approaching zero but never quite reaching it in one step —
        /// which is what "decaySeconds" is supposed to mean, is exact regardless of step size (a
        /// single 1.2 s Advance call and 72 calls of 1/60 s each land on the same answer), and cannot
        /// go negative on its own — no <see cref="Mathf.Max"/> guard needed here, the way the old
        /// flat-rate form needed one. (<see cref="SpaceGame.Items.SnareLattice"/>'s own accumulator
        /// clamps too, but against a different hitch-triggered failure — unbounded substep catch-up,
        /// not a value wiped to zero — so it is a sibling problem here, not the same one.)
        /// </para>
        /// <para>
        /// Pushing at a sustained rate <c>r</c> (up to the cap) settles into a periodic climb-then-
        /// decay cycle whose <b>time-average</b> is exactly <c>r / maxUsefulRate</c> — every accepted
        /// push adds <c>cooldown / decaySeconds</c>, and in the steady state that has to equal the
        /// average proportional loss over the same stretch, which is <c>average level / decaySeconds</c>;
        /// solving that balance is <c>r / maxUsefulRate</c> exactly, independent of how cooldown and
        /// decaySeconds compare to each other. The instantaneous level oscillates around that average
        /// rather than sitting on it, though, and the size of that oscillation — how far the *peak*
        /// right after a push sits above the mean — does depend on the ratio of cooldown to
        /// decaySeconds. At the shipped values that peak reaches the <see cref="Mathf.Min"/> clamp
        /// once the rate passes about 2.055 Hz (1.2·ln(1.5) seconds between pushes), not exactly at
        /// the 2.5 Hz cap — which is a better property than landing exactly at the cap would have
        /// been, since it means the top of the user's stated 2-3 press/second band saturates with
        /// headroom rather than only just arriving there.
        /// </para>
        /// </summary>
        public void Advance(float delta)
        {
            sinceAccepted += delta;
            level *= Mathf.Exp(-delta / decaySeconds);
        }
    }
}
