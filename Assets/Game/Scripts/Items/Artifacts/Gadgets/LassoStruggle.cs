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
        [Tooltip("World-units above the creature's root that the rope ties to. Roughly chest or " +
                 "neck height for whatever is being roped.")]
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

        public float AttachHeight => attachHeight;
        public float StruggleSeconds => Mathf.Max(struggleSeconds, 0.01f);
        public float StruggleSpeed => struggleSpeed;
        public float ThrashFrequency => thrashFrequency;
        public float ThrashShare => thrashShare;
        public float TurnSpeed => turnSpeed;
    }
}
