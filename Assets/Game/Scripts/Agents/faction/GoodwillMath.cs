// What a faction currently thinks of one player, as a number and as a band.
//
// The other meter. AggressionMath is per AGENT and resets — this nomad, right now, about the person
// in front of him. Goodwill is per (faction, player), it persists, and it is what makes the world
// remember you: shoot enough Sand Tribe and the whole tribe knows, in every camp, after a reload.
//
// Pure and static for the same reason as AggressionMath, and one more. The interesting cases are
// about HISTORY — a value that has drifted for three in-game hours, a band you are sitting exactly
// on the edge of — and the hysteresis rule below is the sort of thing that looks obviously right in
// a diff and flickers in play. It is worth being able to assert it without a world.
//
// Design: docs/superpowers/specs/2026-09-07-faction-system-design.md §3.4.
using System;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// How a faction treats one player. Ordered worst to best so comparisons read naturally
    /// (<c>band &lt;= GoodwillBand.HostileOnSight</c> is "they will shoot me").
    /// </summary>
    public enum GoodwillBand
    {
        /// <summary>Hostile on sight, and a war party is sent after you (WarPartyDirector); ordinary caravans shoot on sight but keep travelling.</summary>
        AtWar,

        /// <summary>Resolves Hostile: they acquire you on sight, the way Wildlife already does.</summary>
        HostileOnSight,

        /// <summary>The authored stance applies unchanged. Where everybody starts.</summary>
        Wary,

        /// <summary>Stance unchanged, but trade and dialogue open up.</summary>
        Friendly,

        /// <summary>Resolves Allied: they alert for you, defend you, and talk warmly.</summary>
        Allied,
    }

    /// <summary>
    /// Where the bands sit for one faction. Serialized on the ledger and overridable per
    /// <see cref="FactionDefinition"/>, because a jumpy tribe should be easier to anger than a
    /// patient one and that is a number, not a code path.
    /// </summary>
    [Serializable]
    public struct GoodwillThresholds
    {
        [Tooltip("At or below this, the tribe hunts this player: a WarPartyDirector-raised party is " +
                 "sent after them. Ordinary caravans of this tribe still shoot on sight but keep " +
                 "travelling.")]
        public float atWar;

        [Tooltip("At or below this, members acquire this player on sight.")]
        public float hostileOnSight;

        [Tooltip("At or above this, trade and dialogue open up. The stance itself does not change.")]
        public float friendly;

        [Tooltip("At or above this, members treat this player as one of their own.")]
        public float allied;

        [Tooltip("How far PAST a boundary the meter must travel to leave a band it has entered.\n\n" +
                 "Not optional. Without it a player sitting on exactly -40 sees every nomad in the " +
                 "camp flip between talking and shooting as the value jitters, which reads as the " +
                 "game being broken rather than as a tribe making up its mind.")]
        public float hysteresis;

        /// <summary>Design §3.4's table. Every faction starts here.</summary>
        public static GoodwillThresholds Default => new GoodwillThresholds
        {
            atWar = -80f,
            hostileOnSight = -40f,
            friendly = 20f,
            allied = 60f,
            hysteresis = 10f,
        };
    }

    public static class GoodwillMath
    {
        /// <summary>The meter's range. Symmetric, so being loved is as far from neutral as being hunted.</summary>
        public const float Min = -100f;

        /// <inheritdoc cref="Min"/>
        public const float Max = 100f;

        /// <summary>Everybody starts here: the authored stance, unmodified.</summary>
        public const float Neutral = 0f;

        /// <summary>
        /// The band <paramref name="value"/> falls in, ignoring where it came from. The raw reading.
        /// </summary>
        public static GoodwillBand RawBandFor(float value, in GoodwillThresholds t)
        {
            if (value <= t.atWar) return GoodwillBand.AtWar;
            if (value <= t.hostileOnSight) return GoodwillBand.HostileOnSight;
            if (value >= t.allied) return GoodwillBand.Allied;
            if (value >= t.friendly) return GoodwillBand.Friendly;

            return GoodwillBand.Wary;
        }

        /// <summary>
        /// The band to show, given the band already being shown. **This is the one to call.**
        ///
        /// <para>
        /// A band is entered **at** its threshold and left only once the meter has travelled
        /// <see cref="GoodwillThresholds.hysteresis"/> past it, back toward neutral. So −40 enters
        /// HostileOnSight at once, and it then takes −29 to get back out — not −39, and not −40
        /// again on the next frame the value jitters.
        /// </para>
        /// <para>
        /// <b>The slack is one-sided: it widens only the edge facing zero.</b> Angering a tribe is
        /// immediate; talking them back down costs ten points more than it took to get there. The
        /// first version of this widened the previous band on BOTH sides, which also delayed
        /// entering — a player at −40 stayed Wary until −50, so every authored threshold quietly
        /// meant something else. Wary straddles zero and gets no slack at all, which is correct: it
        /// is the resting state and nothing about it should be sticky.
        /// </para>
        /// </summary>
        public static GoodwillBand BandFor(float value, GoodwillBand previous, in GoodwillThresholds t)
        {
            GoodwillBand raw = RawBandFor(value, t);
            if (raw == previous)
                return previous;

            return StillIn(previous, value, t) ? previous : raw;
        }

        /// <summary>
        /// Is <paramref name="value"/> still inside <paramref name="band"/>, once that band is given
        /// its slack on the side facing zero? The far side gets none — falling from HostileOnSight
        /// into AtWar happens at the authored threshold, like every other descent.
        /// </summary>
        private static bool StillIn(GoodwillBand band, float value, in GoodwillThresholds t)
        {
            float slack = Mathf.Max(0f, t.hysteresis);

            return band switch
            {
                GoodwillBand.AtWar          => value <= t.atWar + slack,
                GoodwillBand.HostileOnSight => value > t.atWar && value <= t.hostileOnSight + slack,
                GoodwillBand.Wary           => value > t.hostileOnSight && value < t.friendly,
                GoodwillBand.Friendly       => value >= t.friendly - slack && value < t.allied,
                GoodwillBand.Allied         => value >= t.allied - slack,
                _                           => false,
            };
        }

        /// <summary>Add <paramref name="delta"/>, clamped to the meter's range.</summary>
        public static float Apply(float value, float delta) => Mathf.Clamp(value + delta, Min, Max);

        /// <summary>
        /// What one hit costs, as a NEGATIVE number: <paramref name="perHitMin"/> for a scratch
        /// rising to <paramref name="perHitMax"/> for a hit that would have killed outright.
        ///
        /// <para>
        /// Scaled by the fraction of max health rather than by raw damage, because raw damage is
        /// not comparable across targets — 30 points off a nomad and 30 off an Appa are different
        /// events, and a tribe should be angrier about the one that nearly killed somebody.
        /// </para>
        /// <para>
        /// Every landed hit costs at least <paramref name="perHitMin"/>. That floor is deliberate:
        /// it is what stops a player whittling a camp down with a low-damage weapon for free.
        /// </para>
        /// </summary>
        public static float HitDelta(float amount, float maxHealth, float perHitMin, float perHitMax)
        {
            if (amount <= 0f)
                return 0f;

            float fraction = maxHealth > 0f ? Mathf.Clamp01(amount / maxHealth) : 1f;
            return -Mathf.Lerp(Mathf.Abs(perHitMin), Mathf.Abs(perHitMax), fraction);
        }

        /// <summary>
        /// Drift toward zero over <paramref name="hours"/> of in-game time — the "slow decay" half
        /// of design §3.4's recovery (amends are the other half, and they are the ledger's job).
        ///
        /// <para>
        /// Toward zero from BOTH sides, and it never overshoots: a tribe forgets a grudge, and it
        /// also stops doing you favours if you never come back. Decay that could cross zero would
        /// turn an old friendship into an enmity by sitting still, which is not a thing time does.
        /// </para>
        /// </summary>
        public static float Decay(float value, float hours, float ratePerHour)
        {
            if (hours <= 0f || ratePerHour <= 0f || Mathf.Approximately(value, 0f))
                return value;

            float shed = Mathf.Abs(ratePerHour) * hours;
            return value > 0f
                ? Mathf.Max(0f, value - shed)
                : Mathf.Min(0f, value + shed);
        }

        /// <summary>
        /// Does this band mean the tribe shoots this player on sight?
        ///
        /// The one question <c>FactionRelations.Resolve</c> asks, so the comparison lives here
        /// rather than being spelled out at the call site where it can drift.
        /// </summary>
        public static bool IsHostile(GoodwillBand band) => band <= GoodwillBand.HostileOnSight;

        /// <summary>Does this band mean they hunt you, rather than merely shooting you on sight?</summary>
        public static bool IsHunting(GoodwillBand band) => band == GoodwillBand.AtWar;
    }
}
