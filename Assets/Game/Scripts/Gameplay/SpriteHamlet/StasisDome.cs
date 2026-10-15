using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// The shimmering dome the Oathbranch holds over the sprite hamlet: any creature that touches
    /// its skin is held in stasis where it stands.
    ///
    /// <para>
    /// <b>Who passes.</b> Players, and members of <see cref="passes"/> — the hamlet's own. Everything
    /// else that is alive and touches the skin is frozen, which is the whole of the hamlet's defence
    /// while Merob sleeps: a creature caught at the edge stands there as a statue until its stasis
    /// runs out, and is caught again if it is still touching. Creatures standing frozen against the
    /// shimmer as the crew walk up are the dome explaining itself before anybody says a word
    /// (GDC-L1-NARR-0003).
    /// </para>
    /// <para>
    /// <b>Up only while the staff is in its socket.</b> The dome does not decide that itself: a
    /// <c>QuestStateGate</c> on the hamlet switches this object off from the step where the Elder
    /// hands the Oathbranch over, so a reload and a late joiner find the dome up or down with
    /// nothing saved here.
    /// </para>
    /// <para>
    /// <b>Scans on every machine, decides on one.</b> Every machine finds the same bodies touching
    /// the same skin and makes sure each has a <see cref="StatusReceiver"/> — a receiver only the
    /// server made is a body frozen for the server alone. <see cref="StatusReceiver.Apply"/> is a
    /// no-op anywhere but the deciding machine, so the freeze itself happens exactly once.
    /// </para>
    /// </summary>
    public class StasisDome : MonoBehaviour
    {
        [Tooltip("Radius of the dome, metres, from this object's position.")]
        [SerializeField, Min(1f)] private float radius = 22f;

        [Tooltip("How far either side of the skin counts as touching it, metres.")]
        [SerializeField, Min(0.1f)] private float skinThickness = 0.75f;

        [Tooltip("Seconds between looks for bodies on the skin. Not per frame: the dome is large.")]
        [SerializeField, Min(0.05f)] private float scanInterval = 0.25f;

        [Tooltip("Seconds of stasis per touch. Zero or less: the status's own duration.")]
        [SerializeField] private float stasisSeconds;

        [Tooltip("Who walks through the skin: players, and the hamlet's own faction.")]
        [SerializeField] private StasisPass passes = new StasisPass();

        [Tooltip("What the scan looks at. Triggers are always ignored.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        private readonly Collider[] touching = new Collider[64];
        private readonly HashSet<StatusReceiver> frozenThisScan = new();
        private float scanTimer;

        public float Radius => radius;

        private void OnEnable() => scanTimer = 0f;

        private void Update()
        {
            scanTimer -= Time.deltaTime;
            if (scanTimer > 0f) return;

            scanTimer = scanInterval;
            Scan();
        }

        private void Scan()
        {
            Vector3 centre = transform.position;
            int count = Physics.OverlapSphereNonAlloc(centre, radius + skinThickness, touching, bodyMask,
                                                      QueryTriggerInteraction.Ignore);
            if (count == touching.Length)
                Debug.LogWarning($"[StasisDome] '{name}' found {count} colliders on one scan, the buffer's size — some bodies at the skin may be missed.", this);

            frozenThisScan.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider part = touching[i];
                if (!OnSkin(part, centre)) continue;

                HealthComponent health = part.GetComponentInParent<HealthComponent>();
                if (health == null || !health.Alive || passes.Passes(health)) continue;

                StatusReceiver body = StatusReceiver.EnsureOnBody(health.gameObject);
                if (body == null || !frozenThisScan.Add(body)) continue;

                body.Apply(StatusKind.Stasis, stasisSeconds, source: transform);
            }
        }

        /// <summary>
        /// Does <paramref name="part"/> straddle the skin? Something wholly inside — a villager going
        /// about its day — is not touching it, and neither is something wholly outside.
        /// </summary>
        private bool OnSkin(Collider part, Vector3 centre)
        {
            Bounds bounds = part.bounds;
            float nearest = Vector3.Distance(centre, bounds.ClosestPoint(centre));
            float farthest = Vector3.Distance(centre, bounds.center) + bounds.extents.magnitude;

            return nearest <= radius + skinThickness && farthest >= radius - skinThickness;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
