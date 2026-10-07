using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How long a tie lasts, and what fighting it is worth.
    ///
    /// <para>
    /// Lives here rather than on <see cref="Hogtie"/> for the reason <see cref="SnareStruggle"/>
    /// gives for itself: a tie is added at runtime and never authored, and serialized fields on a
    /// component nobody can select in the Inspector are constants wearing a costume. These are the
    /// numbers a designer moves, so they are serialized on the leash prefab and handed to each tie
    /// as it lands.
    /// </para>
    /// <para>
    /// <b>Not <see cref="SnareStruggle"/> reused, and that was the first thing tried.</b> The two
    /// share four field names and every one of them wants a different number: the tie's ceiling is
    /// four times the net's, its multiplier is derived against that ceiling, and the whole of
    /// <c>SnareStruggle.HobbleSpeed</c> describes a fallback a tie does not have (a tie refuses a
    /// body it cannot fell rather than hobbling it, because a body that is already down has no
    /// speed left to cap). Reusing it would mean the leash prefab had to override every field it
    /// inherited — and the leash prefab already exists on disk, so an un-overridden field would
    /// silently ship the net's thirty seconds against the user's stated two minutes. The
    /// BEHAVIOUR is reused rather than the field list: a tie runs the same
    /// <see cref="SnareIntegrity"/> pool and the same <see cref="SnareStruggleMeter"/>.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class HogtieSettings
    {
        [Tooltip("Seconds a tied body that does NOT fight stays tied.\n\n" +
                 "The user's stated ceiling: two minutes. It means exactly that — one tied body " +
                 "lying still is held for this long and then the ropes come off by themselves. A " +
                 "body that fights gets out sooner, by the multiplier below.")]
        [SerializeField, Min(0.01f)] private float holdSeconds = 120f;

        [Tooltip("Struggle inputs per second past which nothing more is gained.\n\n" +
                 "The same cap the net uses and for the same reason: above it a struggle rewards " +
                 "input rate, which excludes anyone who cannot spam a key and rewards anyone who " +
                 "binds an autofire macro (GDC-L1-UX-0006).")]
        [SerializeField, Min(0.1f)] private float maxUsefulStruggleRate = 2.5f;

        [Tooltip("Seconds a struggle takes to fade once the body stops fighting.")]
        [SerializeField, Min(0.05f)] private float struggleDecaySeconds = 1.2f;

        [Tooltip("How far a body has to push a direction before it counts as one at all.")]
        [SerializeField, Range(0.05f, 0.95f)] private float struggleMoveDeadzone = 0.5f;

        [Tooltip("How far round a tied body must throw themselves for it to read as a struggle " +
                 "rather than a turn, in degrees.")]
        [SerializeField, Range(90f, 179f)] private float struggleReversalAngle = 120f;

        [Tooltip("Extra load a body struggling flat out puts on the ropes, as a multiple of one " +
                 "still body.\n\n" +
                 "1.96 is DERIVED, not picked: see Hogtie's class summary for the arithmetic. It " +
                 "buys a perfect struggle out of the 120 s ceiling in about 45 s. Raising it " +
                 "shortens only the struggled escape; the untouched ceiling above is what a " +
                 "still body gets either way.")]
        [SerializeField, Min(0f)] private float struggleMultiplier = 1.96f;

        public float HoldSeconds => Mathf.Max(holdSeconds, 0.01f);

        /// <summary>
        /// Handed straight to <see cref="SnareStruggleMeter"/>, floor and ceiling included — the
        /// meter bounds this on both sides for reasons this class cannot see, and a second, weaker
        /// copy of that guard here would be the one nobody revisits when the meter's change.
        /// </summary>
        public float MaxUsefulStruggleRate => maxUsefulStruggleRate;

        /// <summary>Also clamped by the meter itself. See <see cref="MaxUsefulStruggleRate"/>.</summary>
        public float StruggleDecaySeconds => struggleDecaySeconds;

        /// <summary>
        /// A magnitude, not a squared one — the reader squares it rather than square-rooting the
        /// stick every frame.
        /// </summary>
        public float StruggleMoveDeadzone => struggleMoveDeadzone;

        /// <summary>
        /// The reversal angle as a dot product, which is the form the one reader can use.
        ///
        /// Authored in degrees and converted here rather than authored as a cosine: a designer
        /// tuning "how far round is a struggle" should be typing 120, not -0.5. The sign works out
        /// the way it reads — a wider angle is a MORE negative dot, so the test is `dot &lt; this`.
        /// </summary>
        public float StruggleReversalDot => Mathf.Cos(struggleReversalAngle * Mathf.Deg2Rad);

        /// <summary>
        /// Floored at zero here, because nothing downstream floors it. A negative multiplier would
        /// have a struggling body drain the ropes SLOWER than a still one — a tie that lasts longer
        /// the harder it is fought, which is the mechanic inverted rather than mistuned.
        /// </summary>
        public float StruggleMultiplier => Mathf.Max(struggleMultiplier, 0f);
    }
}
