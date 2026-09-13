// Ranged combat module driven by three ScriptableObject assets:
//   AgentWeaponDefinition  — projectile, damage, audio
//   AgentFireProfile       — range, cooldown, burst cadence
//   AgentAimProfile        — spread, lead prediction, LoS requirement
//
// Owns positioning for the whole engagement, not just the trigger:
//   out of range        — passes, so ChaseModule closes the gap.
//   inside preferred    — backs off to preferredRange, still firing and still facing the target.
//   in the band         — holds station, strafing if the profile allows it.
// Facing is claimed separately (IFacingModule) so the agent keeps its gun on the target while
// side-stepping or retreating rather than turning to look where it is walking.
//
// Handing movement to ChaseModule while a target is in weapon range does not work: Chase's only
// goal is to close the distance, so the agent walks into its target's face while shooting.
// CloseCombatModule at higher priority still preempts if something gets right on top of us.
// OnFire  — fires each shot (position of muzzle)
// OnMiss  — fires when a projectile lands but hits no IDamageable
// OnKill  — fires when a shot kills the target
using System;
using FMODUnity;
using SpaceGame.Audio;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Weapons;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    public class AgentRangedCombatModule : BehaviourModuleBase, IFacingModule
    {
        [Header("Engagement")]
        [Tooltip("Fraction of the fire profile's maxRange the target must exceed before the agent " +
                 "stops holding position and lets Chase close again. Without this gap the winner " +
                 "alternates every frame at the edge of the fire band and the NavMesh path is " +
                 "discarded and re-requested until the agent visibly stutters.")]
        [SerializeField] [Range(1f, 2f)] private float rangeExitFactor = 1.1f;

        [Header("Weapon")]
        [SerializeField] private AgentWeaponDefinition weapon;
        [SerializeField] private AgentFireProfile fireProfile;
        [SerializeField] private AgentAimProfile aimProfile;

        [Tooltip("World-space transform used as the projectile spawn point. " +
                 "If left empty, falls back to the child named 'Gun', then to this transform.")]
        [SerializeField] private Transform muzzleSocket;
        [Tooltip("Meters in front of the muzzle (along muzzle.forward) where the projectile actually spawns. " +
                 "Keeps the projectile clear of the gun model and the agent's armature on the first frame.")]
        [SerializeField] private float muzzleForwardOffset = 0.4f;
        [Tooltip("When true, spawns the weapon model from the weapon asset at runtime. " +
                 "Disable if the weapon is already placed in the prefab hierarchy (e.g. parented to a hand bone).")]
        [SerializeField] private bool spawnWeaponModel = false;
        [Tooltip("Optional. When assigned (or found on this object), overrides weapon and muzzleSocket with the active slot.")]
        [SerializeField] private WeaponMount weaponMount;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [Tooltip("Trigger to fire on each shot. Leave empty to disable.")]
        [SerializeField] private string shootAnimTrigger = "AssualtShoot";
        [Tooltip("Bool to set while the agent is in firing range and aiming. Leave empty to disable.")]
        [SerializeField] private string aimAnimBool = "IsAiming";

        [Header("Events")]
        public UnityEvent<Vector3> OnFire;
        public UnityEvent<Vector3> OnMiss;
        public event Action OnFireEvent;
        public event Action OnKillEvent;

        private float cooldownTimer;
        private int burstRemaining;
        private float burstTimer;
        private int currentBurstSpread;
        private PerceptionModule perception;
        // True while holding position to fire. With rangeExitFactor this is the hysteresis:
        // entering the band costs maxRange, leaving it costs maxRange * rangeExitFactor.
        private bool engaged;
        // What the facing channel points at while engaged, so the agent keeps its gun on the target
        // while backing off or strafing instead of turning to face where it is walking.
        private Transform faceTarget;
        private float strafeTimer;
        private Vector3 strafeDestination;
        private bool hasStrafeDestination;
        // The target this module was firing at when the last shot landed, so OnKillEvent can be
        // attributed even after AgentTargeting has already moved on to someone else.
        private IDamageable firingAt;

        // Whose shot this is. Cached rather than resolved per trigger pull — see AgentAuthority.
        private AgentAuthority authority;

        // Read by AgentTargeting at Awake so acquisition range covers the fire band — otherwise an
        // agent can be equipped for a fight it will never notice it could start.
        public float MaxRange => fireProfile != null ? fireProfile.maxRange : 0f;

        /// <summary>
        /// The weapon this barrel is firing right now — the mounted slot when there is a
        /// WeaponMount, the serialized fallback otherwise.
        ///
        /// <para>
        /// A watching machine resolves it the same way and lands on the same asset, because nothing
        /// swaps a WeaponMount slot on its own: <see cref="WeaponMount.Equip"/> is only reachable
        /// from a UnityEvent or a script, so both machines are reading the same serialized index.
        /// If something ever does start swapping mid-fight, the index belongs in the message's
        /// spare <see cref="NetArg.B"/> and a mismatch should drop the shot — which is the rule
        /// EntityEquipmentController already applies to a hotbar slot.
        /// </para>
        /// </summary>
        private AgentWeaponDefinition ActiveWeapon =>
            weaponMount != null ? weaponMount.ActiveDefinition : weapon;

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // The cadence below is durable state, not scratch: a module that reloads at zero cooldown
        // is a free shot for whoever reloads, and one that reloads with burstRemaining at zero
        // drops the rest of a volley that was already in the air.
        //
        // OnEnable clears every one of these, and it runs both before and after a restore
        // depending on the hydration path — hence the latch. See the saver in
        // Core/Persistence/Adapters/CombatCadenceSaveable.cs.
        private bool cadenceRestored;

        public float CooldownTimer => cooldownTimer;
        public int BurstRemaining => burstRemaining;
        public float BurstTimer => burstTimer;
        public int BurstSpread => currentBurstSpread;
        public bool Engaged => engaged;
        public float StrafeTimer => strafeTimer;
        public bool HasStrafeDestination => hasStrafeDestination;
        public Vector3 StrafeDestination => strafeDestination;

        /// <summary>
        /// Whatever this barrel last fired at, as an object. Only used to attribute a kill, so a
        /// referent that has since gone is a perfectly good "nobody".
        /// </summary>
        public GameObject FiringAtObject => firingAt is Component c && c != null ? c.gameObject : null;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreCadence(float cooldown, int burstLeft, float burst, int spread,
                                   bool wasEngaged, float strafe, bool hasStrafeDest,
                                   Vector3 strafeDest)
        {
            cadenceRestored = true;
            cooldownTimer = cooldown;
            burstRemaining = burstLeft;
            burstTimer = burst;
            currentBurstSpread = spread;
            engaged = wasEngaged;
            strafeTimer = strafe;
            hasStrafeDestination = hasStrafeDest;
            strafeDestination = strafeDest;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreFiringAt(GameObject target)
        {
            firingAt = target != null ? target.GetComponentInChildren<IDamageable>() : null;
        }

        private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);
        private void OnEnable()
        {
            // A restore already set this module up. Consumed rather than left standing, so the next
            // genuine enable — a threshold reaction switching the module back on, an ownership
            // change — resets the cadence as it always did.
            if (cadenceRestored)
            {
                cadenceRestored = false;
            }
            else
            {
                cooldownTimer = 0f;
                burstRemaining = 0;
                engaged = false;
                hasStrafeDestination = false;
                strafeTimer = 0f;
            }

            // Watching machines listen so the authority can tell them a shot left this barrel. The
            // authority registers too and never receives its own broadcast — NetRelay filters the
            // sender out. Paired with the OnDisable below rather than living in Awake: NetAuthority
            // switches components on and off as ownership moves, and a subscription that outlived a
            // disable would put a second bullet in the air.
            this.NetOn(NetMsg.AgentActed, OnAgentActed);
        }

        private void OnDisable()
        {
            SetAiming(false);
            this.NetOff(NetMsg.AgentActed, OnAgentActed);
        }

        // Mounted NPCs are parented into a seat, which moves them under a different NetworkObject.
        // See AgentAuthority.Invalidate.
        private void OnTransformParentChanged() => authority?.Invalidate();

        private void Awake()
        {
            authority = new AgentAuthority(this);
            GameObject gun = FindChildByName("Gun");
            gun?.SetActive(IsActive);
            if (!muzzleSocket && gun != null)
                muzzleSocket = gun.transform;
            if (!animator)
                animator = GetComponentInChildren<Animator>();
            if (!weaponMount)
                weaponMount = GetComponentInChildren<WeaponMount>();
            perception = GetComponent<PerceptionModule>();
        }

        private void Start()
        {
            if (!spawnWeaponModel)
                return;

            if (weapon == null || weapon.weaponModelPrefab == null)
            {
                Debug.Log($"[RangedCombat] {name} no weapon model to spawn (weapon={weapon}, prefab={weapon?.weaponModelPrefab})");
                return;
            }

            // If no muzzle socket assigned, create a child transform at chest height as default.
            if (muzzleSocket == null)
            {
                GameObject socketGo = new GameObject("MuzzleSocket");
                socketGo.transform.SetParent(transform);
                socketGo.transform.localPosition = new Vector3(0.3f, 1.4f, 0.5f);
                socketGo.transform.localRotation = Quaternion.identity;
                muzzleSocket = socketGo.transform;
            }

            Instantiate(weapon.weaponModelPrefab, muzzleSocket.position, muzzleSocket.rotation, muzzleSocket);
        }

        public override string ModuleDescription =>
            "Fires projectiles at a hostile target using three ScriptableObject assets.\n\n" +
            "• AgentWeaponDefinition — projectile prefab, speed, damage, fire sound\n" +
            "• AgentFireProfile — min/max range, cooldown, burst count\n" +
            "• AgentAimProfile — spread angle, burst spread growth, lead factor, LoS check\n" +
            "• muzzleSocket — where projectiles spawn (assign a bone or empty child transform)\n\n" +
            "OnFire(Vector3 muzzlePos) — fires each shot\n" +
            "OnMiss(Vector3 hitPos)    — fires when no IDamageable was hit";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Advance timers every frame so cooldown keeps ticking while the agent is out of range.
            cooldownTimer -= deltaTime;
            if (burstRemaining > 0)
                burstTimer -= deltaTime;

            AgentTargeting targeting = context.Targeting;
            Transform target = targeting != null && targeting.HasTarget ? targeting.Target : null;

            if (target == null || weapon == null || fireProfile == null || aimProfile == null)
            {
                engaged = false;
                faceTarget = null;
                SetAiming(false);
                return null;
            }

            float distance = targeting.DistanceToTarget;
            float bandExit = fireProfile.maxRange * rangeExitFactor;
            if (distance > (engaged ? bandExit : fireProfile.maxRange))
            {
                engaged = false;
                faceTarget = null;
                SetAiming(false);
                return null;
            }

            if (aimProfile.requireLineOfSight && !HasLineOfSight(target))
            {
                // Losing line of sight is a reason to move, not a reason to stand there aiming at a
                // wall — drop the engagement so Chase can reposition.
                engaged = false;
                faceTarget = null;
                SetAiming(false);
                return null;
            }

            engaged = true;
            SetAiming(true);
            faceTarget = target;

            // Where to stand. This module claims movement for the whole engagement rather than
            // deferring to ChaseModule, because Chase's only goal is to close the distance — hand it
            // the frame while a target is in weapon range and the agent walks into its face while
            // shooting. Melee (higher priority) still preempts if something gets right on top of us.
            bool tooClose = distance < fireProfile.preferredRange - fireProfile.rangeTolerance;
            MoveIntent movement = tooClose
                ? BackAwayTo(context.Position, target.position, fireProfile.preferredRange)
                : HoldStation(context.Position, target.position, deltaTime);

            // Fire whenever the band allows it, including on the very first frame the target comes
            // into range — no wind-up, no closing first.
            bool canFireNow = distance <= fireProfile.maxRange
                              && distance >= fireProfile.minRange
                              && (fireProfile.allowFireWhileRunning || movement.Type != AgentIntentType.MoveToPosition);

            if (canFireNow)
            {
                if (burstRemaining > 0)
                {
                    if (burstTimer <= 0f)
                    {
                        FireOne(target);
                        burstRemaining--;
                        burstTimer = fireProfile.burstInterval;
                    }
                }
                else if (cooldownTimer <= 0f)
                {
                    // Start a new burst.
                    currentBurstSpread = 0;
                    FireOne(target);
                    burstRemaining = fireProfile.burstCount - 1;
                    burstTimer = fireProfile.burstInterval;
                    cooldownTimer = fireProfile.fireCooldown;
                }
            }

            return movement;
        }

        // Backs off to the preferred distance. Facing is left to the facing channel so the agent
        // retreats while still pointing its gun at the target rather than turning its back.
        private MoveIntent BackAwayTo(Vector3 self, Vector3 targetPos, float standoff)
        {
            Vector3 away = self - targetPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = UnityEngine.Random.insideUnitSphere;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f)
                    away = Vector3.forward;
            }

            Vector3 candidate = targetPos + away.normalized * standoff;

            // Backed into a wall with nowhere to go — hold and keep shooting rather than grinding
            // against geometry.
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return MoveIntent.StopAndFace(targetPos);

            // Running, not walking: something closed the gap, and backing off at walk speed just
            // means being followed at walk speed.
            return MoveIntent.MoveTo(hit.position, 0.4f, 1f, isRunning: true).WithFacing(targetPos);
        }

        // In the band: sidestep periodically instead of standing still. A stationary shooter is
        // trivially easy to hit and reads as a turret rather than a combatant.
        private MoveIntent HoldStation(Vector3 self, Vector3 targetPos, float deltaTime)
        {
            // Strafing is off for weapons that can't fire on the move — otherwise the agent would
            // sidestep its way through the entire engagement without ever taking a shot.
            if (!fireProfile.strafeWhileEngaged
                || !fireProfile.allowFireWhileRunning
                || fireProfile.strafeDistance <= 0.01f)
            {
                return MoveIntent.StopAndFace(targetPos);
            }

            strafeTimer -= deltaTime;
            if (strafeTimer <= 0f || !hasStrafeDestination)
            {
                strafeTimer = fireProfile.strafeInterval;

                Vector3 toTarget = targetPos - self;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f)
                    return MoveIntent.StopAndFace(targetPos);

                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                Vector3 lateral = Vector3.Cross(Vector3.up, toTarget.normalized) * (side * fireProfile.strafeDistance);
                hasStrafeDestination = NavMesh.SamplePosition(self + lateral, out NavMeshHit hit, 2f, NavMesh.AllAreas);
                if (hasStrafeDestination)
                    strafeDestination = hit.position;
            }

            if (!hasStrafeDestination)
                return MoveIntent.StopAndFace(targetPos);

            return MoveIntent.MoveTo(strafeDestination, 0.4f, 1f).WithFacing(targetPos);
        }

        // ── IFacingModule ─────────────────────────────────────────────────────────
        // Outranks every ambient look-around so the gun stays on target while the body walks.
        public int FacingPriority => Priority;

        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            if (!engaged || faceTarget == null)
            {
                facePosition = default;
                return false;
            }

            facePosition = faceTarget.position;
            return true;
        }

        /// <summary>
        /// Decide one shot: pick the weapon, lead the target, roll the spread — then hand the
        /// finished ray to <see cref="PresentShot"/> and tell everybody else about it.
        ///
        /// <para>
        /// Only ever reached on the machine that owns the agent, because AgentController stopped
        /// ticking modules anywhere else. There is deliberately no second authority check here for
        /// the same reason CloseCombatModule.Attack has none: a check repeated one level down is a
        /// second answer to the same question, free to drift from the first.
        /// </para>
        /// </summary>
        private void FireOne(Transform target)
        {
            AgentWeaponDefinition activeWeapon = ActiveWeapon;
            Transform activeMuzzle = weaponMount != null ? weaponMount.ActiveMuzzle : muzzleSocket;

            if (activeWeapon == null || activeWeapon.projectilePrefab == null)
            {
                Debug.LogWarning($"[RangedCombat] {name} fired but projectilePrefab is null on weapon asset!");
                return;
            }

            firingAt = target.GetComponentInChildren<IDamageable>();

            Transform muzzle = activeMuzzle != null ? activeMuzzle : transform;
            Vector3 spawnPos = muzzle.position + muzzle.forward * muzzleForwardOffset;
            Vector3 aimDir = ComputeAimDirection(target, spawnPos, activeWeapon);

            float totalSpread = aimProfile.baseSpreadAngle + aimProfile.spreadGrowthPerBurstShot * currentBurstSpread;
            if (totalSpread > 0f)
            {
                Quaternion spreadRot = Quaternion.Euler(
                    UnityEngine.Random.Range(-totalSpread, totalSpread),
                    UnityEngine.Random.Range(-totalSpread, totalSpread),
                    0f);
                aimDir = spreadRot * aimDir;
            }
            currentBurstSpread++;

            PresentShot(muzzle.position, spawnPos, aimDir, cosmetic: !authority.SimulatedHere);

            // Only where the shot counts. A peer drawing a copy of this burst must not also
            // startle its own copy of the wildlife -- that creature is ticked by whoever owns
            // it, and this machine is not it.
            if (authority.SimulatedHere && activeWeapon.gunshotNoiseRadius > 0f)
                Noise.Emit(NoiseType.Gunshot, spawnPos, activeWeapon.gunshotNoiseRadius,
                           transform, transform);

            // One message per shot, not one per burst — and the third reason is the deciding one.
            //
            // Each shot rolls its own spread, so a single message could not describe a burst
            // without carrying every ray in it. A burst is not atomic either: the loop in Tick
            // stalls it the instant the target leaves the band or line of sight breaks, so a peer
            // replaying a promised N shots would draw rounds that never left the barrel. And
            // replaying a schedule means a second copy of the burst clock running on every watching
            // machine, which is precisely the class of divergence this message exists to remove.
            //
            // The rate is bounded by fireCooldown and burstInterval, never by frame rate, and an
            // NPC that is not pulling a trigger puts nothing on the wire at all.
            AgentActionRelay.Broadcast(this, AgentAction.Ranged, spawnPos, aimDir);
        }

        /// <summary>
        /// Put one shot in the air, with its report and its animation. Runs on every machine.
        ///
        /// <para>
        /// The Present half of the same split <see cref="SpaceGame.Items.UsableItem"/> draws
        /// between Use and Present, and for the same reason: the authority runs it as part of
        /// firing, a watcher runs it from <see cref="NetMsg.AgentActed"/>, and neither needs to
        /// know what the other did. Everything that decides — the target, the lead, the spread, who
        /// gets billed — happens above this line and never below it.
        /// </para>
        /// <para>
        /// <paramref name="reportPosition"/> is the barrel and <paramref name="spawnPos"/> is
        /// <c>muzzleForwardOffset</c> in front of it, which is where the projectile has to start to
        /// clear the gun model. A watcher only receives the second, so it passes it for both — the
        /// sound is then a barrel's length out, which nobody can hear and which is cheaper than a
        /// second Vector3 on every shot.
        /// </para>
        /// </summary>
        private void PresentShot(Vector3 reportPosition, Vector3 spawnPos, Vector3 aimDir, bool cosmetic)
        {
            AgentWeaponDefinition activeWeapon = ActiveWeapon;
            if (activeWeapon == null || activeWeapon.projectilePrefab == null)
                return;

            GameObject projectile = Instantiate(activeWeapon.projectilePrefab, spawnPos, Quaternion.LookRotation(aimDir));

            AgentProjectile agentProjectile = projectile.GetComponent<AgentProjectile>();
            if (agentProjectile != null)
            {
                // The one rule that keeps this from re-opening the bug the authority gate closed:
                // whenever more than one machine puts a copy of the same shot in the air, exactly
                // one of them may bill the target. NetDamage applies a hit on the server and
                // forwards it as a request from a client, and the server honours every request, so
                // four peers drawing the same bullet would deal the damage four times.
                agentProjectile.Cosmetic = cosmetic;

                // No result callback on a watching machine. OnMiss and OnKillEvent are outcomes of
                // the shot that actually happened, and OnKillEvent reads `firingAt` — bookkeeping
                // only the deciding machine fills in. A cosmetic bullet answering "did it die"
                // would be a second, quieter answer to a question the replicated health already
                // answers out loud.
                Action<bool, Vector3> onResult = cosmetic ? null : new Action<bool, Vector3>(OnProjectileResult);
                agentProjectile.Init(activeWeapon.damagePerHit, onResult, gameObject);
            }

            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = aimDir * activeWeapon.projectileSpeed;

            Sfx.Play(activeWeapon.fireId, reportPosition, activeWeapon.fireSound, GetInstanceID());

            if (animator && !string.IsNullOrEmpty(shootAnimTrigger))
                animator.SetTrigger(shootAnimTrigger);

            // The muzzle flash. AgentWeaponDefinition has no VFX slot, so OnFire is where a
            // designer hangs one — that makes it presentation, and presentation runs everywhere.
            // The line between these and the events that stayed with the authority is what each one
            // is handed: OnFire gets the barrel position, which the message carries, while OnMiss
            // and OnKillEvent get the OUTCOME of a shot only the deciding machine actually fired.
            OnFire?.Invoke(reportPosition);
            OnFireEvent?.Invoke();
        }

        /// <summary>
        /// A watching machine drawing the shot the authority actually fired.
        /// </summary>
        private void OnAgentActed(in NetArg arg, ulong sender)
        {
            // Is this message even ours? An unrecognised kind is ignored rather than assumed — see
            // AgentAction — and it matters on this exact channel: an agent carrying a
            // CloseCombatModule as well broadcasts its swings here, and putting a bullet in the air
            // for a sword swing would be worse than drawing nothing.
            if (arg.A != AgentAction.Ranged) return;

            // The deciding machine already drew this while firing it, and NetRelay excludes the
            // sender from its own broadcast — so in practice only a watcher gets here. Asking
            // anyway is what keeps the handler idempotent should the message ever arrive by another
            // route, and it is the same guard EntityEquipmentController.OnItemUsedElsewhere uses.
            // A null authority means Awake has not run (an EditMode fixture), which reads as "this
            // machine decides" and therefore presents nothing.
            if (authority == null || authority.SimulatedHere) return;

            // No fallback to a locally computed aim. A watcher with no ray draws nothing, because
            // guessing from its own copy of the world is the exact behaviour this message replaced.
            if (!AgentActionRelay.TryReadRay(in arg, out Vector3 origin, out Vector3 direction))
                return;

            PresentShot(origin, origin, direction, cosmetic: true);
        }

        // Lead prediction uses the weapon actually being fired, not the serialized fallback — a
        // WeaponMount slot swap changes projectile speed, and aiming with the old number puts every
        // shot behind or ahead of a moving target.
        private Vector3 ComputeAimDirection(Transform target, Vector3 from, AgentWeaponDefinition activeWeapon)
        {
            if (!target)
                return transform.forward;

            Vector3 targetPos = target.position + Vector3.up * 1.2f;

            if (aimProfile.aimLeadFactor > 0f)
            {
                Rigidbody targetRb = target.GetComponentInParent<Rigidbody>();
                if (targetRb != null && activeWeapon.projectileSpeed > 0f)
                {
                    float dist = Vector3.Distance(from, targetPos);
                    float travelTime = dist / activeWeapon.projectileSpeed;
                    targetPos += targetRb.linearVelocity * (travelTime * aimProfile.aimLeadFactor);
                }
            }

            return (targetPos - from).normalized;
        }

        private bool HasLineOfSight(Transform target)
        {
            if (!target)
                return false;

            // Route through PerceptionModule — single source of truth for occlusion layers and self-hit rules.
            // The raycast originates at the muzzle so we're checking "can the bullet reach the target",
            // not "can the eye see the target".
            if (perception == null)
            {
                Debug.LogWarning($"[RangedCombat] {name} requires a PerceptionModule on the same GameObject for line-of-sight checks. Disable aimProfile.requireLineOfSight or add a PerceptionModule.");
                return true;
            }

            Transform muzzle = muzzleSocket != null ? muzzleSocket : transform;
            return perception.HasLineOfSightFrom(muzzle.position, target);
        }

        private void OnProjectileResult(bool hitDamageable, Vector3 hitPos)
        {
            if (!hitDamageable)
            {
                OnMiss?.Invoke(hitPos);
                return;
            }

            if (firingAt != null && !firingAt.Alive)
                OnKillEvent?.Invoke();
        }

        private void SetAiming(bool aiming)
        {
            if (animator && !string.IsNullOrEmpty(aimAnimBool))
                animator.SetBool(aimAnimBool, aiming);
        }

        private GameObject FindChildByName(string childName)
        {
            Transform result = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == childName) { result = t; break; }
            return result != null ? result.gameObject : null;
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.RangedAttack);
        }
    }
}
