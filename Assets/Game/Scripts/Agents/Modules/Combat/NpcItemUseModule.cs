// Decides WHEN an NPC uses the artifact it is carrying. EntityEquipmentController does the using.
//
// Kept apart from AgentRangedCombatModule on purpose. That module drives a weapon defined by three
// ScriptableObjects and owns the whole engagement — range bands, strafing, backing off. This one
// drives an actual InventoryItem out of the NPC's own bag, which means the thing being fired is a
// real prefab the player can loot and fire themselves. An NPC can carry both: the artifact for the
// gun in its hand, the profile weapon for a built-in turret.
//
// Side-effect module, so it never claims the frame. It does claim the FACING channel, which is the
// whole reason that channel exists — the NPC keeps its gun on target while Chase, Flee or the
// formation still own where its feet go.
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class NpcItemUseModule : BehaviourModuleBase, IFacingModule
    {
        public enum Trigger
        {
            /// <summary>Fire at whatever AgentTargeting has acquired. Guns, throwables.</summary>
            TargetInRange,

            /// <summary>Use on itself when health drops below a fraction. Stims, shields.</summary>
            WhenHurt,

            /// <summary>Use on a fixed timer regardless of the world. Beacons, tools, flavour.</summary>
            OnInterval,
        }

        [Header("What to use")]
        [Tooltip("Which inventory slot holds the artifact. The module equips it before using, so " +
                 "two of these on one NPC gives it a weapon it swaps to when the range suits.")]
        [SerializeField] private int slotIndex = 0;

        [Tooltip("Equip this slot when the trigger fires. Off means only use it if it already " +
                 "happens to be in hand.")]
        [SerializeField] private bool equipBeforeUse = true;

        [Header("When")]
        [SerializeField] private Trigger trigger = Trigger.TargetInRange;

        [Tooltip("Closest the target may be. Below this the module passes, so a melee module or a " +
                 "back-off module handles it instead.")]
        [SerializeField] private float minRange = 3f;

        [Tooltip("Furthest the target may be. Also widens AgentTargeting's acquisition range at " +
                 "startup, so an NPC equipped to shoot 40 m actually notices things at 40 m.")]
        [SerializeField] private float maxRange = 30f;

        [Range(0f, 1f)]
        [Tooltip("WhenHurt only: use once health falls to this fraction of maximum.")]
        [SerializeField] private float hurtThreshold = 0.4f;

        [Header("Cadence")]
        [SerializeField] private float cooldown = 1.4f;

        [Tooltip("Shots per trigger. 1 for a single shot.")]
        [SerializeField] private int burstCount = 1;

        [SerializeField] private float burstInterval = 0.14f;

        [Tooltip("Seconds after acquiring a target before the first shot. Without it an NPC that " +
                 "walks round a rock fires in the same frame it sees you, which reads as a trap " +
                 "rather than a person.")]
        [SerializeField] private float reactionDelay = 0.45f;

        [Header("Aim")]
        [Tooltip("Height up the target to aim for, in metres above its origin. A character's origin " +
                 "is between its feet, so 0 shoots the ground it stands on.")]
        [SerializeField] private float targetHeightOffset = 1.1f;

        [Tooltip("Aim where the target will be rather than where it is, in seconds of lead. 0 " +
                 "always aims at the present, which against a running player means always missing " +
                 "behind.")]
        [SerializeField] private float leadSeconds = 0.25f;

        [Tooltip("Degrees of random spread added per shot. 0 is a perfect marksman, which is not a " +
                 "compliment — it reads as a scripted hit rather than an NPC aiming.")]
        [SerializeField] private float spreadDegrees = 2.5f;

        [Tooltip("Require line of sight before firing. Off makes the NPC shoot through the terrain.")]
        [SerializeField] private bool requireLineOfSight = true;

        [SerializeField] private LayerMask lineOfSightBlockers = ~0;

        [Header("Facing")]
        [Tooltip("Turn the body toward the target while this module has something to shoot at.")]
        [SerializeField] private bool claimFacing = true;

        [SerializeField] private int facingPriority = ModulePriority.RangedAttack;

        [Header("Animation")]
        [Tooltip("Animator trigger fired on each use. Leave empty to drive no animation.")]
        [SerializeField] private string useAnimTrigger = string.Empty;

        // Side-effect only: this module says when to pull a trigger, never where to walk.
        public override bool ClaimsMovement => false;

        public int FacingPriority => facingPriority;

        /// <summary>
        /// Read by AgentTargeting at Awake so acquisition covers this weapon's reach — the same
        /// contract AgentRangedCombatModule.MaxRange satisfies. An NPC that can shoot further than
        /// it can see never starts the fight it is equipped for.
        /// </summary>
        public float MaxRange => trigger == Trigger.TargetInRange ? maxRange : 0f;

        private EntityEquipmentController equipment;
        private HealthComponent health;
        private Animator animator;

        private float cooldownTimer;
        private int burstRemaining;
        private float burstTimer;
        private float targetHeldFor;
        private Transform lastTarget;
        private Vector3 lastTargetPosition;
        private Vector3 targetVelocity;
        private bool hasFacingTarget;
        private Vector3 facingPoint;

        private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);

        private void Awake()
        {
            equipment = GetComponent<EntityEquipmentController>();
            health = GetComponent<HealthComponent>();
            animator = GetComponentInChildren<Animator>();

            if (equipment == null)
            {
                Debug.LogWarning($"{name}: NpcItemUseModule needs an EntityEquipmentController on " +
                                 "the same GameObject to hold anything. It will do nothing.", this);
            }
        }

        private void OnEnable()
        {
            // A restore already set this module up. Consumed, so a later genuine enable still
            // resets the cadence as it always did.
            if (cadenceRestored)
            {
                cadenceRestored = false;
                return;
            }

            cooldownTimer = 0f;
            burstRemaining = 0;
            targetHeldFor = 0f;
            hasFacingTarget = false;
        }

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // `targetHeldFor` is the one that matters most and is the least obvious: it is the
        // has-aimed-long-enough accumulator behind `reactionDelay`, so losing it means every NPC in
        // the world grants the player another half second of grace after each load. The cooldown
        // and the burst are the same free-shot problem the other combat modules have.
        //
        // See Core/Persistence/Adapters/CombatCadenceSaveable.cs.
        private bool cadenceRestored;

        public float CooldownTimer => cooldownTimer;
        public int BurstRemaining => burstRemaining;
        public float BurstTimer => burstTimer;
        public float TargetHeldFor => targetHeldFor;
        public Transform LastTarget => lastTarget;
        public Vector3 LastTargetPosition => lastTargetPosition;
        public bool HasFacingTarget => hasFacingTarget;
        public Vector3 FacingPoint => facingPoint;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreCadence(float cooldown, int burstLeft, float burst, float heldFor,
                                   bool facing, Vector3 face)
        {
            cadenceRestored = true;
            cooldownTimer = cooldown;
            burstRemaining = burstLeft;
            burstTimer = burst;
            targetHeldFor = heldFor;
            hasFacingTarget = facing;
            facingPoint = face;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Seeds the lead-prediction tracker with the target and the position it was differencing
        /// against, so the first frame after a load does not read a whole session's displacement as
        /// one frame of velocity and fire the shot into the next county.
        /// </summary>
        public void RestoreAimTracking(Transform target, Vector3 lastPosition)
        {
            lastTarget = target;
            lastTargetPosition = target != null ? lastPosition : Vector3.zero;
            targetVelocity = Vector3.zero;
        }

        public override string ModuleDescription =>
            "Uses an artifact from this NPC's own EntityInventoryComponent — the same UsableItem " +
            "prefab the player would equip.\n\n" +
            "• slotIndex — which inventory slot to use; equipped automatically\n" +
            "• trigger — TargetInRange (guns), WhenHurt (stims), OnInterval (tools)\n" +
            "• minRange / maxRange — the band it fires in; maxRange also widens target acquisition\n" +
            "• leadSeconds / spreadDegrees — aim quality. Spread of 0 reads as scripted, not skilled.\n" +
            "• Claims the FACING channel, so the NPC keeps its gun on target while other modules walk it\n\n" +
            "Add two of these with different slots and ranges to give an NPC a weapon it swaps.";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            hasFacingTarget = false;

            if (equipment == null) return null;

            cooldownTimer -= deltaTime;

            // A burst in progress outranks starting anything new, so an interrupted volley finishes
            // rather than leaving the NPC with one round of a three-round burst fired.
            if (burstRemaining > 0)
            {
                TickBurst(in context, deltaTime);
                return null;
            }

            switch (trigger)
            {
                case Trigger.TargetInRange: TickTargetTrigger(in context, deltaTime); break;
                case Trigger.WhenHurt:      TickHurtTrigger();                        break;
                case Trigger.OnInterval:    TickIntervalTrigger();                    break;
            }

            return null;
        }

        // ── Triggers ─────────────────────────────────────────────────────────────

        private void TickTargetTrigger(in AgentContext context, float deltaTime)
        {
            AgentTargeting targeting = context.Targeting;
            Transform target = targeting != null ? targeting.Target : null;

            if (target == null)
            {
                targetHeldFor = 0f;
                lastTarget = null;
                equipment.ClearAim();
                return;
            }

            TrackTargetVelocity(target, deltaTime);

            float distance = targeting.DistanceToTarget;
            if (distance < minRange || distance > maxRange)
                return;

            Vector3 aim = PredictAimPoint(target);

            if (requireLineOfSight && !HasLineOfSight(aim))
                return;

            // Aim continuously while in the band, whether or not the cooldown is up. Swinging onto
            // target only at the moment of firing is what makes an NPC look like a turret.
            equipment.AimAt(aim);
            facingPoint = target.position;
            hasFacingTarget = claimFacing;

            targetHeldFor += deltaTime;
            if (targetHeldFor < reactionDelay) return;

            if (cooldownTimer > 0f) return;

            BeginBurst();
        }

        private void TickHurtTrigger()
        {
            if (health == null || cooldownTimer > 0f) return;
            if (health.GetMaxHealth <= 0 || !health.Alive) return;

            float fraction = health.GetHealth / (float)health.GetMaxHealth;
            if (fraction > hurtThreshold) return;

            if (!Equip()) return;

            if (equipment.TryUseOnSelf())
            {
                cooldownTimer = cooldown;
                FireAnimation();
            }
        }

        private void TickIntervalTrigger()
        {
            if (cooldownTimer > 0f) return;
            if (!Equip()) return;

            if (equipment.TryUseForward())
            {
                cooldownTimer = cooldown;
                FireAnimation();
            }
        }

        // ── Firing ───────────────────────────────────────────────────────────────

        private void BeginBurst()
        {
            if (!Equip()) return;

            burstRemaining = Mathf.Max(1, burstCount);
            burstTimer = 0f;
            cooldownTimer = cooldown;
        }

        private void TickBurst(in AgentContext context, float deltaTime)
        {
            burstTimer -= deltaTime;
            if (burstTimer > 0f) return;

            AgentTargeting targeting = context.Targeting;
            Transform target = targeting != null ? targeting.Target : null;

            // The target dying or breaking away mid-burst ends it. Continuing would put the rest of
            // the volley through the space where somebody used to be.
            if (target == null)
            {
                burstRemaining = 0;
                return;
            }

            Vector3 aim = ApplySpread(PredictAimPoint(target));

            equipment.AimAt(aim);
            facingPoint = target.position;
            hasFacingTarget = claimFacing;

            if (equipment.TryUseAt(aim))
                FireAnimation();

            burstRemaining--;
            burstTimer = burstInterval;
        }

        private bool Equip()
        {
            if (!equipBeforeUse) return equipment.HasItem;

            if (equipment.EquippedSlotIndex != slotIndex)
                equipment.EquipSlot(slotIndex);

            return equipment.HasItem;
        }

        private void FireAnimation()
        {
            if (animator != null && !string.IsNullOrEmpty(useAnimTrigger))
                animator.SetTrigger(useAnimTrigger);
        }

        // ── Aim ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Track the target's velocity ourselves rather than asking it.
        ///
        /// The target may be a player whose Rigidbody is owner-authoritative and replicated, a
        /// creature driven by a NavMeshAgent, or a vehicle — three different places to read a
        /// velocity from, two of which lie on a machine that does not own the thing. Differencing
        /// its position is the one answer that is true everywhere.
        /// </summary>
        private void TrackTargetVelocity(Transform target, float deltaTime)
        {
            if (target != lastTarget)
            {
                lastTarget = target;
                lastTargetPosition = target.position;
                targetVelocity = Vector3.zero;
                return;
            }

            if (deltaTime <= 0f) return;

            Vector3 instant = (target.position - lastTargetPosition) / deltaTime;
            lastTargetPosition = target.position;

            // Smoothed, because a single frame's difference across a replicated transform is mostly
            // network jitter, and leading a target by jitter is worse than not leading it at all.
            targetVelocity = Vector3.Lerp(targetVelocity, instant, 1f - Mathf.Exp(-6f * deltaTime));
        }

        private Vector3 PredictAimPoint(Transform target)
        {
            Vector3 point = target.position + Vector3.up * targetHeightOffset;
            return point + targetVelocity * leadSeconds;
        }

        private Vector3 ApplySpread(Vector3 aim)
        {
            if (spreadDegrees <= 0f) return aim;

            Vector3 origin = equipment.FireOrigin;
            Vector3 direction = aim - origin;
            float distance = direction.magnitude;
            if (distance < 0.01f) return aim;

            Quaternion deviation = Quaternion.Euler(
                UnityEngine.Random.Range(-spreadDegrees, spreadDegrees),
                UnityEngine.Random.Range(-spreadDegrees, spreadDegrees),
                0f);

            return origin + deviation * (direction / distance) * distance;
        }

        private bool HasLineOfSight(Vector3 aim)
        {
            Vector3 origin = equipment.FireOrigin;
            Vector3 direction = aim - origin;
            float distance = direction.magnitude;

            if (distance < 0.01f) return true;

            // Stop just short of the target, or the target's own collider registers as the thing
            // blocking the shot and the NPC never fires at anybody.
            return !Physics.Raycast(origin, direction / distance, distance - 0.5f,
                                    lineOfSightBlockers, QueryTriggerInteraction.Ignore);
        }

        // ── Facing channel ───────────────────────────────────────────────────────

        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            facePosition = facingPoint;
            return hasFacingTarget;
        }

        protected override void OnValidate()
        {
            slotIndex = Mathf.Max(0, slotIndex);
            minRange = Mathf.Max(0f, minRange);
            maxRange = Mathf.Max(minRange + 0.5f, maxRange);
            cooldown = Mathf.Max(0.05f, cooldown);
            burstCount = Mathf.Max(1, burstCount);
            burstInterval = Mathf.Max(0.02f, burstInterval);
            reactionDelay = Mathf.Max(0f, reactionDelay);
            leadSeconds = Mathf.Max(0f, leadSeconds);
            spreadDegrees = Mathf.Max(0f, spreadDegrees);
            targetHeightOffset = Mathf.Max(0f, targetHeightOffset);
        }

        private void OnDrawGizmosSelected()
        {
            if (trigger != Trigger.TargetInRange) return;

            Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, maxRange);
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, minRange);
        }
    }
}
