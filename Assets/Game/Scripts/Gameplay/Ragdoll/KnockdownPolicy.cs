using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>What put a body on the ground. Travels in <c>NetMsg.Knockdown</c>'s <c>B</c>.</summary>
    public enum RagdollCause : byte
    {
        /// <summary>Damage. Knocks down only past a severity threshold.</summary>
        Hit = 1,

        /// <summary>A shock wave — the gauntlet, the singularity. Always knocks down.</summary>
        Blast = 2,

        /// <summary>A landing hard enough to deal fall damage.</summary>
        Fall = 3,
    }

    /// <summary>The facts a knockdown is priced from, gathered by the machine that decides it.</summary>
    public readonly struct KnockdownEvent
    {
        public readonly RagdollCause Cause;

        /// <summary>Damage dealt as a share of max health, 0..1.</summary>
        public readonly float DamageFraction;

        /// <summary>Health left AFTER the hit as a share of max health, 0..1. 1 for a body with no health.</summary>
        public readonly float HealthLeftFraction;

        /// <summary>
        /// Speed of the impulse the event carries, m/s. A hit carries its controller's
        /// <c>hitImpulse</c>, so every hit is priced with that much knockback; 0 for a fall.
        /// </summary>
        public readonly float KnockbackSpeed;

        public KnockdownEvent(RagdollCause cause, float damageFraction, float healthLeftFraction,
                              float knockbackSpeed)
        {
            Cause = cause;
            DamageFraction = damageFraction;
            HealthLeftFraction = healthLeftFraction;
            KnockbackSpeed = knockbackSpeed;
        }
    }

    /// <summary>
    /// The knobs, one set per body prefab so a boss and a rat can price the same blast differently.
    /// Field initialisers are the agreed defaults (2026-09-24): fall 1 s, hits 1–2 s.
    /// </summary>
    [Serializable]
    public sealed class KnockdownTuning
    {
        [Tooltip("Seconds down after a landing that dealt fall damage.")]
        public float fallSeconds = 1f;

        [Tooltip("Seconds down for the lightest hit or blast that knocks down at all.")]
        public float minSeconds = 1f;

        [Tooltip("Seconds down for the hardest.")]
        public float maxSeconds = 2f;

        [Tooltip("Severity a HIT must reach to knock down. Below it the body flinches and keeps " +
                 "going — the reason automatic fire does not stun-lock. Blasts ignore it.")]
        public float threshold = 0.35f;

        [Tooltip("Severity at which a knockdown reaches maxSeconds.")]
        public float fullSeverity = 1f;

        [Tooltip("Severity per share of max health dealt by the hit.")]
        public float damageWeight = 1f;

        [Tooltip("Severity per share of max health MISSING after the hit. A wounded body goes down " +
                 "easier and stays down longer.")]
        public float lowHealthWeight = 0.5f;

        [Tooltip("Severity for knockback at the reference speed.")]
        public float knockbackWeight = 1f;

        [Tooltip("Knockback speed, m/s, that counts as full knockback.")]
        public float knockbackReferenceSpeed = 20f;

        [Tooltip("Once the down-time is up, how much longer to wait for a still-tumbling body to " +
                 "come to rest before standing it up anyway, seconds. The ceiling that keeps a body " +
                 "wedged against a rock from never getting up (GDC-L1-FEEL-0002).")]
        public float settleGraceSeconds = 1.5f;

        [Tooltip("Seconds after standing up during which HITS cannot knock the body down again. " +
                 "0 disables. Blasts and falls ignore it.")]
        public float reknockImmunitySeconds = 1f;
    }

    /// <summary>
    /// How long a body stays down, decided from what put it there — pure, so the numbers can be
    /// tested and tuned without a scene.
    ///
    /// <para>
    /// Down-time is committed resolution in the GDC-L1-FEEL-0008 sense: the player is heard at once
    /// (the body reacts the frame the event lands) and the world then takes a bounded, legible time
    /// to hand control back. Bounded both ways — a floor so a knockdown reads as one
    /// (GDC-L1-FEEL-0007), a ceiling so it never becomes lost control (GDC-L1-FEEL-0002).
    /// </para>
    /// </summary>
    public static class KnockdownPolicy
    {
        public static float Severity(in KnockdownEvent e, KnockdownTuning t)
        {
            float knockback = Mathf.Clamp01(e.KnockbackSpeed / Mathf.Max(t.knockbackReferenceSpeed, 0.01f));

            return t.damageWeight * Mathf.Clamp01(e.DamageFraction)
                   + t.lowHealthWeight * (1f - Mathf.Clamp01(e.HealthLeftFraction))
                   + t.knockbackWeight * knockback;
        }

        /// <summary>Seconds down, or 0 for an event that does not knock down.</summary>
        public static float Seconds(in KnockdownEvent e, KnockdownTuning t)
        {
            if (e.Cause == RagdollCause.Fall) return t.fallSeconds;

            float severity = Severity(e, t);
            if (e.Cause == RagdollCause.Hit && severity < t.threshold) return 0f;

            float full = Mathf.Max(t.fullSeverity, t.threshold + 1e-3f);
            return Mathf.Lerp(t.minSeconds, t.maxSeconds, Mathf.InverseLerp(t.threshold, full, severity));
        }

        public static bool Immune(RagdollCause cause, float secondsSinceStoodUp, KnockdownTuning t) =>
            cause == RagdollCause.Hit && secondsSinceStoodUp < t.reknockImmunitySeconds;

        public static bool ShouldStandUp(float now, float standAt, bool atRest, float graceSeconds) =>
            now >= standAt && (atRest || now >= standAt + graceSeconds);
    }
}
