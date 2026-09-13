// Deals melee damage to a target when within attack range.
// Claims movement: returns StopAndFace while in range (preempting ChaseModule) and null otherwise,
// so ChaseModule at lower priority can drive the approach when the target is out of melee reach.
using System;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class CloseCombatModule : BehaviourModuleBase
    {
        [Header("Attack")]
        [SerializeField] private float attackRange = 5f;
        [Tooltip("Fraction of attackRange the target must exceed before the agent gives up the swing " +
                 "and starts closing again. 1.15 means it holds position out to 115% of attackRange. " +
                 "Without this gap the winner alternates every frame at the range boundary and the " +
                 "NavMesh path is discarded and re-requested until the agent visibly stutters.")]
        [SerializeField] [Range(1f, 2f)] private float rangeExitFactor = 1.15f;
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private int attackDamage = 10;
        [Tooltip("Seconds the agent stays locked in StopAndFace after a swing fires — keeps the attack committed so it can't start walking mid-animation if the target drifts out of attackRange. Typically set to the length of the attack animation.")]
        [SerializeField] private float attackCommitDuration = 0.5f;

        // Knockback is OFF by default, and that is deliberate: this module is shared, and giving
        // every melee creature in the project a shove because one of them needed it would retune
        // three shipped fights silently. A creature that should knock you back says so.
        [Header("Knockback")]
        [Tooltip("Metres per second the victim is thrown at. 0 disables knockback entirely.")]
        [SerializeField] private float knockbackSpeed;

        [Tooltip("Upward share of the shove, as a fraction of knockbackSpeed. A little lift is what " +
                 "makes a hit read as an impact rather than a nudge; too much turns it into a punt.")]
        [SerializeField] [Range(0f, 1f)] private float knockbackLift = 0.3f;

        [Tooltip("How far a hit CREATURE is thrown. A creature's transform belongs to its motor and " +
                 "forces never land on it, so it is asked for a leap instead — see BlastPush.")]
        [SerializeField] private float knockbackLeapDistance = 4f;
        [SerializeField] private float knockbackLeapHeight = 1.6f;
        [SerializeField] private float knockbackLeapDuration = 0.55f;

        [Tooltip("Mass a loose Rigidbody is priced against, so a crate and a pebble both move a " +
                 "believable amount. Only used for physics props — players and creatures take the " +
                 "two routes above.")]
        [SerializeField] private float knockbackMassReference = 80f;
        [Tooltip("Clamp on that mass compensation: nothing gets launched into orbit, nothing is immovable.")]
        [SerializeField] private Vector2 knockbackMassScale = new Vector2(0.4f, 2.5f);

        [Header("Animation")]
        [Tooltip("Trigger to fire on each attack. Leave empty to disable.")]
        [SerializeField] private string attackAnimTrigger = "Meele";

        [Header("Events")]
        public UnityEvent<Transform> OnAttack;
        public event Action OnAttackEvent;

        [Header("Audio")]
        [SerializeField] private SfxId attackId = SfxId.EntityAttack;
        [SerializeField] private EventReference attackSound;

        private float cooldownTimer;
        // Ticks down after a swing fires; while > 0, the module keeps returning StopAndFace regardless
        // of target distance so the in-progress swing can't be interrupted by Chase.
        private float commitTimer;
        // True while the agent is holding position to fight. Combined with rangeExitFactor this is
        // the hysteresis: entering costs attackRange, leaving costs attackRange * rangeExitFactor.
        private bool engaged;
        private Animator animator;

        // Whose swing this is. Cached rather than resolved per message — see AgentAuthority. Only
        // ever read on the receiving side here: the deciding side is gated one level up, in
        // AgentController, and asking twice would be a second answer free to drift from the first.
        private AgentAuthority authority;

        // Read by ChaseModule (to tighten chaseStopDistance and skip herd-spread offsets that would
        // park the agent outside melee reach) and by AgentTargeting (to cover the range in its
        // acquisition window).
        public float AttackRange => attackRange;

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // A swing's cadence outlives a session. Reloading at zero cooldown hands whoever reloaded a
        // free hit — a creature caught mid-recovery comes back able to strike immediately — and
        // reloading with commitTimer at zero lets Chase reclaim a frame that a swing had committed.
        //
        // OnEnable clears all three, and runs on either side of a restore depending on the
        // hydration path, hence the latch. See Core/Persistence/Adapters/CombatCadenceSaveable.cs.
        private bool cadenceRestored;

        public float CooldownTimer => cooldownTimer;
        public float CommitTimer => commitTimer;
        public bool Engaged => engaged;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreCadence(float cooldown, float commit, bool wasEngaged)
        {
            cadenceRestored = true;
            cooldownTimer = cooldown;
            commitTimer = commit;
            engaged = wasEngaged;
        }

        private void Reset() => SetPriorityDefault(ModulePriority.MeleeAttack);

        private void OnEnable()
        {
            // A restore already set this module up. Consumed, so a later genuine enable — a
            // threshold reaction, an ownership change — still resets the cadence as it always did.
            if (cadenceRestored)
            {
                cadenceRestored = false;
            }
            else
            {
                cooldownTimer = 0f;
                commitTimer = 0f;
                engaged = false;
            }

            // Watching machines listen so the authority can tell them a swing happened. The
            // authority registers too and simply never receives its own broadcast — NetRelay
            // filters the sender out — which is the same shape EntityEquipmentController uses for
            // NetMsg.ItemUsed. Registered in OnEnable rather than Awake so it is paired with the
            // OnDisable below: NetAuthority switches components on and off as ownership moves, and
            // a subscription that outlived a disable would present a swing twice.
            this.NetOn(NetMsg.AgentActed, OnAgentActed);
        }

        private void OnDisable() => this.NetOff(NetMsg.AgentActed, OnAgentActed);

        private void Awake()
        {
            authority = new AgentAuthority(this);
            FindChildByName("Sword")?.SetActive(IsActive);
            animator = GetComponentInChildren<Animator>();
        }

        // Being carried — onto a walker's deck, into a seat — moves this agent under a different
        // NetworkObject. See AgentAuthority.Invalidate.
        private void OnTransformParentChanged() => authority?.Invalidate();

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Advance timers every frame so a target stepping out and back can't instant-hit,
            // and so the commit window decays even on frames we're not returning an intent.
            cooldownTimer -= deltaTime;
            commitTimer -= deltaTime;

            AgentTargeting targeting = context.Targeting;
            Transform target = targeting != null && targeting.HasTarget ? targeting.Target : null;
            if (target == null)
            {
                engaged = false;
                return null;
            }

            // Mid-swing: keep the agent planted and facing the target regardless of distance,
            // so Chase can't reclaim the frame and start walking while the attack animation plays.
            if (commitTimer > 0f)
                return MoveIntent.StopAndFace(target.position);

            float distance = targeting.DistanceToTarget;
            float threshold = engaged ? attackRange * rangeExitFactor : attackRange;
            if (distance > threshold)
            {
                engaged = false;
                return null;
            }

            engaged = true;

            // Only swing when genuinely inside attackRange — the exit factor exists to stop the
            // agent walking away, not to extend its reach.
            if (cooldownTimer <= 0f && distance <= attackRange)
            {
                Attack(target);
                cooldownTimer = attackCooldown;
                commitTimer = attackCommitDuration;
            }

            return MoveIntent.StopAndFace(target.position);
        }

        /// <summary>
        /// One swing. Runs on the machine that simulates this agent and on no other.
        ///
        /// <para>
        /// There is deliberately no authority check here, and that is worth stating because the
        /// bug it prevents is severe: <see cref="NetDamage"/> lands a hit locally on the server and
        /// forwards it as a REQUEST from a client, and the server honours every request it gets, so
        /// a swing that runs on the host and on two clients bills the target three times. The gate
        /// is one level up, in <see cref="AgentController"/>, which stops ticking modules at all on
        /// a machine that does not own the agent — and this method is reachable from nowhere else.
        /// Repeating the check here would be a second answer to the same question, free to drift
        /// from the first.
        /// </para>
        /// <para>
        /// The consequence used to be that the sound and the trigger only fired on the simulating
        /// machine, so nobody else saw the swing at all. They now go out as
        /// <see cref="NetMsg.AgentActed"/> — see <see cref="PresentSwing"/>, which is the half of
        /// this method that runs everywhere.
        /// </para>
        /// </summary>
        private void Attack(Transform target)
        {
            var health = target.GetComponentInChildren<HealthComponent>();
            if (health != null && health.Alive)
                NetDamage.Apply(health.gameObject, attackDamage, transform);

            // Alongside the damage, not inside the presentation below: a shove moves the victim,
            // and where the victim ends up is exactly the state every machine must agree on.
            // BlastPush routes it correctly for each kind of target — a player is
            // owner-authoritative and gets NetMsg.Flung, a creature's transform belongs to its
            // motor so it is asked for a leap, and a loose Rigidbody takes a mass-scaled impulse.
            Knock(target);

            // Deliberately NOT part of the presentation below, and not carried in the message
            // either: this hands out the TARGET, which is exactly the divergent state AgentActed
            // exists so that watchers never have to guess at. A handler holding the victim is one
            // edit away from being a second machine that can damage it.
            OnAttack?.Invoke(target);

            // The agent's own position, matching where the sound was played from before this was
            // split — a swing has no muzzle, so its origin is the body that made it.
            Vector3 origin = transform.position;
            Vector3 direction = target.position - origin;

            PresentSwing(origin);

            // Once per swing, from inside the cooldown gate in Tick — never per frame. A committed
            // swing already holds the agent still for attackCommitDuration, so the wire rate is
            // bounded by attackCooldown and nothing else.
            AgentActionRelay.Broadcast(this, AgentAction.Melee, origin, direction);
        }

        /// <summary>
        /// Throw the victim away from the attacker.
        ///
        /// <para>
        /// Authority only — it is called from <see cref="Attack"/>, which is the deciding side.
        /// Running it on a watcher would shove the same victim once per machine in the session,
        /// which is the movement equivalent of the damage bug the <c>Cosmetic</c> split exists to
        /// prevent.
        /// </para>
        /// <para>
        /// The direction is flattened before the lift is added back, so the shove is always
        /// outward along the ground rather than steeply up when the attacker's head happens to be
        /// above the victim — which for a creature that hits with a lowered head is most of the
        /// time.
        /// </para>
        /// </summary>
        private void Knock(Transform target)
        {
            if (knockbackSpeed <= 0f)
                return;

            Vector3 away = target.position - transform.position;
            away.y = 0f;
            // Directly on top of each other: shove along the attacker's facing rather than
            // resolving a zero vector to no direction at all.
            if (away.sqrMagnitude < 1e-6f)
                away = transform.forward;

            Vector3 velocity = away.normalized * knockbackSpeed
                             + Vector3.up * (knockbackSpeed * knockbackLift);

            BlastPush.Apply(target.GetComponentInChildren<Collider>(), target.gameObject, velocity,
                            knockbackSpeed,
                            BlastPush.Leap.Proportional(knockbackLeapDistance, knockbackLeapHeight,
                                                        knockbackLeapDuration),
                            knockbackMassReference, knockbackMassScale);
        }

        /// <summary>
        /// One swing's look and sound. Runs on every machine — here as part of attacking, on a
        /// watcher because the authority said it happened.
        ///
        /// <para>
        /// Idempotent by construction: it starts a sound and sets an animator trigger and reads no
        /// state at all, so a message that arrived twice would be a doubled sound rather than a
        /// doubled swing. Nothing below this line may damage, spawn or consume anything — that is
        /// the whole boundary, and <see cref="Attack"/> above it is the only side that decides.
        /// </para>
        /// </summary>
        private void PresentSwing(Vector3 origin)
        {
            Sfx.Play(attackId, origin, attackSound, GetInstanceID());

            if (animator && !string.IsNullOrEmpty(attackAnimTrigger))
                animator.SetTrigger(attackAnimTrigger);

            OnAttackEvent?.Invoke();
        }

        /// <summary>
        /// A watching machine drawing the swing the authority actually made.
        /// </summary>
        private void OnAgentActed(in NetArg arg, ulong sender)
        {
            // Is this message even ours? An unrecognised kind is ignored rather than assumed — see
            // AgentAction — and it matters on this exact channel: an agent carrying an
            // AgentRangedCombatModule as well broadcasts its shots here, and swinging a sword for a
            // rifle shot is worse than playing nothing.
            if (arg.A != AgentAction.Melee) return;

            // The deciding machine already drew this while performing it, and NetRelay excludes the
            // sender from its own broadcast — so in practice only a watcher gets here. Asking
            // anyway is what keeps the handler idempotent should the message ever arrive by another
            // route, and it is the same guard EntityEquipmentController.OnItemUsedElsewhere uses.
            // A null authority means Awake has not run (an EditMode fixture), which reads as "this
            // machine decides" and therefore presents nothing.
            if (authority == null || authority.SimulatedHere) return;

            PresentSwing(arg.P);
        }

        private GameObject FindChildByName(string childName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.gameObject;
            return null;
        }

        protected override void OnValidate()
        {
            attackRange = Mathf.Max(0.1f, attackRange);
            attackCooldown = Mathf.Max(0.1f, attackCooldown);
            attackDamage = Mathf.Max(0, attackDamage);
            attackCommitDuration = Mathf.Max(0f, attackCommitDuration);
            knockbackSpeed = Mathf.Max(0f, knockbackSpeed);
            knockbackLeapDistance = Mathf.Max(0f, knockbackLeapDistance);
            knockbackLeapHeight = Mathf.Max(0f, knockbackLeapHeight);
            knockbackLeapDuration = Mathf.Max(0.05f, knockbackLeapDuration);
            knockbackMassReference = Mathf.Max(0.1f, knockbackMassReference);
            // An inverted range silently clamps every mass to the wrong end.
            knockbackMassScale.x = Mathf.Max(0.01f, knockbackMassScale.x);
            knockbackMassScale.y = Mathf.Max(knockbackMassScale.x, knockbackMassScale.y);
            SetMinPriority(ModulePriority.MeleeAttack);
        }
    }
}
