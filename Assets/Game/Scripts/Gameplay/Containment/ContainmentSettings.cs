// Every number a container is allowed to have an opinion about.
//
// Serialized on the CONTAINER's prefab and handed to the runtime pieces as they are created, for
// the reason SnareStruggle sets out at length: SnareStruggleMeter, ContainmentPull, PulledBody and
// BottledPlayer are all added in code and never authored, so serialized fields on them would be
// constants wearing a costume. A designer tunes a canister; a bigger canister is a prefab variant
// carrying a different rated volume and nothing else.
using UnityEngine;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// The tunables of one container: how much fits, how long filling takes, and how hard a
    /// captive can fight it.
    ///
    /// <para>
    /// The struggle numbers are deliberately the same four
    /// <see cref="SpaceGame.Items.SnareStruggleMeter"/> and
    /// <see cref="SpaceGame.Items.SnareStruggleReader"/> already take from the net gun and the
    /// leash's hogtie. They are authored again here rather than shared with those because they
    /// belong to the device doing the holding — a canister may legitimately be easier or harder to
    /// fight than a net — but the meaning of each is identical, so the tooltips point at the same
    /// design rather than restating it.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class ContainmentSettings
    {
        [Header("What fits")]
        [Tooltip("How much the container is rated for, in cubic metres of the target's collider " +
                 "bounding box.\n\n" +
                 "The measure is deliberately the AABB and not the mesh volume: it is what a " +
                 "physics query can answer for any body in the game, it over-estimates rather " +
                 "than under-estimates (so nothing that reads as too big for the bottle ever " +
                 "fits), and it makes a bigger canister a prefab variant carrying a bigger " +
                 "number rather than new code. 12 m3 takes a creature and an unridden mount and " +
                 "refuses a lander.")]
        [SerializeField, Min(0.01f)] private float ratedVolume = 12f;

        [Header("Filling")]
        [Tooltip("Seconds of continuous use to draw in a target that does not fight back.\n\n" +
                 "This is the whole of the fill time for a loose prop or a sleeping creature. " +
                 "Anything that can struggle takes longer, by the multiplier below.")]
        [SerializeField, Min(0.05f)] private float fillSeconds = 2f;

        [Tooltip("How much of the pull a captive fighting flat out takes away, as a multiple of " +
                 "the whole of it.\n\n" +
                 "At 1 a saturated struggle exactly cancels the draw, so a captive who never " +
                 "stops can never be bottled — which is a promise to the player being aimed at, " +
                 "not a balance knob. Above 1 the progress runs BACKWARDS while they fight, so " +
                 "letting up for a second costs them ground; below 1 a perfect struggle only " +
                 "delays the inevitable.")]
        [SerializeField, Min(0f)] private float struggleMultiplier = 1.15f;

        [Header("The captive's fight")]
        [Tooltip("Struggle inputs per second past which nothing more is gained.\n\n" +
                 "The cap is the design, not a balance knob. Above it a struggle rewards input " +
                 "rate — which excludes anyone who cannot spam a key and rewards anyone who " +
                 "binds an autofire macro (GDC-L1-UX-0006).")]
        [SerializeField, Min(0.1f)] private float maxUsefulStruggleRate = 2.5f;

        [Tooltip("Seconds a struggle takes to fade once the captive stops fighting.")]
        [SerializeField, Min(0.05f)] private float struggleDecaySeconds = 1.2f;

        [Tooltip("How far a captive has to push a direction before it counts as one at all. At " +
                 "0.5 a gamepad pushed half way is steering rather than fighting.")]
        [SerializeField, Range(0.05f, 0.95f)] private float struggleMoveDeadzone = 0.5f;

        [Tooltip("How far round a captive must throw themselves for it to read as a struggle " +
                 "rather than a turn, in degrees. 120 counts mashing A against D and refuses " +
                 "strafing round a corner.")]
        [SerializeField, Range(90f, 179f)] private float struggleReversalAngle = 120f;

        [Header("A bottled player")]
        [Tooltip("The longest a PLAYER can be held inside, in seconds, however hard the captor " +
                 "holds the trigger and however still the captive sits.\n\n" +
                 "A ceiling, not a duration: it runs down on its own. Aimed at a person the " +
                 "container is a short inconvenience and nothing more, which is the whole answer " +
                 "to griefing with it (GDC-L1-MP-0002). A player who puts the controller down " +
                 "still gets out.")]
        [SerializeField, Min(0.5f)] private float containedPlayerSeconds = 5f;

        [Tooltip("How much faster the ceiling runs down for a player mashing flat out, as a " +
                 "multiple of real time.\n\n" +
                 "At 1 a perfect struggle halves the wait: the clock spends 1 + 1 seconds per " +
                 "second. Zero would make mashing pointless, which is worse than it sounds — the " +
                 "point of the input path is that a bottled player has SOMETHING to do, not that " +
                 "it saves them much time.")]
        [SerializeField, Min(0f)] private float containedPlayerStruggleGain = 1f;

        /// <summary>Cubic metres of collider bounding box the container is rated for.</summary>
        public float RatedVolume => Mathf.Max(ratedVolume, 0.01f);

        /// <summary>Seconds to draw in a target that does not resist.</summary>
        public float FillSeconds => Mathf.Max(fillSeconds, 0.05f);

        /// <summary>
        /// What a saturated struggle takes off the draw, as a fraction of the whole draw rate.
        /// Floored at zero here because nothing downstream floors it, and a negative value would
        /// have a struggling captive pull themselves IN faster — the mechanic inverted rather
        /// than mistuned.
        /// </summary>
        public float StruggleMultiplier => Mathf.Max(struggleMultiplier, 0f);

        /// <summary>
        /// Handed straight to <see cref="SpaceGame.Items.SnareStruggleMeter"/>, floor and ceiling
        /// included. Not re-clamped here: the meter bounds this on BOTH sides for a reason this
        /// class cannot see, and a second, weaker copy of that guard is the one nobody revisits.
        /// </summary>
        public float MaxUsefulStruggleRate => maxUsefulStruggleRate;

        /// <summary>Also clamped by the meter itself. See <see cref="MaxUsefulStruggleRate"/>.</summary>
        public float StruggleDecaySeconds => struggleDecaySeconds;

        /// <summary>
        /// A magnitude, not a squared one — <see cref="SpaceGame.Items.SnareStruggleReader"/>
        /// squares it rather than square-rooting the stick every frame.
        /// </summary>
        public float StruggleMoveDeadzone => struggleMoveDeadzone;

        /// <summary>
        /// The reversal angle as a dot product, which is the form the reader can use. Authored in
        /// degrees because a designer tuning "how far round is a struggle" should be typing 120,
        /// not -0.5. A wider angle is a MORE negative dot, so the test is <c>dot &lt; this</c>.
        /// </summary>
        public float StruggleReversalDot => Mathf.Cos(struggleReversalAngle * Mathf.Deg2Rad);

        /// <summary>The hard ceiling on holding a player, in seconds.</summary>
        public float ContainedPlayerSeconds => Mathf.Max(containedPlayerSeconds, 0.5f);

        /// <summary>Extra seconds of ceiling a fully struggling player burns per real second.</summary>
        public float ContainedPlayerStruggleGain => Mathf.Max(containedPlayerStruggleGain, 0f);
    }
}
