// Makes a creature peaceful until something hurts it, then hostile toward whoever did.
//
// The interesting part of this component is how little it does, and that is a property of the
// architecture rather than of this file. Two facts make "peaceful" almost free:
//
//   * Every combat module — ChaseModule, CloseCombatModule, AgentRangedCombatModule — acts only
//     when AgentTargeting is holding a target. None of them consults the faction table itself.
//     So an agent that never acquires a target is peaceful with no module disabled, no behaviour
//     tree branch and no state flag threaded through the stack. It simply wanders.
//   * FactionRelationshipTable.Get returns Neutral for any pair it has no row for, and
//     AgentTargeting only ever queries for candidates it is Hostile toward. So a faction with no
//     rows in the table — Fauna — can never acquire anyone.
//
// Which leaves exactly one job for this component: when the creature is hurt, hand AgentTargeting
// the attacker directly, and keep handing it over for as long as the grudge lasts.
//
// The grudge rule is a leash, not a timer that starts on the hit:
//     inside leashRange  → angry, indefinitely. Stand and fight.
//     outside leashRange → the calm-down clock runs.
//     re-enter          → clock resets to zero.
// So you cannot wait out a creature standing on top of you, and you are not chased across the
// world for one stray shot. Walk away and it forgets.
//
// Requires nothing to be wired: it finds AgentTargeting and HealthComponent on the same object.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    // After AgentTargeting (-50), before AgentController (0). AgentTargeting has already run its
    // acquisition and staleness pass by the time this re-asserts the grudge target, so the
    // behaviour modules that tick later in the frame see one coherent answer rather than a target
    // that exists on even frames.
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(HealthComponent))]
    public class ProvocationModule : MonoBehaviour
    {
        [Header("Grudge")]
        [Tooltip("While the attacker is within this distance the creature stays hostile " +
                 "indefinitely. Past it, the calm-down clock starts. Coming back inside resets " +
                 "the clock, so a fight cannot be waited out.\n\n" +
                 "Keep this at or below AgentTargeting's loseRange: a grudge held further out " +
                 "than targeting will retain is re-asserted and dropped on alternate frames.")]
        [SerializeField] private float leashRange = 45f;

        [Tooltip("Seconds outside leashRange before the creature forgets and goes back to being " +
                 "peaceful.")]
        [SerializeField] private float calmDownDelay = 60f;

        [Tooltip("Ignore damage below this. Set above 0 for creatures that should shrug off " +
                 "chip damage — a fall, a scrape — rather than turning on the world for 1 HP.")]
        [SerializeField] private int damageThreshold = 0;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // ── Published state ───────────────────────────────────────────────────────
        public bool IsProvoked => aggressor != null;
        public Transform Aggressor => aggressor;

        // Seconds the aggressor has been outside the leash. Zero whenever they are inside it.
        public float CalmingFor { get; private set; }

        private AgentTargeting targeting;
        private HealthComponent health;
        private Transform aggressor;

        // Set by RestoreGrudge, consumed by the next OnEnable. Without it a restored grudge is wiped
        // by the Forget() below whenever the restore lands while the object is disabled — which is
        // the ordinary case for an entity whose chunk is hydrated before it is switched on.
        private bool restoredGrudge;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();

            // GetOrAdd rather than GetComponent: without an AgentTargeting there is nothing to
            // hand the attacker to, and the creature would take hits forever without reacting —
            // a silent failure that looks exactly like the feature not being implemented.
            targeting = AgentTargeting.GetOrAdd(gameObject);
        }

        private void OnEnable()
        {
            // A creature that comes back — respawned or streamed back in — comes back calm, because
            // nothing in that path says otherwise.
            //
            // A creature restored from a SAVE is the exception, and it is the reason for the latch.
            // Shooting a Golem and reloading used to hand back a peaceful Golem forever: Fauna is
            // Neutral toward everything, so AgentTargeting.Reevaluate can never re-acquire the player
            // on its own, and this component is the only thing that would have re-asserted the target.
            // The grudge is now persisted (ProvocationSaveable), and this must not throw it away.
            if (restoredGrudge)
                restoredGrudge = false;
            else
                Forget();

            if (health != null)
                health.OnDamage += HandleDamage;
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDamage -= HandleDamage;
        }

        private void HandleDamage(int amount)
        {
            if (amount < damageThreshold || health == null)
                return;

            // A save being replayed, not a punch. HealthComponent re-raises OnDamage while
            // restoring, and waking every creature that was ever wounded is not the state the
            // world was saved in.
            if (health.IsRestoring)
                return;

            Transform source = health.LastDamageSource;
            if (source == null)
                return;

            // Attribute to the entity, not to the collider or the projectile that carried the
            // reference — a limb collider is not something the creature can walk toward, and a
            // projectile is destroyed the frame it lands.
            EntityFaction attacker = source.GetComponentInParent<EntityFaction>();
            Transform resolved = attacker != null ? attacker.transform : source;

            if (resolved == transform)
                return;                      // self-inflicted; nothing to be angry at

            Provoke(resolved);
        }

        /// <summary>
        /// Turn this creature on <paramref name="target"/>. Public so a herd, an ally alert or a
        /// scripted event can share the anger without having to land a hit first.
        /// </summary>
        public void Provoke(Transform target)
        {
            if (!TargetResolution.IsViable(target))
                return;

            // Normally filled by Awake. Resolved here too because a restore can reach this before
            // Awake has run — the save system talks to components on objects it has just hydrated.
            if (targeting == null)
                targeting = AgentTargeting.GetOrAdd(gameObject);

            aggressor = target;
            CalmingFor = 0f;
            targeting.ForceTarget(target);
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Goes through <see cref="Provoke"/> rather than assigning the field, so the restored grudge
        /// arrives the same way a fresh one does: the target is handed to AgentTargeting immediately,
        /// and this component's <c>[DefaultExecutionOrder(-40)]</c> re-assertion in
        /// <see cref="Update"/> then holds it against a Reevaluate that could never find it. Setting
        /// <c>aggressor</c> directly would leave the creature angry with no target until the leash
        /// check happened to notice.
        ///
        /// <paramref name="calmingFor"/> is re-applied after the fact because <see cref="Provoke"/>
        /// zeroes it — a creature saved 50 seconds into a 60 second calm-down must not come back with
        /// a full clock.
        /// </summary>
        public void RestoreGrudge(Transform target, float calmingFor)
        {
            Provoke(target);

            // Not viable — dead, despawned, or retired from the registry. Nothing was restored, so
            // the latch stays down and OnEnable is free to reset as usual.
            if (aggressor == null)
                return;

            CalmingFor = Mathf.Max(0f, calmingFor);
            restoredGrudge = true;
        }

        /// <summary>Drop the grudge and go back to being peaceful.</summary>
        public void Forget()
        {
            if (aggressor != null && targeting != null)
                targeting.ClearTarget();

            aggressor = null;
            CalmingFor = 0f;
        }

        private void Update()
        {
            if (aggressor == null)
                return;

            // Killed, despawned, or retired from the registry. Nothing to chase and nothing to
            // come back into range, so run the clock out rather than holding a dead grudge —
            // it keeps the creature alert for a moment after a kill instead of instantly idling.
            if (!TargetResolution.IsViable(aggressor))
            {
                if (Tick())
                    Forget();
                return;
            }

            if (Vector3.Distance(transform.position, aggressor.position) > leashRange)
            {
                if (Tick())
                    Forget();
                return;
            }

            CalmingFor = 0f;

            // Re-assert every frame the attacker is in reach. This is what holds the target:
            // AgentTargeting's own Reevaluate can never find this candidate (a Fauna creature is
            // Neutral toward everything, and NPCs are Neutral toward the player), and its
            // line-of-sight staleness pass will happily drop a target that ducks behind a rock.
            // Cheap because it is a reference compare in the common case.
            if (targeting.Target != aggressor)
                targeting.ForceTarget(aggressor);
        }

        // Advances the calm-down clock. True once the grudge has expired.
        private bool Tick()
        {
            CalmingFor += Time.deltaTime;
            return CalmingFor >= calmDownDelay;
        }

        private void OnValidate()
        {
            leashRange = Mathf.Max(1f, leashRange);
            calmDownDelay = Mathf.Max(0f, calmDownDelay);
            damageThreshold = Mathf.Max(0, damageThreshold);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Gizmos.color = IsProvoked ? new Color(1f, 0.35f, 0.1f) : new Color(0.3f, 0.8f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, leashRange);

            if (Application.isPlaying && IsProvoked)
                Gizmos.DrawLine(transform.position + Vector3.up, aggressor.position + Vector3.up);
        }
    }
}
