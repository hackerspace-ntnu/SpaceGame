using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How hard a netted thing fights, how far it can shuffle, and for how long the net lasts.
    ///
    /// <para>
    /// Lives here rather than on <see cref="SnareTether"/> for the reason
    /// <see cref="LassoStruggle"/> gives: the tether is added at runtime and never authored, and
    /// serialized fields on a component nobody can select in the Inspector are constants wearing a
    /// costume. These are the numbers a designer actually moves, so they are serialized on the gun
    /// prefab and handed to each tether as the net lands.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class SnareStruggle
    {
        [Tooltip("Seconds an ordinary single captive is held before the net rots away.\n\n" +
                 "It means exactly that: one captive of SnareIntegrity.ReferenceLoad mass is held " +
                 "for this long. Three of them share the same pool and get a third of it each, " +
                 "which is what stops a wide net being strictly better than a careful shot.")]
        [SerializeField, Min(0.01f)] private float holdSeconds = 30f;

        [Tooltip("Metres a captive may wander from where the net landed. Not zero: a captive frozen " +
                 "on the spot reads as the animation being switched off, which is the mistake " +
                 "LassoTether's own docs describe replacing.")]
        [SerializeField, Min(0.01f)] private float shuffleRadius = 1.4f;

        [Tooltip("Fraction of its normal speed a netted thing may move at.")]
        [SerializeField, Range(0.05f, 1f)] private float hobbleSpeed = 0.28f;

        [Tooltip("How fast it throws its weight about, in cycles per second. Without this the " +
                 "struggle is a straight tug along one line, which reads as a constraint rather " +
                 "than as an animal.")]
        [SerializeField] private float thrashFrequency = 2.4f;

        [Tooltip("How much of the pull is sideways thrash rather than straight retreat, 0-1. At 0 " +
                 "thrashFrequency above does nothing at all and a captive pulls in a dead straight " +
                 "line away from the net.")]
        [SerializeField, Range(0f, 1f)] private float thrashShare = 0.45f;

        [Tooltip("Metres the net's own centre is dragged per second at full thrash. This is what " +
                 "makes a captive drag the net with it rather than walk out from under it.")]
        [SerializeField] private float dragInfluence = 0.9f;


        public float HoldSeconds => Mathf.Max(holdSeconds, 0.01f);
        public float ShuffleRadius => Mathf.Max(shuffleRadius, 0.01f);
        public float HobbleSpeed => hobbleSpeed;
        public float ThrashFrequency => thrashFrequency;
        public float ThrashShare => thrashShare;
        public float DragInfluence => dragInfluence;
    }
}
