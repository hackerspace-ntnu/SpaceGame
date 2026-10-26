using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The Oathbranch's shot: a mote of shimmer that flies straight along the holder's aim and puts
    /// the first body it touches into stasis.
    ///
    /// <para>
    /// <b>It flies; it does not hit instantly.</b> The witch it was made for blinks and keeps her
    /// distance, and a bolt with travel time is a shot the player has to lead and she has a chance
    /// to slip — the skill in the fight is landing it, which a hitscan would take away
    /// (GDC-L1-DESIGN-0002).
    /// </para>
    /// <para>
    /// <b>Every machine flies its own, and only one of them decides.</b> The staff instantiates a
    /// copy locally on every machine from the same aim ray, so the bolt is never waiting on a round
    /// trip; only the authority's copy applies the status, which then reaches every machine as the
    /// ordinary status message. So this prefab is NOT in the network prefab list — nothing about it
    /// is spawned. Every copy still makes sure the body it struck has a <see cref="StatusReceiver"/>,
    /// because a receiver only the server made is a body that is in stasis for the server and nobody
    /// else (see <see cref="StatusReceiver.EnsureOnBody"/>).
    /// </para>
    /// <para>
    /// <b>Players and the hamlet's own are passed through</b> (<see cref="StasisPass"/>). A co-op
    /// partner who walks into the line of fire must not be frozen for five seconds by a friend, and
    /// neither may a keeper running in to strike the witch.
    /// </para>
    /// </summary>
    public sealed class StasisBolt : MonoBehaviour
    {
        [Tooltip("Metres per second.")]
        [SerializeField, Min(1f)] private float speed = 35f;

        [Tooltip("How far it flies before it fizzles, metres.")]
        [SerializeField, Min(1f)] private float range = 45f;

        [Tooltip("How fat the bolt is for hitting things, metres. A little generous: it is a slow " +
                 "shot at a moving target.")]
        [SerializeField, Min(0.01f)] private float radius = 0.35f;

        [Tooltip("What it can strike. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Who it flies through: players, and the hamlet's own faction.")]
        [SerializeField] private StasisPass passes = new StasisPass();

        [Tooltip("Played where it strikes or fizzles, on every machine. Detached and left to finish.")]
        [SerializeField] private ParticleSystem burst;

        [Tooltip("Seconds the burst is left to play before it is cleaned up.")]
        [SerializeField, Min(0.1f)] private float burstSeconds = 1.5f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Transform shooter;
        private bool cosmetic;
        private float stasisSeconds;
        private float travelled;

        /// <summary>
        /// Send it on its way. <paramref name="stasisSeconds"/> of zero or less is the status's own
        /// duration, which is the usual case.
        /// </summary>
        public void Launch(GameObject owner, bool isCosmetic, float seconds)
        {
            shooter = owner != null ? owner.transform : null;
            cosmetic = isCosmetic;
            stasisSeconds = seconds;
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 along = transform.forward;

            if (TryStrike(from, along, step, out RaycastHit hit))
            {
                Strike(hit);
                return;
            }

            transform.position = from + along * step;
            travelled += step;

            if (travelled >= range) Finish(transform.position);
        }

        /// <summary>The nearest thing along this frame's step that the bolt stops on.</summary>
        private bool TryStrike(Vector3 from, Vector3 along, float step, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.SphereCastNonAlloc(from, radius, along, Hits, step, hitMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = Hits[i];
                if (PassesThrough(candidate.collider)) continue;
                if (found && candidate.distance >= nearest.distance) continue;

                nearest = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>Its own shooter, and whoever <see cref="passes"/> lets through.</summary>
        private bool PassesThrough(Collider collider)
        {
            if (shooter != null && collider.transform.IsChildOf(shooter)) return true;
            return passes.Passes(collider);
        }

        private void Strike(RaycastHit hit)
        {
            StatusReceiver body = StatusReceiver.EnsureOnBody(hit.collider.gameObject);

            // Apply is a no-op anywhere but the deciding machine; the cosmetic copies only need the
            // receiver to exist, which EnsureOnBody already saw to.
            if (body != null && !cosmetic)
                body.Apply(StatusKind.Stasis, stasisSeconds, source: shooter);

            // A sweep that started inside something reports the origin and no point; the bolt's own
            // position is the honest place for the burst then.
            Finish(hit.point == Vector3.zero ? transform.position : hit.point);
        }

        private void Finish(Vector3 at)
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
