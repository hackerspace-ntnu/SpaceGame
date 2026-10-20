using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// What put a body on the ground. Travels in <c>NetMsg.Knockdown</c>'s <c>B</c>.
    ///
    /// <para>
    /// Damage is deliberately not a cause: a body goes down only to a specific event, or to death.
    /// Value 1 was <c>Hit</c>, removed 2026-10-12 so that taking damage never ragdolls anybody; it
    /// is left unused rather than renumbering the wire.
    /// </para>
    /// </summary>
    public enum RagdollCause : byte
    {
        /// <summary>A shock wave — the gauntlet, the singularity. Always knocks down.</summary>
        Blast = 2,

        /// <summary>A landing hard enough to deal fall damage.</summary>
        Fall = 3,
    }

    /// <summary>The facts a knockdown is priced from, gathered by the machine that decides it.</summary>
    public readonly struct KnockdownEvent
    {
        public readonly RagdollCause Cause;

        /// <summary>Damage the event deals outside <c>HealthComponent</c>, as a share of max health, 0..1.</summary>
        public readonly float DamageFraction;

        /// <summary>Health left when the event lands, as a share of max health, 0..1. 1 for a body with no health.</summary>
        public readonly float HealthLeftFraction;

        /// <summary>
        /// Speed of the impulse the event carries, m/s — a blast's fling; 0 for a fall.
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
    /// Field initialisers are the agreed defaults: fall 0.1 s, blasts 0.1–0.2 s — a tenth of the
    /// first cut (2026-09-24), which playtesting found kept bodies down far too long. The settle
    /// grace stays 1.5 s (2026-10-04): cut with the rest, it stood every knockdown up mid-fall, and
    /// the half-collapse-and-snap-back read as the bodies spasming.
    /// </summary>
    [Serializable]
    public sealed class KnockdownTuning
    {
        [Tooltip("Seconds down after a landing that dealt fall damage.")]
        public float fallSeconds = 0.1f;

        [Tooltip("Seconds down for the lightest blast.")]
        public float minSeconds = 0.1f;

        [Tooltip("Seconds down for the hardest.")]
        public float maxSeconds = 0.2f;

        [Tooltip("Severity at which a knockdown reaches maxSeconds.")]
        public float fullSeverity = 1f;

        [Tooltip("Severity per share of max health dealt by the event itself.")]
        public float damageWeight = 1f;

        [Tooltip("Severity per share of max health MISSING when the event lands. A wounded body " +
                 "stays down longer.")]
        public float lowHealthWeight = 0.5f;

        [Tooltip("Severity for knockback at the reference speed.")]
        public float knockbackWeight = 1f;

        [Tooltip("Knockback speed, m/s, that counts as full knockback.")]
        public float knockbackReferenceSpeed = 20f;

        [Tooltip("Once the down-time is up, how much longer to wait for a still-tumbling body to " +
                 "come to rest before standing it up anyway, seconds. Long enough for a body knocked " +
                 "over from standing to land (about 0.4 s to fall from the hips, more to topple): a " +
                 "body that is at rest stands up at once, so this only ever delays a body still " +
                 "falling. The ceiling that keeps one wedged against a rock from never getting up " +
                 "(GDC-L1-FEEL-0002).")]
        public float settleGraceSeconds = 1.5f;
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

        /// <summary>Seconds down. Every cause knocks down; only how long it lasts varies.</summary>
        public static float Seconds(in KnockdownEvent e, KnockdownTuning t)
        {
            if (e.Cause == RagdollCause.Fall) return t.fallSeconds;

            float full = Mathf.Max(t.fullSeverity, 1e-3f);
            return Mathf.Lerp(t.minSeconds, t.maxSeconds, Mathf.InverseLerp(0f, full, Severity(e, t)));
        }

        public static bool ShouldStandUp(float now, float standAt, bool atRest, float graceSeconds) =>
            now >= standAt && (atRest || now >= standAt + graceSeconds);
    }
}
