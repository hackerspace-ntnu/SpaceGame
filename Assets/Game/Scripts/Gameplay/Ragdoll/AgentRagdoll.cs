using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Locomotion;
using SpaceGame.Teleporting;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Puts a creature's body in the hands of physics when it dies or is knocked down, and takes it
    /// back afterwards.
    ///
    /// <para>
    /// All the difficulty is in the taking back. Nothing here decides where a creature is — three
    /// separate layers do, each of which believes it owns the transform, and every one of them has
    /// to be told to stop before the ragdoll can have it:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>AgentController</c> ticks the brain that issues movement intents.</item>
    /// <item>The motor executes them. A <c>NavMeshAgent</c> writes the transform from its own
    /// internal position every frame it is enabled, which is why <c>ISelfDrivingMotor</c> exists —
    /// stopping the agent is not the same as switching it off.</item>
    /// <item><c>LeggedLocomotion</c> is stronger still: invariant I4 makes it the single owner of
    /// the body, holding its path position and every planted foot in WORLD space and rewriting the
    /// transform from them each LateUpdate.</item>
    /// </list>
    ///
    /// <para>
    /// That last one is why recovery raises <see cref="ITeleportAware"/>. A legged machine resumed
    /// after its body has moved does not stay where the body is: it writes back the position it
    /// integrated before the fall, and the creature walks out of the ragdoll and back to the spot
    /// where it was hit, within one frame and with nothing in the console. The interface exists for
    /// exactly this class of problem and rebases the path, the feet, the ground normals and the
    /// swing arcs in one rigid change of frame.
    /// </para>
    ///
    /// <para>
    /// Networking is the split the codebase already uses. Death is announced by
    /// <c>NetworkedHealthComponent</c> on every machine, so each one goes limp off its own
    /// <c>OnDeath</c> with no message needed. A knockdown arrives as <see cref="NetMsg.Knockdown"/>
    /// and is likewise presented everywhere. Only the SIMULATING machine drives the root — the rest
    /// take their position from the NetworkTransform and their limb poses from local physics.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class AgentRagdoll : MonoBehaviour
    {
        [Tooltip("Speed handed to the body when it dies of ordinary damage, m/s. Small on purpose " +
                 "— this is a creature folding up, not being launched. The gauntlet supplies its " +
                 "own far larger impulse through NetMsg.Knockdown.")]
        [SerializeField] private float deathImpulse = 2.5f;

        [Tooltip("Upward share of that death impulse. Enough that the body clears its own feet " +
                 "instead of collapsing straight through them.")]
        [SerializeField, Range(0f, 1f)] private float deathImpulseLift = 0.35f;

        [Tooltip("Seconds a knocked-down creature stays down, when the blast that felled it does " +
                 "not say. The beat that makes a knockdown read as a knockdown rather than a " +
                 "stumble the creature walks off.")]
        [SerializeField] private float downedSeconds = 1.2f;

        private RagdollRig rig;
        private HealthComponent health;
        private AgentController agentController;
        private LeggedLocomotion locomotion;
        private NavMeshAgent navAgent;
        private Rigidbody body;
        private Collider bodyCollider;
        private MountModule mount;
        private NpcPassenger passenger;

        private bool suspended;
        private bool bodyWasKinematic;
        private bool controllerWasEnabled;
        private bool locomotionWasEnabled;
        private bool colliderWasEnabled;
        private bool dead;

        /// <summary>Earliest this creature may stand up. See <see cref="OnKnockdown"/>.</summary>
        private float downUntil;

        /// <summary>
        /// Everything currently holding this creature down with no end time — a net, a tie, both at
        /// once. See <see cref="HoldDown"/>.
        ///
        /// <para>
        /// A set of holders rather than a flag, the same shape <see cref="CarriedBody"/> uses and
        /// for the same reason: two systems can want one body down, and the one that lets go first
        /// must not stand it up. A captor hands back the token it claimed with, so forgetting is a
        /// compile error rather than a captive that gets up on its own.
        /// </para>
        /// </summary>
        private readonly HashSet<object> holders = new HashSet<object>();

        /// <summary>Is something holding this creature down right now?</summary>
        public bool IsHeld => holders.Count > 0;

        /// <summary>
        /// The motor, asked for at the moment it is needed rather than cached in Awake.
        ///
        /// <para>
        /// <c>AgentController.Motor</c> is assigned by that component's OWN Awake, and Unity does
        /// not define which of two components on one GameObject wakes first. Cached, the answer was
        /// null whenever this component happened to win that race — and a null motor is not a crash.
        /// It is a NavMeshAgent that never gets suspended, left writing the transform from its own
        /// internal position for the whole time the ragdoll is also writing it. Two owners of one
        /// transform is a body that glitches rather than falls, and whether it happened would have
        /// come down to the order the components sit in on the prefab.
        /// </para>
        /// </summary>
        private ISelfDrivingMotor SelfDrivingMotor =>
            agentController != null ? agentController.Motor as ISelfDrivingMotor : null;

        private void Awake()
        {
            rig = GetComponent<RagdollRig>();
            health = GetComponent<HealthComponent>();
            agentController = GetComponent<AgentController>();
            locomotion = GetComponentInChildren<LeggedLocomotion>(true);
            navAgent = GetComponentInChildren<NavMeshAgent>(true);
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();
            mount = GetComponentInChildren<MountModule>(true);
            passenger = GetComponentInChildren<NpcPassenger>(true);

            // No warning for a missing HealthComponent: plenty of these creatures genuinely have
            // none. The Ostrich is a rideable mount that cannot die, and the crab and the humanoid
            // are walking rigs with no health either — all three can still be knocked flat by a
            // blast, which is the half of this that needs no health at all.
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath += OnDeath;
            if (health != null) health.OnRevive += OnRevive;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath -= OnDeath;
            if (health != null) health.OnRevive -= OnRevive;
        }

        // ── What starts it ────────────────────────────────────────────────────

        private void OnDeath()
        {
            dead = true;

            // Death drops every hold's claim — see PlayerRagdoll.OnDeath for why this empties the
            // set directly rather than calling ReleaseHold, which gives up one claim and would
            // leave the others standing. The corpse stays limp; it simply stops being a captive
            // RagdollBudget is forbidden to reclaim.
            holders.Clear();
            rig.BudgetExempt = false;

            // A save being loaded, not a kill — the same rule HealthReactionModule.HandleDeath
            // follows and for the same reason. The corpse's resting POSITION is already in the save
            // (the rig follows the hips into the transform, so the transform is where the body
            // lies). What must not happen is throwing it again: a body relaunched on every load
            // walks its way across the desert one reload at a time.
            bool restoring = health != null && health.IsRestoring;

            // Read before Suspend, which switches the motor off underneath it.
            Vector3 carried = restoring ? Vector3.zero : CarriedVelocity;

            Suspend();
            rig.GoLimp(restoring ? Vector3.zero : DeathImpulse() + carried,
                       settled: restoring, drives: Drives);
        }

        /// <summary>
        /// How fast this creature was already travelling.
        ///
        /// Read off the MOTOR, not off a rigidbody: an agent's body is kinematic and its transform
        /// is moved by assignment, so there is no rigidbody velocity to read and asking for one
        /// returns a confident zero. Carrying it into the ragdoll is what stops a creature knocked
        /// down mid-sprint from reading as one that was simply switched off.
        /// </summary>
        private Vector3 CarriedVelocity =>
            agentController != null && agentController.Motor != null
                ? agentController.Motor.Velocity
                : Vector3.zero;

        /// <summary>
        /// Away from whatever killed it, and up. Reading the damage source rather than picking a
        /// direction is what makes a creature shot from the front fall backwards.
        /// </summary>
        private Vector3 DeathImpulse()
        {
            Transform source = health != null ? health.LastDamageSource : null;

            Vector3 away = source != null
                ? Vector3.ProjectOnPlane(transform.position - source.position, Vector3.up)
                : -transform.forward;

            if (away.sqrMagnitude < 1e-4f) away = -transform.forward;

            return (away.normalized * (1f - deathImpulseLift) + Vector3.up * deathImpulseLift)
                   * deathImpulse;
        }

        private void OnRevive()
        {
            dead = false;

            // See PlayerRagdoll.OnRevive: unreachable today, permanent and silent if it ever is.
            holders.Clear();
            rig.BudgetExempt = false;

            if (rig.IsLimp) Restore();
        }

        /// <summary>
        /// A blast. Every machine presents it: the impulse rides in <c>P</c>, and how long the
        /// creature stays down in <c>A</c>, as milliseconds.
        ///
        /// <para>
        /// The duration travels with the message rather than being decided locally because it is
        /// the only part of the recovery every machine can agree on. Settling cannot be: a watcher
        /// does not simulate the flight — it takes the body's position off the wire — so its
        /// ragdoll comes to rest on a different schedule from the one that does. Sharing the floor
        /// and letting each machine wait out its own body on top of it keeps them within a frame or
        /// two of each other without a second round trip to say "get up now".
        /// </para>
        /// </summary>
        private void OnKnockdown(in NetArg arg, ulong sender)
        {
            if (dead) return;                 // a corpse is already down and is not getting up
            if (HasRider) return;             // see CanBeKnockedDown

            Vector3 carried = CarriedVelocity;

            Suspend();
            rig.GoLimp(arg.P + carried, settled: false, drives: Drives);

            float down = arg.A > 0 ? arg.A / 1000f : downedSeconds;
            downUntil = Time.time + down;
        }

        /// <summary>
        /// Does this machine decide where this creature ends up? A creature is server-authoritative,
        /// so the answer is the server's (and, offline, everyone's). See <see cref="RagdollRig.Drives"/>.
        /// </summary>
        private bool Drives => Network.Simulates(this);

        /// <summary>
        /// Is there someone on this creature's back?
        ///
        /// A rider is PARENTED to the seat, so a mount that goes limp underneath one drags them
        /// through the ground with it — and on a player that is a body the server does not own and
        /// cannot put back. Mounts keep the leap the gauntlet already gives them instead. Both
        /// riding systems have to be asked: <c>NpcPassenger</c> deliberately does not go through
        /// <c>MountModule</c>, because MountModule's rider contract is PlayerMovement.
        /// </summary>
        private bool HasRider => (mount != null && mount.IsMounted)
                                 || (passenger != null && passenger.HasRider);

        /// <summary>Can the shock wave knock this creature down, or must it be leapt instead?</summary>
        public bool CanBeKnockedDown => isActiveAndEnabled && !HasRider;

        /// <summary>
        /// Go limp and stay limp. The creature counterpart of <c>PlayerRagdoll.HoldDown</c>, and
        /// like it, released by the caller rather than by a timer.
        ///
        /// <para>
        /// Refuses a creature that is carrying somebody, and that refusal has to be visible to the
        /// caller rather than silent: <see cref="CanBeKnockedDown"/> is false while a rider is
        /// aboard for the reason <see cref="HasRider"/> gives, so a net on a ridden mount would
        /// otherwise be a no-op with a clean console. The captor is expected to fall back to
        /// whatever restraint it has instead — <c>SnareTether.Bind</c> caps a NavMeshAgent's speed
        /// when, and only when, this answers false, and has nothing to fall back on for a creature
        /// with no NavMeshAgent, a legged rig among them. This only says whether the body itself
        /// went down.
        /// </para>
        /// <para>
        /// Called by <c>SnareTether.Bind</c>, on every machine — see <c>PlayerRagdoll.HoldDown</c>
        /// for why a capture reaches all of them.
        /// </para>
        /// </summary>
        /// <returns>
        /// True once the creature is actually limp and held. FALSE means the hold did not take and
        /// the caller must not treat this body as held — it is dead, it is carrying somebody, or the
        /// rig declined to go limp at all. See <c>PlayerRagdoll.HoldDown</c> for what that last one
        /// costs a body that is left believing it was held.
        /// </returns>
        public bool HoldDown(object holder)
        {
            if (holder == null) return false;

            // A corpse is already down and is not getting up — the same refusal OnKnockdown opens
            // with. It matters more here than there: a hold that took a corpse would set
            // BudgetExempt on a body nothing will ever release, which is the leak OnDeath exists to
            // close arriving through a second door.
            if (dead) return false;

            // Somebody else already has it down. Take a claim and say so, rather than repeating
            // work that would record the suspended state as this creature's normal one.
            if (IsHeld)
            {
                holders.Add(holder);
                return true;
            }

            if (!CanBeKnockedDown) return false;

            holders.Add(holder);

            // Off the motor and read before Suspend, which switches that motor off underneath it
            // — the ordering OnKnockdown uses and for the same reason.
            Vector3 carried = CarriedVelocity;

            Suspend();
            rig.BudgetExempt = true;
            rig.GoLimp(carried, settled: false, drives: Drives);

            // Asked of the rig rather than pre-checked, and everything undone on a refusal — see
            // PlayerRagdoll.HoldDown, which states the case in full.
            if (rig.IsLimp) return true;

            holders.Remove(holder);
            rig.BudgetExempt = false;
            Restore();
            return false;
        }

        /// <summary>
        /// Give up one claim. The creature gets up only once the LAST one is given up — the rule
        /// <see cref="CarriedBody.Release"/> follows, so a net rotting off a hogtied animal does
        /// not untie it.
        ///
        /// Safe to call with a token that was never claimed, or after death has cleared the set.
        /// </summary>
        public void ReleaseHold(object holder)
        {
            if (holder == null || !holders.Remove(holder)) return;
            if (IsHeld) return;

            rig.BudgetExempt = false;
            downUntil = 0f;
        }

        // ── Getting back up ───────────────────────────────────────────────────

        private void Update()
        {
            if (dead || !suspended) return;

            // A hold has no timer and no settle condition to wait for, so every reason to stand up
            // below is the wrong one — including the budget rescue underneath, which is the path
            // BudgetExempt exists to keep a captive off.
            if (IsHeld) return;

            // The budget froze this body out from under us (RagdollBudget evicts the oldest limp
            // rig past the cap). Nothing is going to come to rest and nothing is going to tell us
            // so — take the creature back now, or it stays suspended for good with its brain
            // switched off, which is a knockdown that never ends.
            if (!rig.IsLimp)
            {
                Restore();
                return;
            }

            // The shared floor first, then this machine's own body. A creature still in the air
            // when the floor expires keeps tumbling; one that landed early lies there for the rest
            // of the beat, which is what stops a glancing blast reading as a stumble
            // (GDC-L1-FEEL-0007 — what is being tuned is the sensation, and a body that pops up the
            // instant it stops moving has none).
            if (Time.time < downUntil || !rig.IsSettled) return;

            Restore();
        }

        // ── Handing the body over and back ────────────────────────────────────

        /// <summary>
        /// Stop every layer that writes this creature's transform or its bones.
        ///
        /// Idempotent: a creature blasted twice while already down must not record the suspended
        /// state a second time, or resuming restores the values captured mid-ragdoll.
        /// </summary>
        private void Suspend()
        {
            if (suspended) return;
            suspended = true;

            // Recorded rather than assumed, for the reason NavMeshAgentMotor.SuspendSelfDrive
            // spells out: "enabled" is not every layer's resting state. On a machine that is only
            // watching this creature, NetAuthority has already switched its brain and its
            // locomotion off — and a resume that turned them back on would leave a client running
            // an AI it has no business running, arguing with the server over every step.
            controllerWasEnabled = agentController != null && agentController.enabled;
            locomotionWasEnabled = locomotion != null && locomotion.enabled;
            colliderWasEnabled = bodyCollider != null && bodyCollider.enabled;

            if (agentController != null) agentController.enabled = false;
            SelfDrivingMotor?.SuspendSelfDrive();

            // Switched off, not set ExternallyPosed. That flag means "someone else writes the root
            // but I keep solving the legs", which is right for a replicated copy and exactly wrong
            // here — the ragdoll owns the bones, and a locomotion still solving IK would fight it
            // for every leg joint.
            if (locomotion != null) locomotion.enabled = false;

            if (bodyCollider != null) bodyCollider.enabled = false;

            if (body != null)
            {
                bodyWasKinematic = body.isKinematic;
                body.isKinematic = true;
            }
        }

        /// <summary>
        /// Give the body back to the layers that drive it, at the place it actually came to rest.
        /// </summary>
        private void Restore()
        {
            if (!suspended) return;
            suspended = false;

            TeleportMove move = rig.Recover();

            // Only ever switched back ON, never off — restoring the recorded flag verbatim would
            // make this a race with HealthReactionModule, which disables the AgentController on
            // death and re-enables it on revive off the same two events. Whichever of the two
            // subscribed first would win, and if that were this component a revived creature would
            // come back with its brain switched off and stand there. Re-enabling only what this
            // component itself switched off has no such ordering to get wrong.
            if (bodyCollider != null && colliderWasEnabled) bodyCollider.enabled = true;
            if (body != null) body.isKinematic = bodyWasKinematic;

            if (locomotion != null && locomotionWasEnabled) locomotion.enabled = true;
            SelfDrivingMotor?.ResumeSelfDrive();
            if (agentController != null && controllerWasEnabled) agentController.enabled = true;

            // Last, and only now that everything is back on: the point of the rebase is to move
            // world-space state that these layers hold OUTSIDE their transforms — a legged
            // machine's path position and planted feet, a look rig's pitch. Raising it before they
            // were enabled would rebase components that then reset themselves on enable.
            if (move.Distance > 1e-3f) RaiseTeleported(move);

            // After the resume, because Warp needs an enabled agent — ResumeSelfDrive is what
            // switches it back on. Re-enabling a NavMeshAgent already re-samples it onto the
            // transform, so this is the belt to that braces: an agent whose resting position sits
            // off the mesh (a corpse that slid off a walkable ledge) is placed on the nearest
            // polygon rather than left frozen off it.
            if (navAgent != null && navAgent.enabled)
                navAgent.Warp(transform.position);
        }

        /// <summary>
        /// Tell everything under this creature that its body has moved.
        ///
        /// Hand-raised rather than routed through <c>SaveTeleport</c>, which is the project's single
        /// instant-move: the body is ALREADY at the destination — physics carried it there over the
        /// last two seconds — and asking SaveTeleport to move it would re-place a transform that is
        /// already right, zero the velocities the recovery depends on, and re-enter the netcode's
        /// teleport path for a move no machine needs told about. What is wanted is the notification
        /// half on its own.
        /// </summary>
        private void RaiseTeleported(in TeleportMove move)
        {
            foreach (ITeleportAware aware in GetComponentsInChildren<ITeleportAware>(true))
                aware.OnTeleported(move);
        }
    }
}
