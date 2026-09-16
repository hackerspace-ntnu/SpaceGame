// How close an agent is to turning on you, as a number instead of a coin flip.
//
// ProvocationModule used to be binary: one hit over the threshold and a peaceful nomad was
// fighting you, with nothing in between. That is unreadable in play — the player has no way to
// learn what the rule is, because the only two states they ever see are "ignoring me" and
// "shooting me" (GDC-L1-SYS-0006: you may hide the rule, but you owe the player the feedback).
// The meter is what puts states in between, so the bands below can be shown as posture and barks.
//
// Everything here is pure and static. The reason is the usual one and it is worth stating: the
// interesting cases are all about ACCUMULATION over time — seven gunshots inside a cooling window,
// a hit that is big enough and one that is not — and those are miserable to test through a
// MonoBehaviour and trivial to test as arithmetic. ProvocationModule owns the state; this file
// owns the rules.
using System;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>What just happened that an agent might take badly. See the table in design §3.3.</summary>
    public enum AggressionInput
    {
        /// <summary>This agent was damaged. Magnitude is the fraction of its max health.</summary>
        Hit,

        /// <summary>An ally within the alert radius was hurt. Arrives through AlertReceiverModule.</summary>
        AllyHurt,

        /// <summary>A gun went off in earshot. Arrives through NoiseReceiverModule; needs no line of sight.</summary>
        Gunshot,

        /// <summary>A weapon is being pointed at this agent. Magnitude is seconds.</summary>
        Menace,

        /// <summary>Standing in this faction's territory while unwelcome. Magnitude is seconds.</summary>
        Trespass,
    }

    /// <summary>
    /// How near the top the meter is, and therefore what the agent shows. The player never sees
    /// the number — they see the band.
    /// </summary>
    public enum AggressionBand
    {
        /// <summary>Gets on with its errand.</summary>
        Calm,

        /// <summary>Watches you and says something.</summary>
        Wary,

        /// <summary>Weapon up, planted, last warning. Will not talk.</summary>
        Drawn,

        /// <summary>The fight. This is the old binary behaviour, unchanged, at the top of the meter.</summary>
        Grudge,
    }

    /// <summary>
    /// What one agent's temperament is worth in points. Serialized on <see cref="ProvocationModule"/>
    /// so every number is tunable per prefab — the per-tribe flavour in design §3.3 is entirely in
    /// these values, not in code: a Sand nomad forgives a gunshot quickly, an Outlaw notices an
    /// aimed gun from further away.
    /// </summary>
    [Serializable]
    public struct AggressionSettings
    {
        [Tooltip("Points for being damaged, multiplied by the fraction of max health the hit took. " +
                 "The default is high on purpose: a solid hit is still an instant fight, which is " +
                 "what this component did before it had a meter at all. Chip damage is not.")]
        public float hitGain;

        [Tooltip("Points for an ally being hurt inside the alert radius. Two of these is a fight.")]
        public float allyHurtGain;

        [Tooltip("Points per gunshot heard. Low enough that hunting nearby is not a declaration of " +
                 "war, high enough that emptying a magazine into the air is.")]
        public float gunshotGain;

        [Tooltip("Points per second while a weapon is pointed at this agent from inside menaceRange.")]
        public float menaceGainPerSecond;

        [Tooltip("How far off a player can be aiming and still be read as pointing a gun at this agent.")]
        public float menaceRange;

        [Tooltip("Seconds of being aimed at before menace starts counting at all. Without it, " +
                 "sweeping the camera across a camp menaces everyone in it.")]
        public float menaceDelay;

        [Tooltip("Points per second while standing in this faction's territory unwelcome (§3.10).")]
        public float trespassGainPerSecond;

        [Tooltip("Points shed per second. This is forgiveness: how long the agent stays wary after " +
                 "you stop. At 10 a full meter is empty in ten seconds.")]
        public float calmRate;

        [Tooltip("The meter reading that starts the fight. 100 unless a creature should snap early.")]
        public float attackAt;

        /// <summary>
        /// The temperament every prefab starts from: a solid hit fights, a scratch does not, and
        /// the agent forgets in about ten seconds.
        /// </summary>
        public static AggressionSettings Default => new AggressionSettings
        {
            // Calibrated against what this component did BEFORE it had a meter, which is the
            // behaviour the design asks to keep: any real hit starts a fight. At 1200 the crossover
            // is a hit worth a twelfth of max health — 9 damage on a 100 HP creature fights, which
            // is what ProvocationTests has always called "a real hit" — while a 5 % graze is worth
            // 60 and only makes the agent wary. Two grazes are still a fight; that is the meter
            // earning its keep rather than softening anything.
            hitGain = 1200f,
            allyHurtGain = 40f,
            gunshotGain = 15f,
            menaceGainPerSecond = 20f,
            menaceRange = 12f,
            menaceDelay = 1.5f,
            trespassGainPerSecond = 10f,
            calmRate = 10f,
            attackAt = AggressionMath.Max,
        };
    }

    public static class AggressionMath
    {
        /// <summary>Full. Nothing above this is tracked — the meter is a percentage, not a tally.</summary>
        public const float Max = 100f;

        /// <summary>The agent starts watching you.</summary>
        public const float WaryAt = 40f;

        /// <summary>The agent raises its weapon and stops talking.</summary>
        public const float DrawnAt = 80f;

        /// <summary>
        /// What <paramref name="input"/> is worth, for a <paramref name="magnitude"/> that means
        /// whatever that input says it means — a damage fraction, a number of seconds, or a count.
        ///
        /// Returns a DELTA rather than a new value, so the caller can log it, halve it, or ignore
        /// it; <see cref="Apply"/> is the one that moves the meter.
        /// </summary>
        public static float Gain(AggressionInput input, float magnitude, in AggressionSettings settings)
        {
            if (magnitude <= 0f)
                return 0f;

            return input switch
            {
                AggressionInput.Hit      => settings.hitGain * magnitude,
                AggressionInput.AllyHurt => settings.allyHurtGain * magnitude,
                AggressionInput.Gunshot  => settings.gunshotGain * magnitude,
                AggressionInput.Menace   => settings.menaceGainPerSecond * magnitude,
                AggressionInput.Trespass => settings.trespassGainPerSecond * magnitude,
                _                        => 0f,
            };
        }

        /// <summary>Add <paramref name="delta"/> to the meter, clamped to [0, <see cref="Max"/>].</summary>
        public static float Apply(float value, float delta) => Mathf.Clamp(value + delta, 0f, Max);

        /// <summary>
        /// Forgiveness. Sheds <paramref name="calmRate"/> points per second, never below zero.
        ///
        /// The caller decides WHEN to cool: above <c>attackAt</c> the old leash rule applies
        /// instead, because a creature you are standing on top of must not calm down on a timer.
        /// </summary>
        public static float Cool(float value, float deltaTime, float calmRate)
        {
            if (deltaTime <= 0f || calmRate <= 0f)
                return value;

            return Mathf.Max(0f, value - calmRate * deltaTime);
        }

        /// <summary>
        /// Which band <paramref name="value"/> falls in. <paramref name="attackAt"/> is the top of
        /// the meter for this agent, so a creature that snaps at 60 still shows all three bands on
        /// the way there rather than skipping the telegraph.
        /// </summary>
        public static AggressionBand BandFor(float value, float attackAt = Max)
        {
            if (value >= Mathf.Min(attackAt, Max))
                return AggressionBand.Grudge;

            // Scaled against attackAt, not against 100: an agent whose fight starts at 60 should
            // draw its weapon at 80 % of 60, not sit calm until it is two points from attacking.
            //
            // The scale is applied to the THRESHOLDS rather than folded into a ratio, so the
            // ordinary attackAt == 100 case multiplies by exactly 1f. Computing `span * (WaryAt /
            // Max)` instead put 40f up against 40.000002f and quietly reported Calm at the exact
            // boundary — the bands would have been one ulp late for every agent in the game.
            float scale = Mathf.Max(1f, Mathf.Min(attackAt, Max)) / Max;

            if (value >= DrawnAt * scale) return AggressionBand.Drawn;
            if (value >= WaryAt * scale) return AggressionBand.Wary;

            return AggressionBand.Calm;
        }
    }
}
