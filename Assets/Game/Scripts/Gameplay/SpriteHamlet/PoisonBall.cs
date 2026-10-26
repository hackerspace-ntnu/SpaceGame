using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// One small ball of the witch's poison, drifting out along the way she faced when she threw it.
    /// A spin throws a stream of them, and the stream is a spiral the player has to walk through the
    /// gaps of.
    ///
    /// <para>
    /// <b>Slow on purpose.</b> The spiral is a dodging puzzle, not a bullet: every ball is visible for
    /// seconds before it arrives, and the gaps between them are the counterplay (GDC-L1-DESIGN-0002).
    /// It hugs the ground at keeper height, because the keepers are what it is really hunting.
    /// </para>
    /// <para>
    /// <b>Every machine flies its own, and only one of them decides</b> — <see cref="Items.StasisBolt"/>'s
    /// arrangement. The spin is broadcast once (<c>NetMsg.WitchSpiral</c>) and each machine throws the
    /// same balls on the same clock; only the server's copies bill anything. A player it reaches is
    /// hurt and shoved — the shove travels as <c>NetMsg.Flung</c> to the machine that owns the body. A
    /// keeper is hurt with <see cref="DamageKind.Poison"/>, which its <c>PoisonTally</c> counts. A
    /// keeper being carried has its colliders off, so the ball meets the carrier instead.
    /// </para>
    /// <para>Not a network prefab: nothing about it is spawned.</para>
    /// </summary>
    public sealed class PoisonBall : MonoBehaviour
    {
        [Tooltip("Metres per second.")]
        [SerializeField, Min(0.1f)] private float speed = 4f;

        [Tooltip("How far it drifts before it bursts on its own, metres.")]
        [SerializeField, Min(1f)] private float range = 22f;

        [Tooltip("How fat it is for hitting things, metres.")]
        [SerializeField, Min(0.01f)] private float radius = 0.3f;

        [Tooltip("Height it floats above the ground, metres. Keeper height, so it finds them.")]
        [SerializeField, Min(0.1f)] private float floatHeight = 0.6f;

        [Tooltip("How fast it settles back to its float height over bumps, per second.")]
        [SerializeField, Min(0f)] private float heightFollow = 6f;

        [Tooltip("Damage to whatever it reaches.")]
        [SerializeField, Min(1)] private int damage = 12;

        [Tooltip("How hard a player it reaches is shoved, m/s along its path.")]
        [SerializeField, Min(0f)] private float knockSpeed = 9f;

        [Tooltip("Upward part of the shove, m/s, so the player leaves the ground rather than skids.")]
        [SerializeField, Min(0f)] private float knockLift = 4f;

        [Tooltip("What it can strike and what counts as ground. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Played where it bursts, on every machine. Detached and left to finish.")]
        [SerializeField] private ParticleSystem burst;

        [Tooltip("Seconds the burst is left to play before it is cleaned up.")]
        [SerializeField, Min(0.1f)] private float burstSeconds = 2f;

        [Tooltip("Heard when it splashes over a body, on every machine. Not when it fizzles on its " +
                 "own — a spin throws dozens, and the clearing would never be quiet.")]
        [SerializeField] private SfxId splashSound = SfxId.ImpactFlesh;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Transform thrower;
        private bool cosmetic;
        private float travelled;

        /// <summary>
        /// Send it on its way along this transform's forward. Only the server's copy is not
        /// <paramref name="isCosmetic"/>.
        /// </summary>
        public void Launch(Transform owner, bool isCosmetic)
        {
            thrower = owner;
            cosmetic = isCosmetic;
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 along = transform.forward;

            if (TryStrike(from, along, step, out RaycastHit hit))
            {
                Strike(hit, along);
                return;
            }

            Vector3 next = from + along * step;
            if (Physics.Raycast(next + Vector3.up * floatHeight, Vector3.down, out RaycastHit ground,
                                floatHeight * 4f, hitMask, QueryTriggerInteraction.Ignore))
                next.y = Mathf.Lerp(next.y, ground.point.y + floatHeight, 1f - Mathf.Exp(-heightFollow * Time.deltaTime));

            transform.position = next;
            travelled += step;

            if (travelled >= range) Burst(transform.position);
        }

        /// <summary>The nearest thing along this frame's step, skipping its thrower.</summary>
        private bool TryStrike(Vector3 from, Vector3 along, float step, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.SphereCastNonAlloc(from, radius, along, Hits, step, hitMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = Hits[i];
                if (thrower != null && candidate.collider.transform.IsChildOf(thrower)) continue;
                if (found && candidate.distance >= nearest.distance) continue;

                nearest = candidate;
                found = true;
            }

            return found;
        }

        private void Strike(RaycastHit hit, Vector3 along)
        {
            if (hit.collider.GetComponentInParent<HealthComponent>() != null)
                Sfx.Play(splashSound, transform.position);

            if (!cosmetic)
            {
                NetDamage.Apply(hit.collider.gameObject, damage, thrower, DamageKind.Poison);

                PlayerMovement player = hit.collider.GetComponentInParent<PlayerMovement>();
                if (player != null)
                {
                    Vector3 shove = Vector3.ProjectOnPlane(along, Vector3.up).normalized * knockSpeed + Vector3.up * knockLift;
                    NetMessaging.NetSendTo(player.gameObject, NetMsg.Flung, new NetArg { P = shove }, NetTo.All);
                }
            }

            // A sweep that started already touching something — a keeper walking into a ball —
            // reports distance 0 and a point at the world origin; the ball itself is the honest
            // place for the burst then.
            Burst(hit.distance <= 0f ? transform.position : hit.point);
        }

        private void Burst(Vector3 at)
        {
            if (burst != null)
            {
                burst.transform.SetParent(null, worldPositionStays: true);
                burst.transform.position = at;
                burst.Play(withChildren: true);
                Destroy(burst.gameObject, burstSeconds);
            }

            Destroy(gameObject);
        }
    }
}
