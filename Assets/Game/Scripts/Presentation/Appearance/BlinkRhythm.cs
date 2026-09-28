using System;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// When a face blinks and how shut its lids are while it does. Plain state advanced by the
    /// caller, so the timing is testable without a scene and <see cref="EyeBlink"/> is left with
    /// nothing to decide.
    ///
    /// <para>
    /// The shape of one blink is a real one's, cartooned: a fast close that settles into the shut
    /// pose, a beat shut, and a slower open that peels away from it. The waits between blinks are
    /// drawn afresh every time, because a fixed period reads as a machine within three blinks
    /// (GDC-L1-ANIM-0001: timing is what sells it as alive).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BlinkRhythm
    {
        [Tooltip("Shortest wait between one blink and the next, in seconds.")]
        [SerializeField, Min(0.1f)] private float minInterval = 1f;

        [Tooltip("Longest wait between one blink and the next, in seconds.")]
        [SerializeField, Min(0.1f)] private float maxInterval = 3f;

        [Tooltip("Seconds for the lids to come together. Eases out, so they land softly.")]
        [SerializeField, Min(0.01f)] private float closeTime = 0.08f;

        [Tooltip("Seconds the eye stays fully shut.")]
        [SerializeField, Min(0f)] private float shutTime = 0.05f;

        [Tooltip("Seconds for the lids to part again. Slower than the close, as a real blink is.")]
        [SerializeField, Min(0.01f)] private float openTime = 0.15f;

        [Tooltip("Chance that a blink is followed at once by a second one.")]
        [SerializeField, Range(0f, 1f)] private float doubleBlinkChance = 0.25f;

        [Tooltip("Seconds between the two blinks of a double blink.")]
        [SerializeField, Min(0f)] private float doubleBlinkGap = 0.08f;

        [NonSerialized] private bool started;
        [NonSerialized] private float untilNextBlink;
        [NonSerialized] private float intoBlink = -1f;
        [NonSerialized] private bool secondBlinkQueued;

        private float BlinkLength => closeTime + shutTime + openTime;

        /// <summary>
        /// Moves the clock on by <paramref name="deltaTime"/> and says how shut the eyes are now:
        /// 0 wide open, 1 shut.
        /// </summary>
        public float Advance(float deltaTime, System.Random random)
        {
            // A random point in the first wait, so a band spawned together does not blink together.
            if (!started)
            {
                started = true;
                untilNextBlink = NextInterval(random) * (float)random.NextDouble();
            }

            if (intoBlink < 0f)
            {
                untilNextBlink -= deltaTime;
                if (untilNextBlink > 0f) return 0f;

                intoBlink = -untilNextBlink;
                secondBlinkQueued = random.NextDouble() < doubleBlinkChance;
            }
            else
            {
                intoBlink += deltaTime;
            }

            if (intoBlink < BlinkLength)
                return Closure(intoBlink);

            float overshoot = intoBlink - BlinkLength;
            intoBlink = -1f;
            untilNextBlink = (secondBlinkQueued ? doubleBlinkGap : NextInterval(random)) - overshoot;
            secondBlinkQueued = false;
            return 0f;
        }

        /// <summary>Forgets any blink in progress; the next <see cref="Advance"/> starts a fresh wait.</summary>
        public void Restart()
        {
            started = false;
            intoBlink = -1f;
            secondBlinkQueued = false;
        }

        private float Closure(float t)
        {
            if (t < closeTime)
            {
                float s = 1f - t / closeTime;
                return 1f - s * s;
            }

            t -= closeTime;
            if (t < shutTime) return 1f;

            float opened = (t - shutTime) / openTime;
            return 1f - opened * opened;
        }

        private float NextInterval(System.Random random) =>
            Mathf.Lerp(minInterval, Mathf.Max(minInterval, maxInterval), (float)random.NextDouble());
    }
}
