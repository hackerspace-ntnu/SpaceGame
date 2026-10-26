using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// A little glass bottle of rue a keeper lobs at the witch: it arcs to where she stood when it
    /// was thrown and shatters there.
    ///
    /// <para>
    /// <b>Slow, and aimed at where she WAS.</b> The flight takes the better part of a second and she
    /// does not stand still, so a bomb thrown at a free witch lands on empty grass — the keepers are
    /// visibly trying and missing, which is what tells the player their job is to hold her still
    /// (GDC-L1-NARR-0001). A frozen witch is still there when it lands.
    /// </para>
    /// <para>
    /// <b>It does not decide what it burns.</b> Landing within <see cref="splashRadius"/> of its
    /// target bills <see cref="DamageKind.Rue"/>; the witch's <c>HerbBoundLife</c> takes it only while
    /// she is in stasis, and only up to the herb she is on — so one bomb is one herb, and a bomb on a
    /// free witch is shrugged off.
    /// </para>
    /// <para>
    /// <b>Every machine flies its own</b> from <c>NetMsg.RueBombThrown</c>, and only the server's copy
    /// bills — <see cref="PoisonBall"/>'s arrangement. It is lobbed over everything, so nothing in the
    /// way stops it and every copy lands at the same point. Not a network prefab.
    /// </para>
    /// </summary>
    public sealed class RueBomb : MonoBehaviour
    {
        [Tooltip("Height of the arc above the straight line between hand and target, per metre of " +
                 "throw. A short lob still rises a little — see minArcHeight.")]
        [SerializeField, Min(0f)] private float arcPerMetre = 0.3f;

        [Tooltip("Lowest the arc may be, metres.")]
        [SerializeField, Min(0f)] private float minArcHeight = 1.2f;

        [Tooltip("How close to its target it must land to count, metres.")]
        [SerializeField, Min(0.1f)] private float splashRadius = 1.6f;

        [Tooltip("Rue billed on a landing in reach. Generous: the witch caps what one stasis can " +
                 "take at one herb, so this only has to be at least a herb's worth.")]
        [SerializeField, Min(1)] private int rueDamage = 1000;

        [Tooltip("Turns per second the bottle tumbles in flight.")]
        [SerializeField] private float tumble = 1.5f;

        [Tooltip("The bottle, tumbled in flight. Optional — the root is used otherwise.")]
        [SerializeField] private Transform bottle;

        [Tooltip("Played where it shatters, on every machine. Detached and left to finish.")]
        [SerializeField] private ParticleSystem shatter;

        [Tooltip("Seconds the shatter is left to play before it is cleaned up.")]
        [SerializeField, Min(0.1f)] private float shatterSeconds = 3f;

        [Tooltip("The glass breaking, on every machine.")]
        [SerializeField] private SfxId shatterSound = SfxId.ImpactProjectile;

        private Vector3 from;
        private Vector3 to;
        private float arcHeight;
        private float flightSeconds;
        private float elapsed;
        private Transform thrower;
        private GameObject target;
        private bool cosmetic;
        private Vector3 tumbleAxis;

        /// <summary>
        /// Throw it from where it stands to <paramref name="landing"/>, taking
        /// <paramref name="seconds"/>. Only the server's copy is not <paramref name="isCosmetic"/>.
        /// </summary>
        public void Launch(Vector3 landing, float seconds, Transform owner, GameObject aimedAt, bool isCosmetic)
        {
            from = transform.position;
            to = landing;
            flightSeconds = Mathf.Max(0.05f, seconds);
            thrower = owner;
            target = aimedAt;
            cosmetic = isCosmetic;

            float distance = Vector3.Distance(from, to);
            arcHeight = Mathf.Max(minArcHeight, distance * arcPerMetre);

            // Seeded from the throw so every machine tumbles the same bottle the same way.
            Random.State saved = Random.state;
            Random.InitState(Mathf.RoundToInt(landing.x * 31f + landing.z * 17f));
            tumbleAxis = Random.onUnitSphere;
            Random.state = saved;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Clamp01(elapsed / flightSeconds);

            transform.position = Vector3.Lerp(from, to, s) + Vector3.up * (4f * arcHeight * s * (1f - s));
            Transform spun = bottle != null ? bottle : transform;
            spun.Rotate(tumbleAxis, tumble * 360f * Time.deltaTime, Space.World);

            if (s >= 1f) Land();
        }

        private void Land()
        {
            if (!cosmetic && target != null)
            {
                Vector3 offset = target.transform.position - to;
                offset.y = 0f;
                if (offset.sqrMagnitude <= splashRadius * splashRadius)
                    NetDamage.Apply(target, rueDamage, thrower, DamageKind.Rue);
            }

            Sfx.Play(shatterSound, to);

            if (shatter != null)
            {
                shatter.transform.SetParent(null, worldPositionStays: true);
                shatter.transform.SetPositionAndRotation(to, Quaternion.identity);
                shatter.Play(withChildren: true);
                Destroy(shatter.gameObject, shatterSeconds);
            }

            Destroy(gameObject);
        }
    }
}
