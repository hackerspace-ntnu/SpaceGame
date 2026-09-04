// What a bolt of lightning does where it lands, in one place.
//
// Extracted from LightningSpell, which used to be the only thing that could throw one.
// The lightning conjurer throws the same bolt, and two copies of "who is inside the
// radius, and how many times do we bill them" is exactly the kind of duplication that
// drifts: one side gets a fix for hitting a creature once per limb and the other does
// not, and nobody notices until a boss dies to four hits instead of twelve.
//
// Deliberately NOT a MonoBehaviour and deliberately holding no tuning of its own. The
// caster owns its numbers -- the player's spell hits for 120 in 3.5 m because that is
// balanced against the player's cooldown, and the conjurer's is balanced against its
// own -- so damage, radius and mask are arguments rather than fields. What is shared is
// the mechanism, not the values.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public static class LightningStrike
    {
        /// <summary>
        /// Hurt everything standing where the bolt earthed.
        ///
        /// <para>
        /// SERVER ONLY. Damage is shared world state and exactly one machine may decide
        /// it; calling this on every peer kills a creature once per player watching.
        /// Both callers reach it from a path that only the simulating machine runs.
        /// </para>
        /// <para>
        /// <paramref name="ground"/> is where the bolt LANDS, not where it is drawn from.
        /// The visual is spawned high so it has sky to fall through, and billing that
        /// point instead would put the blast radius in the air above everything it was
        /// meant to hit.
        /// </para>
        /// </summary>
        /// <returns>How many distinct things were billed.</returns>
        public static int Damage(Vector3 ground, int damage, float radius, LayerMask mask,
                                 GameObject attacker, bool damagesAttacker)
        {
            if (damage <= 0 || radius <= 0f) return 0;

            Collider[] caught = Physics.OverlapSphere(ground, radius, mask,
                                                      QueryTriggerInteraction.Ignore);

            // Colliders, not creatures: a body is several of them, and billing each would
            // multiply the damage by however many limbs happened to be inside the radius.
            var billed = new HashSet<GameObject>();

            foreach (Collider collider in caught)
            {
                if (collider == null) continue;

                if (!damagesAttacker && attacker != null &&
                    collider.transform.IsChildOf(attacker.transform))
                    continue;

                HealthComponent health = collider.GetComponentInParent<HealthComponent>();

                // Not everything hurtable owns a HealthComponent -- destructible props
                // implement IDamageable directly -- so fall back to the collider itself
                // and let NetDamage work out which of the two it is looking at.
                GameObject target = health != null ? health.gameObject : collider.gameObject;

                if (!billed.Add(target)) continue;

                NetDamage.Apply(target, damage,
                                attacker != null ? attacker.transform : null);
            }

            return billed.Count;
        }

        /// <summary>
        /// Where a bolt fired ALONG a line actually stops.
        ///
        /// <para>
        /// Pure geometry, no damage: safe on any machine, and every machine needs it,
        /// because the bolt has to be DRAWN to the same place it is billed at. The
        /// conjurer's chest cast fires from between its hands rather than dropping out of
        /// the sky, so a wall between it and its target should stop the bolt at the wall
        /// instead of letting it pass through and hurt whatever is behind.
        /// </para>
        /// <para>
        /// A sweep rather than a ray because the visible bolt is a ribbon with width, and
        /// a zero-thickness ray slips through gaps the picture clearly does not.
        /// </para>
        /// </summary>
        /// <returns>The first thing hit, or <paramref name="to"/> if the line is clear.</returns>
        public static Vector3 BeamImpact(Vector3 from, Vector3 to, float radius,
                                         LayerMask mask, GameObject attacker)
        {
            Vector3 span = to - from;
            float distance = span.magnitude;
            if (distance < 1e-4f) return to;

            Vector3 direction = span / distance;
            RaycastHit[] hits = Physics.SphereCastAll(
                from, Mathf.Max(0.01f, radius), direction, distance, mask,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Vector3 impact = to;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;

                // The muzzle sits a metre in front of the caster's own chest, so its body
                // is the FIRST thing any sweep from there finds. Without this the conjurer
                // shoots itself in the ring every time.
                if (attacker != null && hit.collider.transform.IsChildOf(attacker.transform))
                    continue;

                if (hit.distance >= nearest) continue;
                nearest = hit.distance;

                // A sweep that starts already overlapping a collider reports distance 0
                // and a meaningless point at the world origin. Use the muzzle itself in
                // that case rather than drawing the bolt to (0,0,0).
                impact = hit.distance > 0f ? hit.point : from + direction * radius;
            }

            return impact;
        }

        /// <summary>
        /// Fire a bolt along a line and hurt what it lands on.
        ///
        /// <para>
        /// SERVER ONLY, for the same reason <see cref="Damage"/> is: it bills damage.
        /// </para>
        /// <para>
        /// The splash is deliberately small compared to the sky strike's blast. A bolt
        /// aimed down a line that you can break by stepping behind something has already
        /// given the player their counterplay; giving it the falling bolt's radius on top
        /// would make it strictly better than the attack it replaced.
        /// </para>
        /// </summary>
        /// <param name="impact">Where it stopped -- draw the bolt to exactly this point.</param>
        /// <returns>How many distinct things were billed.</returns>
        public static int Beam(Vector3 from, Vector3 to, float radius, int damage,
                               float splashRadius, LayerMask mask, GameObject attacker,
                               out Vector3 impact)
        {
            impact = BeamImpact(from, to, radius, mask, attacker);
            return Damage(impact, damage, splashRadius, mask, attacker,
                          damagesAttacker: false);
        }

        /// <summary>
        /// Draw the bolt. Pure presentation -- safe to run on any machine, and it must
        /// run on every machine that should see it.
        /// </summary>
        /// <param name="drawFrom">
        /// Where the bolt starts, which is <c>ground + up * drawHeight</c>. The prefab's
        /// own graph falls from there.
        /// </param>
        /// <param name="lifetime">
        /// Seconds before the spawned object is destroyed, or 0 to leave it alive forever.
        ///
        /// <para>
        /// Not optional in spirit. Lightning.prefab is a bare VisualEffect with nothing on
        /// it that ever cleans it up, so every call leaks one GameObject. A player casting
        /// occasionally never notices; a creature throwing one every five seconds for the
        /// life of the session accumulates them until the scene is full of finished bolts.
        /// </para>
        /// <para>
        /// Defaults to 0 so existing callers keep the behaviour they were written against.
        /// Object.Destroy's delayed overload is used rather than a coroutine because this
        /// is a static helper with no MonoBehaviour to run one on.
        /// </para>
        /// </param>
        public static GameObject Present(GameObject vfxPrefab, Vector3 drawFrom,
                                         Vector3 landsAt, float lifetime = 0f)
        {
            if (vfxPrefab == null) return null;

            // The graph is authored pointing down its own +Y; the 90 deg pitch stands it
            // upright in world space. Same constant the spell has always used.
            GameObject spawned =
                Object.Instantiate(vfxPrefab, drawFrom, Quaternion.Euler(90f, 0f, 0f));

            // A geometry bolt is told BOTH ends, and that is the difference that matters:
            // it then spans exactly the gap it was given at any draw height. A VFX Graph
            // whose length is baked in can only be dropped at a point and hoped over --
            // which is why raising the conjurer to 100 m left a bolt in the sky.
            //
            // GetComponentInChildren rather than GetComponent so the prefab is free to put
            // the renderer under a child with the impact flash and light beside it.
            var bolt = spawned.GetComponentInChildren<LightningBoltEffect>();
            if (bolt != null) bolt.Strike(drawFrom, landsAt);

            if (lifetime > 0f) Object.Destroy(spawned, lifetime);

            return spawned;
        }
    }
}
