// Stand still, charge lightning in a cupped hand for three seconds, then drop it on
// the target.
//
// ---- the one decision that matters -------------------------------------------
//
// Whether the bolt lands where the target IS when it resolves, or where it WAS when the
// cast began. That single choice is most of how the creature feels to fight, so it is a
// field (CastAim) rather than something buried in the code:
//
//   TracksTarget      the body turns to follow you for three seconds and the bolt lands
//                     on you. The wind-up is a WARNING -- move out of the blast radius
//                     or eat it, but you cannot outrun the aim.
//   WhereItCommitted  the aim freezes when the cast starts. The wind-up is a DODGE --
//                     the bolt lands where you were standing and you simply leave.
//
// TracksTarget is the default. WhereItCommitted is the more interesting fight and the
// more forgiving one; if the attack ever reads as unavoidable, that is the switch.
//
// Either way the aim and the ANIMATION agree: whatever CurrentAimPoint returns is both
// what the bolt hits and what TryGetFacing turns the body toward, so the pointing hand
// is always aimed at the spot that is about to be struck.
//
// ---- shape -------------------------------------------------------------------
//
//   idle        target inside CastRange and off cooldown  -> begin
//   casting     holds position for CastSeconds, body tracking the target
//   commit      strike, then CooldownSeconds before it can begin again
//
// Claims movement only while casting. Out of range it returns null and passes, so
// ChaseModule (priority 20, below this one's 22) closes the gap on its own. That is the
// same division AgentRangedCombatModule uses and it is why this module does no walking.
//
// ---- what runs where ---------------------------------------------------------
//
// AgentController only ticks modules on the machine that SIMULATES the agent, and
// NetAuthority switches that controller off everywhere else. So everything below --
// deciding to cast, timing it, aiming it, applying the damage -- happens on exactly one
// machine, which is what stops a bolt billing each victim once per player watching.
//
// The consequence is that a watching machine never runs a line of it, so it would show
// nothing: no wind-up animation, no charge in the hand, no bolt. Motion is the exception
// and arrives for free -- the NetworkTransform carries the body and AgentAnimatorDriver
// measures that transform on any frame nobody drove it, so the walk cycle is already
// correct on every peer. Discrete effects are not free and have to be told.
//
// Hence the split every method below is arranged around:
//
//   DECIDE     server only. Begin() and Commit(): timing, aim, damage, and a broadcast.
//   PRESENT    every machine. PresentCast() and PresentStrike(): the trigger, the charge
//              effect, the bolt. Reached locally on the server and from a message
//              everywhere else, so both paths run identical code.
//
// Damage is deliberately NOT in the present half. It is shared world state and exactly
// one machine may decide it.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    /// Where a charged bolt lands. See ConjurerCastModule's header.
    public enum CastAim
    {
        /// Follows the target for the whole wind-up and lands on it. Move out of the
        /// blast radius or take it; you cannot outrun the aim.
        TracksTarget = 0,

        /// Frozen when the cast begins. Lands where the target was standing three
        /// seconds ago, so walking away is the counterplay.
        WhereItCommitted = 1,
    }

    public class ConjurerCastModule : BehaviourModuleBase, IFacingModule
    {
        [Header("Engagement")]
        [Tooltip("Maximum distance at which a cast will START. Beyond this the module passes " +
                 "and ChaseModule closes the gap.")]
        [SerializeField] private float castRange = 25f;

        [Tooltip("Line of sight required to BEGIN a cast. Once begun the cast always " +
                 "finishes -- breaking line of sight mid-wind-up does not cancel it.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Whether the bolt lands where the target is when it resolves, or where " +
                 "the target stood when the cast began. See the file header - this is the " +
                 "difference between a wind-up you must move out of and one you can " +
                 "simply walk away from.")]
        [SerializeField] private CastAim aim = CastAim.TracksTarget;

        [Header("Timing")]
        [Tooltip("Wind-up before the bolt lands. Must match the Attack clip's length or the " +
                 "strike fires against the wrong frame of the animation. The clip authored by " +
                 "_Source~/anim.py is 90 frames at 30 fps.")]
        [SerializeField] private float castSeconds = 3f;

        [Tooltip("From the START of a cast, not the end. At the defaults that is 3 s casting " +
                 "then 2 s recovering, so a bolt every 5 s.")]
        [SerializeField] private float cooldownSeconds = 5f;

        [Header("Animation")]
        [Tooltip("Trigger fired on the Animator when a cast begins. Leave empty to disable.")]
        [SerializeField] private string castAnimTrigger = "Cast";
        [SerializeField] private Animator animator;

        [Header("Strike")]
        [SerializeField] private GameObject lightningVFXPrefab;

        [Tooltip("How far above the ground point the bolt is drawn from, so its graph has sky " +
                 "to fall through. Damage is always billed at the ground point.")]
        [SerializeField] private float drawHeight = 10f;

        [Tooltip("Seconds before the spawned bolt is destroyed. The lightning prefab is a " +
                 "bare VisualEffect that never cleans itself up, and this creature casts " +
                 "every few seconds forever - without this the scene fills with spent bolts. " +
                 "Must outlast the graph or the bolt is cut off mid-strike.")]
        [SerializeField] private float vfxLifetime = 5f;

        [SerializeField] private int damage = 10;
        [SerializeField] private float damageRadius = 3.5f;
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Charge effect")]
        [Tooltip("Spawned in the cupped hand when the cast begins and destroyed when it " +
                 "resolves. Optional.")]
        [SerializeField] private GameObject chargeVFXPrefab;

        [Tooltip("Bone the charge effect parents to. hands.py puts CastSocket.R in the middle " +
                 "of the right palm, so it rides the cup for free.")]
        [SerializeField] private string chargeSocketBone = "CastSocket.R";

        private AgentTargeting targeting;
        private Transform chargeSocket;
        private GameObject liveCharge;

        private bool casting;
        private float castElapsed;
        private float cooldownRemaining;

        /// Where the target stood when the cast committed.
        ///
        /// Under WhereItCommitted this IS the aim. Under TracksTarget it is the fallback,
        /// and it has to exist: a target that dies, despawns or is streamed out during the
        /// three-second wind-up leaves a null Transform, and a cast with nowhere to land is
        /// a NullReferenceException on the frame it resolves. Freezing it up front means
        /// the bolt always has somewhere to go.
        private Vector3 committedPoint;

        /// Where the bolt is aimed RIGHT NOW -- what it will hit and what the body turns
        /// to face. Those must be the same point or the pointing hand lies about where the
        /// strike is going.
        private Vector3 CurrentAimPoint()
        {
            if (aim == CastAim.WhereItCommitted) return committedPoint;

            Transform target = targeting != null ? targeting.Target : null;
            return target != null ? target.position : committedPoint;
        }

        public override string ModuleDescription =>
            "Charges for castSeconds, then drops a lightning strike on the target - either " +
            "tracking it through the wind-up or striking where it stood when the cast " +
            "began, per Aim. Holds station while casting; passes otherwise so Chase can " +
            "close.";

        // Facing outranks Chase's so the conjurer keeps the pointing hand on its target
        // through the whole wind-up rather than turning to look where it last walked.
        public int FacingPriority => ModulePriority.RangedAttack;

        private void Reset()
        {
            SetPriorityDefault(ModulePriority.RangedAttack);
        }

        private void Awake()
        {
            targeting = GetComponent<AgentTargeting>();
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            ResolveSocket();
        }

        // Registered on every machine, including the one that simulates -- a broadcast sent
        // from inside a handler re-enters Dispatch inline on the host, so the server also
        // receives its own message. Both present methods are idempotent for that reason.
        private void OnEnable()
        {
            this.NetOn(NetMsg.ConjurerCast, OnCastElsewhere);
            this.NetOn(NetMsg.ConjurerStruck, OnStruckElsewhere);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.ConjurerCast, OnCastElsewhere);
            this.NetOff(NetMsg.ConjurerStruck, OnStruckElsewhere);

            casting = false;
            ClearCharge();
        }

        private void ResolveSocket()
        {
            if (string.IsNullOrEmpty(chargeSocketBone) || animator == null) return;

            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != chargeSocketBone) continue;
                chargeSocket = t;
                return;
            }

            // Not fatal -- the cast still works, it just charges nothing visible. Worth a
            // line though: a renamed bone is otherwise silent.
            Debug.LogWarning(
                $"{name}: ConjurerCastModule found no bone '{chargeSocketBone}' under the " +
                "Animator; the charge effect will spawn on the agent root instead.", this);
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Belt and braces. NetAuthority already disables AgentController on a watching
            // machine so this should never be reached there, but a module that decides to
            // cast on a peer would fire a second, unsynchronised bolt -- cheap to rule out.
            if (!Network.Simulates(this)) return null;

            if (cooldownRemaining > 0f) cooldownRemaining -= deltaTime;

            if (casting)
                return TickCast(deltaTime);

            if (!CanBegin(in context)) return null;

            Begin(in context);
            return MoveIntent.Idle();
        }

        private bool CanBegin(in AgentContext context)
        {
            if (cooldownRemaining > 0f) return false;
            if (targeting == null || !targeting.HasTarget) return false;
            if (requireLineOfSight && !targeting.CanSeeTarget) return false;

            return targeting.DistanceToTarget <= castRange;
        }

        private void Begin(in AgentContext context)
        {
            casting = true;
            castElapsed = 0f;
            cooldownRemaining = cooldownSeconds;

            // Target.position rather than LastKnownPosition: CanBegin has just confirmed
            // this thing is visible right now, and LastKnownPosition can lag it by a frame.
            // LastKnownPosition is the fallback for the case where it is not.
            committedPoint = targeting.CanSeeTarget && targeting.Target != null
                ? targeting.Target.position
                : targeting.LastKnownPosition;

            PresentCast();

            // Everyone else starts their wind-up now, on the same frame the server did.
            // Sent rather than timed locally because a peer cannot know when the server
            // decided, and a peer that joins mid-cast must not start one of its own.
            this.NetToAll(NetMsg.ConjurerCast, new NetArg { A = 1 }.With(gameObject));
        }

        /// The wind-up, on every machine. Idempotent: the host reaches it once directly and
        /// once more when NetToAll hands its own broadcast back.
        private void PresentCast()
        {
            if (animator && !string.IsNullOrEmpty(castAnimTrigger))
                animator.SetTrigger(castAnimTrigger);

            if (chargeVFXPrefab == null || liveCharge != null) return;

            Transform at = chargeSocket != null ? chargeSocket : transform;
            liveCharge = Instantiate(chargeVFXPrefab, at.position, at.rotation, at);
        }

        /// The bolt, on every machine.
        ///
        /// The prefab is deliberately NOT a network prefab. It is pure cosmetic, so every
        /// machine draws its own from this one message -- spawning it through the server
        /// would cost a replicated object per cast to show something nobody interacts with.
        private void PresentStrike(Vector3 strike)
        {
            ClearCharge();

            LightningStrike.Present(lightningVFXPrefab, strike + Vector3.up * drawHeight,
                                    strike, vfxLifetime);
        }

        private void ClearCharge()
        {
            if (liveCharge == null) return;
            Destroy(liveCharge);
            liveCharge = null;
        }

        private void OnCastElsewhere(in NetArg arg, ulong sender) => PresentCast();

        private void OnStruckElsewhere(in NetArg arg, ulong sender) => PresentStrike(arg.P);

        private MoveIntent? TickCast(float deltaTime)
        {
            castElapsed += deltaTime;
            if (castElapsed < castSeconds)
                return MoveIntent.Idle();

            Commit();
            return null;
        }

        private void Commit()
        {
            casting = false;

            // Resolved once, here, and used for all three: the picture every machine draws
            // and the damage this one applies have to agree about where the bolt landed.
            // Re-reading the target between them would draw it in one place and hurt people
            // in another, and shipping the point on the wire is what keeps the peers honest.
            Vector3 strike = CurrentAimPoint();

            PresentStrike(strike);

            this.NetToAll(NetMsg.ConjurerStruck,
                          new NetArg { P = strike }.With(gameObject));

            // Server only, by virtue of Tick's guard. Damage is shared world state and
            // exactly one machine may decide it -- applying it beside the visual on every
            // peer would kill a player once per player watching.
            LightningStrike.Damage(strike, damage, damageRadius, damageMask,
                                   gameObject, damagesAttacker: false);
        }

        /// Face the spot about to be struck.
        ///
        /// The SAME point the bolt will land on, deliberately, whichever aim mode is set.
        /// Under TracksTarget the body follows the target through the wind-up and the arm
        /// stays on it; under WhereItCommitted the body holds the spot it committed to and
        /// lets the target run out of it. Both read correctly because in both the pointing
        /// hand is aimed at where the lightning is actually going.
        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            if (casting)
            {
                facePosition = CurrentAimPoint();
                return true;
            }

            facePosition = default;
            return false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            castRange = Mathf.Max(0f, castRange);
            castSeconds = Mathf.Max(0f, castSeconds);
            cooldownSeconds = Mathf.Max(castSeconds, cooldownSeconds);
            damage = Mathf.Max(0, damage);
            damageRadius = Mathf.Max(0f, damageRadius);
            drawHeight = Mathf.Max(0f, drawHeight);
            vfxLifetime = Mathf.Max(0f, vfxLifetime);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, castRange);

            if (!casting) return;
            Vector3 strike = CurrentAimPoint();
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(strike, damageRadius);
            Gizmos.DrawLine(transform.position, strike);
        }
    }
}
