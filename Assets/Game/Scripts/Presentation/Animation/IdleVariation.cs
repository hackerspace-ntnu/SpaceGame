using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Keeps a crowd of the same body out of lockstep: each body stands in its own idle, changes
    /// it now and then, and walks at its own point in the stride cycle.
    ///
    /// <para>
    /// Without it every NPC of a prefab plays idle 0 forever and, stepping off together, swings the
    /// same leg on the same frame — the tell that a crowd is one clip. The Base Layer's idle blend
    /// reads <c>IdleIndex</c> and its locomotion state reads <c>CycleOffset</c>
    /// (GDC-L1-ANIM-0005: life is in the secondary motion).
    /// </para>
    /// <para>
    /// <b>Multiplayer.</b> Nothing is sent. Which idle is showing is a pure function of the body's
    /// <see cref="CharacterActions.Seed"/> and the network's shared server clock, so every machine
    /// that writes this body lands on the same idle at the same moment. Who writes follows
    /// <see cref="AnimatorAuthority"/>: every machine for an NPC, the owner for the player (NGO
    /// carries the parameters from there).
    /// </para>
    /// <para><b>Persistence:</b> none — it is recomputed from the clock.</para>
    /// </summary>
    [RequireComponent(typeof(CharacterActions))]
    public sealed class IdleVariation : MonoBehaviour
    {
        [Tooltip("Optional. Found on this object or its children when empty.")]
        [SerializeField] private Animator animator;

        [Tooltip("Seconds a body stands in one idle before it may pick another.")]
        [SerializeField, Min(1f)] private float secondsPerIdle = 14f;

        [Tooltip("Seconds to blend from one idle to the next.")]
        [SerializeField, Min(0.01f)] private float blendSeconds = 0.8f;

        [Tooltip("Seconds between chances to fidget while standing idle. Whether it does, and with " +
                 "what, is the IdleFidget row of the reaction table.")]
        [SerializeField, Min(1f)] private float secondsPerFidget = 9f;

        private CharacterActions actions;
        private BodyLanguage body;
        private float shown;
        private long fidgetBucket = long.MinValue;

        private void Awake()
        {
            actions = GetComponent<CharacterActions>();
            body = BodyLanguage.Of(this);
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
        }

        private void Start()
        {
            if (!Writes()) return;

            // Once, before the first evaluation: the state's cycle offset read later would jump the
            // walk to a new phase mid-stride.
            animator.SetFloat(HumanoidParams.CycleOffsetHash, Fraction(actions.Seed));
        }

        private void Update()
        {
            if (!Writes()) return;

            Fidget();

            CharacterActionCatalog catalog = CharacterActionCatalog.Default;
            int count = catalog != null ? catalog.IdleVariantCount : 1;
            if (count <= 1) return;

            int target = IdleAt(actions.Seed, Now(), secondsPerIdle, count);
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime / blendSeconds);
            animator.SetFloat(HumanoidParams.IdleIndexHash, shown);
        }

        /// <summary>
        /// Once per <see cref="secondsPerFidget"/> of the shared clock, raise
        /// <see cref="CharacterMoment.IdleFidget"/> if the body stands idle with nothing playing.
        /// The clock bucket is the roll's salt, so every machine writing this body makes the same
        /// call on the same beat; the first bucket after spawn only arms it.
        /// </summary>
        private void Fidget()
        {
            if (body == null) return;

            long bucket = Bucket(actions.Seed, Now(), secondsPerFidget);
            if (bucket == fidgetBucket) return;

            bool armed = fidgetBucket != long.MinValue;
            fidgetBucket = bucket;
            if (!armed || body.Posture != BodyPosture.Standing) return;
            if (actions.PlayingOn(CharacterAction.Slot.Full) != null || actions.PlayingOn(CharacterAction.Slot.Upper) != null) return;

            body.React(CharacterMoment.IdleFidget, unchecked((int)bucket));
        }

        /// <summary>
        /// The idle body <paramref name="seed"/> stands in at <paramref name="now"/>. Each body's
        /// changes are offset within the period by its seed, so a crowd does not all shift weight
        /// on the same beat.
        /// </summary>
        public static int IdleAt(int seed, double now, float secondsPerIdle, int count)
        {
            long bucket = Bucket(seed, now, secondsPerIdle);
            uint h = unchecked((uint)seed * 2654435761u ^ (uint)bucket * 2246822519u);
            h ^= h >> 13;
            return (int)(h % (uint)count);
        }

        /// <summary>Which <paramref name="period"/>-long slice of the clock <paramref name="now"/> is in, offset per body.</summary>
        private static long Bucket(int seed, double now, float period) =>
            (long)System.Math.Floor((now + Fraction(seed) * period) / period);

        private static float Fraction(int seed) => (unchecked((uint)seed * 2654435761u) >> 8) / (float)(1 << 24);

        private static double Now()
        {
            NetworkManager network = NetworkManager.Singleton;
            return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
        }

        private bool Writes() =>
            animator != null && animator.runtimeAnimatorController != null && actions.WritesAnimator;
    }
}
