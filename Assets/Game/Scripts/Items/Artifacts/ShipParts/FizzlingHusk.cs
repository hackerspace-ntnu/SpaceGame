using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// A dead part popped out of its cradle: it drops to the floor, its sparks die down, and it is
    /// gone after <see cref="lifetime"/> seconds. Junk, never an item — nothing picks it up.
    ///
    /// <para>
    /// <b>Server decides, every machine draws.</b> The server animates the drop and despawns it;
    /// the motion reaches peers through the prefab's <c>NetworkTransform</c>, and the end of its life
    /// is a server-written instant every machine fades its sparks against, so a late joiner sees a
    /// husk as far through fizzling as everyone else.
    /// </para>
    /// <para>
    /// <b>Not saved, on purpose.</b> Its body is kinematic and it carries no persistent marker, so
    /// <c>SaveablePolicy</c> never opts it in: a save taken during its few seconds simply does not
    /// have it, and the cradle it came out of is saved empty. After it despawns it never returns.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FizzlingHusk : NetworkBehaviour
    {
        [Tooltip("Seconds from popping out to despawning.")]
        [SerializeField, Min(0.5f)] private float lifetime = 15f;

        [Tooltip("Seconds the fall to the floor takes.")]
        [SerializeField, Min(0.05f)] private float dropSeconds = 0.35f;

        [Tooltip("How far below the cradle it may fall, in metres.")]
        [SerializeField, Min(0.1f)] private float maxDrop = 4f;

        [Tooltip("Its sparks. Their rate fades with the life it has left.")]
        [SerializeField] private ParticleSystem sparks;

        [SerializeField] private LayerMask floorMask = ~0;

        private readonly NetworkVariable<double> networkEndsAt = new();

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private Vector3 dropFrom;
        private Vector3 dropTo;
        private Quaternion restFrom;
        private Quaternion restTo;
        private float dropClock;
        private float sparkRate = -1f;
        private double endsAt;

        public float Lifetime => lifetime;

        /// <summary>
        /// How much of its spark rate a husk still has, <paramref name="elapsed"/> seconds into a
        /// life of <paramref name="life"/>: full for the first third, dying to nothing by the end.
        /// </summary>
        public static float SparkShare(float elapsed, float life)
        {
            if (life <= 0f) return 0f;

            float left = Mathf.Clamp01(1f - elapsed / life);
            return Mathf.Clamp01(left * 1.5f);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                endsAt = Now + lifetime;
                networkEndsAt.Value = endsAt;
                BeginDrop();
                return;
            }

            endsAt = networkEndsAt.Value;
        }

        private static double Now
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening ? manager.ServerTime.Time : Time.timeAsDouble;
            }
        }

        /// <summary>SERVER: find the floor under it, and a tumble to land on.</summary>
        private void BeginDrop()
        {
            dropFrom = transform.position;
            restFrom = transform.rotation;
            dropTo = dropFrom;

            int count = Physics.RaycastNonAlloc(dropFrom + Vector3.up * 0.1f, Vector3.down, hits, maxDrop,
                                                floorMask, QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].distance <= 0f) continue;
                if (hits[i].collider.transform.IsChildOf(transform)) continue;

                // The highest floor below the drop point, not the first thing the ray met: the
                // cradle the husk just left is directly underneath it on the wall.
                if (hits[i].point.y < dropFrom.y - 0.3f && hits[i].point.y > best) best = hits[i].point.y;
            }

            if (!float.IsNegativeInfinity(best)) dropTo = new Vector3(dropFrom.x, best + 0.2f, dropFrom.z);

            restTo = Quaternion.Euler(0f, restFrom.eulerAngles.y + 35f, 0f);
            dropClock = 0f;
        }

        private void Update()
        {
            if (IsSpawned && IsServer)
            {
                if (dropClock < dropSeconds)
                {
                    dropClock += Time.deltaTime;
                    float t = Mathf.Clamp01(dropClock / dropSeconds);
                    transform.SetPositionAndRotation(Vector3.Lerp(dropFrom, dropTo, t * t),
                                                     Quaternion.Slerp(restFrom, restTo, t));
                }

                if (Now >= endsAt)
                {
                    GameServices.World.Despawn(gameObject);
                    return;
                }
            }

            FadeSparks();
        }

        private void FadeSparks()
        {
            if (sparks == null || endsAt <= 0d) return;

            ParticleSystem.EmissionModule emission = sparks.emission;
            if (sparkRate < 0f) sparkRate = emission.rateOverTimeMultiplier;

            float elapsed = lifetime - (float)(endsAt - Now);

            // The captured rate times a share: rateOverTimeMultiplier IS the rate (Flamethrower.md).
            emission.rateOverTimeMultiplier = sparkRate * SparkShare(elapsed, lifetime);
        }
    }
}
