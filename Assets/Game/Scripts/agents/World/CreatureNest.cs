// A place a creature calls home: the Caraxoid mother's nest. Static set dressing that a NestModule
// finds by position — so the creature's home survives save/load and streaming without being saved
// anywhere: the nest is a scene object and never moves, and the module looks it up again on enable.
//
// Two radii, because a guard that attacks without warning is a rule the player cannot learn
// (GDC-L1-SYS-0006): inside warnRadius the creature gets up and watches; inside guardRadius its
// aggression climbs fast enough to become a fight if the player stays.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public class CreatureNest : MonoBehaviour
    {
        private static readonly List<CreatureNest> All = new List<CreatureNest>();

        [Tooltip("Inside this the nesting creature gets up, watches the intruder and its aggression " +
                 "holds level — a warning, not yet a threat.")]
        [SerializeField] private float warnRadius = 30f;
        [Tooltip("Inside this the creature's aggression climbs. Stay long enough and it attacks.")]
        [SerializeField] private float guardRadius = 16f;
        [Tooltip("How close to the centre the creature must be to count as home and lie down.")]
        [SerializeField] private float homeRadius = 2.5f;

        public float WarnRadius => warnRadius;
        public float GuardRadius => guardRadius;
        public float HomeRadius => homeRadius;
        public Vector3 Position => transform.position;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>The nearest nest within <paramref name="maxDistance"/>, or null.</summary>
        public static CreatureNest Nearest(Vector3 position, float maxDistance)
        {
            CreatureNest best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (CreatureNest nest in All)
            {
                float sqr = (nest.transform.position - position).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = nest;
            }
            return best;
        }

        private void OnValidate()
        {
            guardRadius = Mathf.Max(1f, guardRadius);
            warnRadius = Mathf.Max(guardRadius, warnRadius);
            homeRadius = Mathf.Max(0.5f, homeRadius);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, warnRadius);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, guardRadius);
        }
    }
}
