// Everything inside a blast, each thing billed exactly once.
//
// This was written out by hand wherever a blast was needed — LightningSpell, TurretProjectile's
// splash, DragonRocket, SuckerPuncher — and the copies did not agree. The rule that is easy to get
// wrong, and that at least one of them got wrong, is that a body is SEVERAL colliders: a rig's
// capsules hang off its bones, so billing per collider multiplies the damage by however many limbs
// happened to be inside the radius. Deduplicating by the object that actually OWNS the health is
// the whole job, and it belongs next to NetDamage rather than in each caller.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public static class RadiusDamage
    {
        /// <summary>
        /// Ceiling on colliders considered in one sweep. Generous for a blast; a sweep that fills
        /// it is a radius or a mask that wants tuning, not a bigger buffer.
        /// </summary>
        private const int MaxOverlap = 64;

        // Safe as shared state because Collect never calls back into anything that could re-enter
        // it — a physics query and GetComponentInParent, no user code. Apply, which DOES run user
        // code by way of NetDamage, keeps its own list for exactly that reason.
        private static readonly Collider[] Overlap = new Collider[MaxOverlap];
        private static readonly HashSet<GameObject> Seen = new HashSet<GameObject>();

        /// <summary>
        /// Fill <paramref name="targets"/> with every damageable thing inside the sphere, once each.
        ///
        /// <para>
        /// Takes the list rather than returning one so a caller sweeping every frame — a drifting
        /// projectile looking for something to earth through — can reuse one buffer and allocate
        /// nothing. Triggers are always ignored.
        /// </para>
        /// <para>
        /// <paramref name="exclude"/> is a root to skip, normally whoever fired: pass the owner and
        /// the blast cannot bill its own author.
        /// </para>
        /// </summary>
        public static int Collect(Vector3 center, float radius, LayerMask mask, Transform exclude,
                                  List<GameObject> targets)
        {
            if (targets == null) return 0;

            targets.Clear();
            if (radius <= 0f) return 0;

            int found = Physics.OverlapSphereNonAlloc(center, radius, Overlap, mask,
                                                      QueryTriggerInteraction.Ignore);

            // The query stops filling once it is out of room, so a saturated sweep silently drops
            // bodies. That is a content problem worth hearing about rather than a runtime one.
            if (found >= MaxOverlap)
            {
                Debug.LogWarning(
                    $"[RadiusDamage] {MaxOverlap} colliders inside a {radius:0.#} m sweep — some were ignored. " +
                    "Narrow the radius or the mask.");
            }

            Seen.Clear();

            for (int i = 0; i < found; i++)
            {
                Collider collider = Overlap[i];
                if (collider == null) continue;

                if (exclude != null && collider.transform.IsChildOf(exclude)) continue;

                GameObject target = Owner(collider);
                if (target == null) continue;

                if (!Seen.Add(target)) continue;

                targets.Add(target);
            }

            return targets.Count;
        }

        /// <summary>
        /// Hurt everything inside the sphere for <paramref name="amount"/>, once each.
        /// Returns how many things were billed.
        /// </summary>
        public static int Apply(Vector3 center, float radius, LayerMask mask, int amount,
                                Transform source, Transform exclude = null)
        {
            if (amount <= 0) return 0;

            // A local list, not the shared one: NetDamage runs death, loot and whatever a listener
            // does with them, and any of that is entitled to set off another blast.
            List<GameObject> targets = new List<GameObject>();
            Collect(center, radius, mask, exclude, targets);

            for (int i = 0; i < targets.Count; i++)
            {
                NetDamage.Apply(targets[i], amount, source);
            }

            return targets.Count;
        }

        /// <summary>
        /// The object that owns this collider's health, or null if it has none.
        ///
        /// <para>
        /// GetComponentInParent both times, because colliders on this project's rigs hang off bones
        /// well below the object carrying the HealthComponent. Not everything hurtable owns one —
        /// destructible props implement <see cref="IDamageable"/> directly — and resolving that to
        /// the component's own object rather than the collider's is what stops a three-collider
        /// crate taking three hits.
        /// </para>
        /// </summary>
        private static GameObject Owner(Collider collider)
        {
            HealthComponent health = collider.GetComponentInParent<HealthComponent>();
            if (health != null) return health.gameObject;

            if (collider.GetComponentInParent<IDamageable>() is Component damageable)
                return damageable.gameObject;

            return null;
        }
    }
}
