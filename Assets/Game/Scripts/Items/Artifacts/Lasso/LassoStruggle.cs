using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How hard a roped creature fights, and for how long.
    ///
    /// <para>
    /// Lives here rather than on <see cref="LassoTether"/> because the tether is added at runtime
    /// and never authored — serialized fields on a component nobody can select in the Inspector are
    /// constants wearing a costume. These are the numbers a designer actually wants to move, so
    /// they are serialized on the lasso prefab and handed to the tether when the rope lands.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoStruggle
    {
        [Tooltip("Where up the creature the rope ties, as a fraction of its own height. 0.75 is " +
                 "about the neck on most things.\n\n" +
                 "A fraction rather than metres because the rope has to tie to the ANIMAL, not to " +
                 "a number: a flat 1.2 m hung the collar in mid-air above anything small and " +
                 "around the shin of a six-legged habitat. Measured from the creature's largest " +
                 "solid collider — see LassoTether.AttachHeightFor.")]
        [SerializeField, Range(0.1f, 1f)] private float attachFraction = 0.75f;

        [Tooltip("Metres above the root to tie at when the creature's height cannot be measured — " +
                 "nothing solid to read, or a chunk whose colliders have not built yet. A " +
                 "person-sized fallback, and only ever a fallback.")]
        [SerializeField] private float attachHeight = 1.2f;

        [Tooltip("Seconds of fight in a fresh animal. It pulls at full strength at the start and " +
                 "nothing at the end — a creature that fights forever is not caught, it is a tug " +
                 "of war with no result.")]
        [SerializeField] private float struggleSeconds = 6f;

        [Tooltip("Metres per second a fresh, unencumbered creature can pull against the rope.")]
        [SerializeField] private float struggleSpeed = 4.5f;

        [Tooltip("How fast it throws its weight side to side, in cycles per second. Without this " +
                 "the struggle is a straight tug-of-war along one line, which reads as a physics " +
                 "constraint rather than an animal.")]
        [SerializeField] private float thrashFrequency = 2.2f;

        [Tooltip("How much of the pull is sideways thrash rather than straight retreat, 0-1.")]
        [SerializeField, Range(0f, 1f)] private float thrashShare = 0.45f;

        [Tooltip("Degrees per second the creature turns to face where it is pulling.")]
        [SerializeField] private float turnSpeed = 220f;

        [Tooltip("Seconds of slack that give back one second of fight. A roped animal that has " +
                 "been let some line gets its wind back, which is what stops the catch ending the " +
                 "moment the struggle clock runs out and never resuming.")]
        [SerializeField] private float recoverySeconds = 4f;

        public float AttachFraction => Mathf.Clamp(attachFraction, 0.1f, 1f);
        public float AttachHeight => attachHeight;
        public float RecoverySeconds => Mathf.Max(recoverySeconds, 0.1f);
        public float StruggleSeconds => Mathf.Max(struggleSeconds, 0.01f);
        public float StruggleSpeed => struggleSpeed;
        public float ThrashFrequency => thrashFrequency;
        public float ThrashShare => thrashShare;
        public float TurnSpeed => turnSpeed;
    }
}
