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
                 "chip damage — a fall, a scrape — rather than turning on the world for 1 HP.\n\n" +
                 "A floor under hitGain, not a replacement for it: damage that clears this still " +
                 "only moves the meter by what it is worth.")]
        [SerializeField] private int damageThreshold = 0;

        [Header("Aggression")]
        [Tooltip("What this agent's temperament is worth in points. The per-tribe flavour lives " +
                 "entirely in these numbers — a nomad who forgives a gunshot, an outlaw who " +
                 "notices an aimed gun from further off.")]
        [SerializeField] private AggressionSettings aggressionSettings = AggressionSettings.Default;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // ── Published state ───────────────────────────────────────────────────────
        public bool IsProvoked => aggressor != null;
        public Transform Aggressor => aggressor;

        // Seconds the aggressor has been outside the leash. Zero whenever they are inside it.
        public float CalmingFor { get; private set; }

        /// <summary>
        /// How close this agent is to fighting, in [0, 100]. Read by the telegraph; the player
        /// never sees the number, only the band it falls in.
        /// </summary>
        public float Aggression => aggression;

        /// <summary>Which band <see cref="Aggression"/> falls in for THIS agent's temperament.</summary>
        public AggressionBand Band => AggressionMath.BandFor(aggression, aggressionSettings.attackAt);

        public AggressionSettings Settings => aggressionSettings;

        /// <summary>
        /// Who the meter is filling up because of, before it is full enough to be a grudge.
        ///
        /// Separate from <see cref="Aggressor"/> on purpose: an agent that is merely wary has not
        /// picked a fight with anybody, and handing this to AgentTargeting would be exactly the
        /// binary behaviour the meter replaces. It is who the telegraph looks at, and who
        /// <see cref="Provoke"/> is called with if the meter fills.
        /// </summary>
        public Transform Provoker => provoker;

        /// <summary>
        /// Raised when the band changes, with the band just left and the one just entered. The
        /// telegraph listens; nothing else should need to.
        /// </summary>
        public event System.Action<AggressionBand, AggressionBand> BandChanged;

        /// <summary>
        /// Who this agent last heard fire a shot, and when (<c>Time.time</c>). Answers
        /// "somebody is shooting around here and it was them".
        ///
        /// <para>
        /// Recorded here rather than on the shooter because it is a fact about what THIS agent
        /// witnessed: the gunshot arrives through <c>NoiseReceiverModule</c>, which already has a
        /// radius and already refuses to count an ally, so an agent over the ridge never heard it
        /// and has no business acting on it. A flag on the player would be heard by the whole map.
        /// </para>
        /// </summary>
        public Transform LastGunshotFrom { get; private set; }

        /// <inheritdoc cref="LastGunshotFrom"/>
        public float LastGunshotTime { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// Did this agent hear <paramref name="who"/> fire within the last <paramref name="within"/>
        /// seconds? Matched at the root, because a shot is attributed through whatever child
        /// carried it.
        /// </summary>
        public bool HeardGunshotFrom(Transform who, float within)
        {
            if (who == null || LastGunshotFrom == null)
                return false;

            return LastGunshotFrom.root == who.root
                   && Time.time - LastGunshotTime <= within;
        }

        private float aggression;
        private Transform provoker;
        private AggressionBand band;

        private AgentTargeting targeting;
        private HealthComponent health;
        private Transform aggressor;

        // Set by RestoreGrudge, consumed by the next OnEnable. Without it a restored grudge is wiped
        // by the Forget() below whenever the restore lands while the object is disabled — which is
        // the ordinary case for an entity whose chunk is hydrated before it is switched on.
        private bool restoredGrudge;

        // Same latch, for a restore that carried only a meter reading and no grudge.
        private bool restoredAggression;

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
            // The same latch covers a restored METER, for the same reason and a milder symptom:
            // a nomad you had made wary before quitting would otherwise come back calm, because
            // the restore routinely lands while the object is still disabled.
            if (restoredGrudge || restoredAggression)
            {
                restoredGrudge = false;
                restoredAggression = false;
            }
            else
            {
                Forget();
            }

            if (health != null)
            {
                health.OnDamage += HandleDamage;
                health.OnDefended += HandleDefended;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamage -= HandleDamage;
                health.OnDefended -= HandleDefended;
            }
        }

        private void HandleDamage(int amount)
        {
            if (health == null)
                return;

            // A save being replayed, not a punch. HealthComponent re-raises OnDamage while
            // restoring, and waking every creature that was ever wounded is not the state the
            // world was saved in.
            if (health.IsRestoring)
                return;

            HandleAttack(health.LastDamageSource, amount);
        }

        /// <summary>
        /// A blow its guard stopped whole still happened. OnDamage never fires for it, so without
        /// this a player could block-bait a neutral camp forever without anyone minding — and a
        /// blow that got partly through is already counted by <see cref="HandleDamage"/>, at the
        /// amount that landed.
        /// </summary>
        private void HandleDefended(DamageHit hit)
        {
            if (health == null || health.IsRestoring || hit.Amount > 0)
                return;

            HandleAttack(hit.Source, hit.Attempted);
        }

        private void HandleAttack(Transform source, int amount)
        {
            if (source == null)
                return;

            // Attribute to the entity, not to the collider or the projectile that carried the
            // reference — a limb collider is not something the creature can walk toward, and a
            // projectile is destroyed the frame it lands.
            EntityFaction attacker = source.GetComponentInParent<EntityFaction>();
            Transform resolved = attacker != null ? attacker.transform : source;

            if (resolved == transform)
                return;                      // self-inflicted; nothing to be angry at

            int maxHealth = Mathf.Max(1, health.GetMaxHealth);
            float fraction = amount / (float)maxHealth;

            // The tribe's ledger, before the personal threshold below. Deliberately: `damageThreshold`
            // is about whether THIS agent shrugs the hit off, and a tribe notices you shooting its
            // people whether or not the individual you shot was bothered. The ledger has its own
            // floor (GoodwillMath's perHitMin), so a scratch still costs reputation — which is what
            // stops a player whittling a camp down for free.
            //
            // Null-conditional: there is no ledger offline in a test scene, in the arena, or before
            // the persistent scene has loaded, and none of those are errors.
            if (TryGetComponent(out EntityFaction mine))
            {
                FactionGoodwillLedger.Instance?.Report(mine, attacker, GoodwillEvent.Hit, fraction);
            }

            if (amount < damageThreshold)
                return;

            // Being hit is now one input among several rather than the only one, but it is by far
            // the heaviest: hitGain is set so a solid hit fills the meter on its own, which keeps
            // the behaviour this component had before it had a meter at all.
            AddAggression(AggressionInput.Hit, fraction, resolved);
        }

        /// <summary>
        /// Something happened that this agent might take badly. <paramref name="magnitude"/> means
        /// whatever <paramref name="input"/> says it means — a fraction of max health for a hit,
        /// seconds for menace and trespass, a count for gunshots and hurt allies.
        ///
        /// <para>
        /// Reaching <c>attackAt</c> calls <see cref="Provoke"/> and everything from there is
        /// unchanged: the target goes to AgentTargeting, the leash holds it, the calm-down clock
        /// runs, the alert goes out. The meter is only the road up to that.
        /// </para>
        /// <para>
        /// <paramref name="from"/> is remembered as the <see cref="Provoker"/> even well below the
        /// top, because the telegraph has to look at somebody — but it is deliberately NOT handed
        /// to AgentTargeting until the meter is full. A wary nomad has not picked a fight.
        /// </para>
        /// </summary>
        public void AddAggression(AggressionInput input, float magnitude, Transform from)
        {
            // Already fighting. The grudge rules own the agent from here — adding to a full meter
            // would do nothing, and re-running the crossing below would re-announce the alert.
            // Nothing below matters to an agent that is already shooting back, the gunshot record
            // included: MenaceSensor stands down the moment the meter is full.
            if (IsProvoked)
                return;

            // Recorded before the early-out below, and whatever the gain works out to: the fact
            // that somebody was shooting near here is worth knowing even from an agent with
            // gunshotGain turned down to nothing, because MenaceSensor reads it.
            if (input == AggressionInput.Gunshot && from != null)
            {
                LastGunshotFrom = from;
                LastGunshotTime = Time.time;
            }

            float delta = AggressionMath.Gain(input, magnitude, aggressionSettings);
            if (delta <= 0f)
                return;

            if (from != null && from != transform)
                provoker = from;

            aggression = AggressionMath.Apply(aggression, delta);
            RaiseBandChanged();

            if (Band != AggressionBand.Grudge)
                return;

            // The meter filled. Who it filled because of is the best answer available; an agent
            // pushed over the edge by a noise with nobody to blame stays at the top of the meter
            // and waits, rather than attacking whoever it happens to see next.
            if (TargetResolution.IsViable(provoker))
                Provoke(provoker);
        }

        /// <summary>
        /// Fire <see cref="BandChanged"/> if the band moved. Held in a field rather than recomputed
        /// by every listener so the edge is raised exactly once — a bark on a level rather than on
        /// an edge is a bark every frame.
        /// </summary>
        private void RaiseBandChanged()
        {
            AggressionBand now = Band;
            if (now == band)
                return;

            AggressionBand previous = band;
            band = now;
            BandChanged?.Invoke(previous, now);
        }

        /// <summary>
        /// Turn this creature on <paramref name="target"/>. Public so a herd, an ally alert or a
        /// scripted event can share the anger without having to land a hit first.
        ///
        /// <para>
        /// <paramref name="announce"/> tells the <see cref="AlertBroadcaster"/> on this object, if
        /// there is one, to pass a NEW aggressor on to allies in range. Leave it on for anger this
        /// creature earned itself (a hit); turn it off when the anger arrived from someone else's
        /// alert or a restore, or every receiver becomes a broadcaster and one hit cascades across
        /// the map.
        /// </para>
        /// </summary>
        public void Provoke(Transform target, bool announce = true)
        {
            if (!TargetResolution.IsViable(target))
                return;

            // Normally filled by Awake. Resolved here too because a restore can reach this before
            // Awake has run — the save system talks to components on objects it has just hydrated.
            if (targeting == null)
                targeting = AgentTargeting.GetOrAdd(gameObject);

            bool newAggressor = aggressor != target;
            aggressor = target;
            provoker = target;
            CalmingFor = 0f;

            // Whatever route got here — a hit, a full meter, an ally's alert, a restore — the agent
            // is now fighting, so the meter reads full. Without this a Provoke that skipped the
            // meter (an alert, a scripted event) would leave it at zero and the telegraph would
            // show a calm agent in the middle of a gunfight.
            aggression = AggressionMath.Max;
            RaiseBandChanged();

            targeting.ForceTarget(target);

            if (announce && newAggressor && TryGetComponent(out AlertBroadcaster broadcaster))
                broadcaster.Broadcast(target, target.position);
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
            Provoke(target, announce: false);

            // Not viable — dead, despawned, or retired from the registry. Nothing was restored, so
            // the latch stays down and OnEnable is free to reset as usual.
            if (aggressor == null)
                return;

            CalmingFor = Mathf.Max(0f, calmingFor);
            restoredGrudge = true;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Sets the meter without raising <see cref="BandChanged"/>: the bands are what the
        /// telegraph barks and postures on, and an agent that was merely wary when you quit must
        /// come back wary silently rather than greeting the loading screen with a threat. The
        /// listener reads the band on enable instead.
        /// </para>
        /// <para>
        /// Only for an agent that is NOT provoked — a restored grudge goes through
        /// <see cref="RestoreGrudge"/>, which fills the meter itself.
        /// </para>
        /// </summary>
        public void RestoreAggression(float value, Transform from)
        {
            if (IsProvoked)
                return;

            aggression = Mathf.Clamp(value, 0f, AggressionMath.Max);
            provoker = from;
            band = Band;
            restoredAggression = true;
        }

        /// <summary>Drop the grudge and go back to being peaceful.</summary>
        public void Forget()
        {
            if (aggressor != null && targeting != null)
                targeting.ClearTarget();

            aggressor = null;
            provoker = null;
            CalmingFor = 0f;

            // Empty the meter too, or the creature that just forgot you is still reading full and
            // the next point of anything at all puts it straight back into a fight.
            aggression = 0f;
            RaiseBandChanged();
        }

        private void Update()
        {
            if (aggressor == null)
            {
                // Forgiveness, and only while below the top: above it the leash rule applies
                // instead, because a creature you are standing on top of must not calm down on a
                // timer. That is the same split the grudge always had, now with a slope under it.
                if (aggression > 0f)
                {
                    aggression = AggressionMath.Cool(aggression, Time.deltaTime, aggressionSettings.calmRate);
                    if (aggression <= 0f)
                        provoker = null;
                    RaiseBandChanged();
                }

                return;
            }

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

            // A zero attackAt is a creature that is permanently at the top of its own meter, which
            // reads in play as one that attacks on sight for no reason anybody can see.
            aggressionSettings.attackAt = Mathf.Clamp(aggressionSettings.attackAt, 1f, AggressionMath.Max);
            aggressionSettings.hitGain = Mathf.Max(0f, aggressionSettings.hitGain);
            aggressionSettings.allyHurtGain = Mathf.Max(0f, aggressionSettings.allyHurtGain);
            aggressionSettings.gunshotGain = Mathf.Max(0f, aggressionSettings.gunshotGain);
            aggressionSettings.menaceGainPerSecond = Mathf.Max(0f, aggressionSettings.menaceGainPerSecond);
            aggressionSettings.menaceRange = Mathf.Max(0f, aggressionSettings.menaceRange);
            aggressionSettings.menaceDelay = Mathf.Max(0f, aggressionSettings.menaceDelay);
            aggressionSettings.trespassGainPerSecond = Mathf.Max(0f, aggressionSettings.trespassGainPerSecond);
            aggressionSettings.calmRate = Mathf.Max(0f, aggressionSettings.calmRate);
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
