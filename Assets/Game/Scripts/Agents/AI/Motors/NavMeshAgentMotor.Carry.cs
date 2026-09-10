// Being carried: the third thing that can be done to a creature.
//
// BlastPush states the constraint this file answers. "A creature's transform belongs to its motor
// and forces never land on it, so there are only two things that can be done to one: take the body
// away from the motor and let it fall, or throw it as a leap." A rope wanted a third -- hold the
// animal up and move it -- and had no way to ask for it. NavMeshAgent.Move re-projects onto the
// mesh, so the whole vertical half of a pull was discarded every step, silently: a jetpack pilot
// paid full thrust and heat for the weight of a leashed rat (LeashLoad counts a kinematic body's
// mass just the same) and the rat never left the sand.
//
// So a carried creature is a leap that nobody has decided the end of. The mechanism is the leap's,
// exactly -- navigation switched off, the transform driven by hand, agent.Warp on landing -- and
// the only difference is who supplies the motion: an arc there, a rope and gravity here.
//
// The creature stays upright, animated and alive throughout. It is not knocked down, which is
// AgentRagdoll's business and carries a capture's meaning; a hoisted animal lands on its feet and
// resumes whatever it was doing (GDC-L1-SYS-0005 -- a carry that also disabled the creature would
// be doing the net's job as well as its own).
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public partial class NavMeshAgentMotor
    {
        [Header("Rope Carry")]
        [Tooltip("How steep a rope's pull must be before it lifts this body off the NavMesh rather " +
                 "than dragging it along one. 1 is straight up. Well above the slope a flat drag " +
                 "produces -- see AgentCarry.IsLift, which explains where that floor comes from.")]
        [SerializeField, Range(0f, 1f)] private float carryEnterSlope = 0.7f;

        [Tooltip("How close to the NavMesh underneath a falling body must come to count as landed. " +
                 "Widened automatically by the distance the body covers in one physics step, so " +
                 "this is the figure for a body that is barely moving.")]
        [SerializeField, Min(0f)] private float carryLandTolerance = 0.15f;

        [Tooltip("Terminal speed of a dropped creature, m/s. Bounds how far one physics step can " +
                 "carry it past the ground it is landing on.")]
        [SerializeField, Min(1f)] private float maxCarryFallSpeed = 30f;

        private bool carried;
        private Vector3 carryVelocity;
        private Vector3 lastCarryPos;

        /// <summary>
        /// Was the rope still asking for a LIFT on the last step? A body something is actively
        /// holding up has not landed, whatever is underneath it.
        ///
        /// <para>
        /// Without this the state churns. A pilot hovering directly above at exactly the rope's
        /// length asks for a steep pull while the creature is still standing on the sand: it lifts
        /// a millimetre, is descending again by the next step, finds the mesh right there and
        /// lands — then re-enters on the next ask, fifty times a second, and each round trip runs
        /// a `ResetPath` and an `Agent.Warp`. Warping an agent at physics rate is the exact
        /// failure the leash's own "never Warp" rule exists to prevent.
        /// </para>
        /// <para>
        /// Read one step later than it is written, which is what makes it a plain bool rather than
        /// a frame stamp: the motor's FixedUpdate runs at −100 and the rope's at 0, so the flag a
        /// step sets is always read by the step after it.
        /// </para>
        /// </summary>
        private bool liftedLastStep;

        /// <summary>
        /// Is a rope holding this creature off its own NavMesh right now?
        ///
        /// <para>
        /// Read by <c>AgentGroundConform</c> for the same reason it reads <see cref="IsLeaping"/>:
        /// the ground under a body in the air is not the ground it is standing on, and conforming
        /// to it would pull the body straight back down out of the carry.
        /// </para>
        /// </summary>
        public bool IsCarried => carried;

        // ─────────── ITowable ───────────
        //
        // Implemented on the motor rather than on a component of its own, so every NavMesh-driven
        // thing in the game -- creatures, NPCs, the patrol robots, a mount -- gains this at once
        // with no prefab wiring to forget. LeggedDriver had to be a separate seam only because
        // ITowable lives in the default assembly and the locomotion behind it does not; nothing
        // here has that problem.

        /// <summary>The body itself: where a rope tied to a walking creature pulls from.</summary>
        public Vector3 TowAttachPoint => transform.position;

        /// <summary>
        /// A rope wants this creature at <paramref name="anchor"/> this step.
        ///
        /// <para>
        /// Deliberately uncapped, unlike <c>LeggedDriver.RequestTow</c>. That cap exists so a rope
        /// cannot drag an animal faster than it could walk, which is the right rule for something
        /// on its feet and the wrong one for something hanging in the air: a carried body has no
        /// feet on anything, and its own walking speed has nothing to say about how fast a jetpack
        /// can lift it. The bound that does apply is the rope's, and it is real physics --
        /// <c>Leash.TowCap</c> is the winner's spare pull over this body's mass, and every caller
        /// of this method hands over one step's worth of ask rather than a destination.
        /// </para>
        /// </summary>
        public bool RequestTow(Vector3 anchor)
        {
            NavMeshAgent nav = Agent;
            if (nav == null) return false;

            // Somebody else's copy. Its pose arrives over the wire, and a machine that towed a
            // creature it only watches would be a second author on that transform. The same
            // refusal LeggedDriver makes on ExternallyPosed.
            if (selfDriveSuspended) return false;

            // A leap already owns the transform and puts the agent's own flags back when it lands.
            // Answered true rather than false: a refusal is permanent for that rope, and a rope
            // that let go because the animal happened to be mid-hop would never take hold again.
            if (isLeaping) return true;

            Vector3 ask = anchor - TowAttachPoint;
            if (ask.sqrMagnitude < 1e-8f) return true;

            bool lift = AgentCarry.IsLift(ask, carryEnterSlope);

            if (!carried && lift) BeginCarry();

            if (carried)
            {
                liftedLastStep |= lift;
                transform.position += ask;
                return true;
            }

            // Still on its feet. Through the agent's OWN API -- MovePosition fights the agent's
            // position writes and loses, and Warp resets navigation fifty times a second.
            if (nav.isActiveAndEnabled && nav.isOnNavMesh) nav.Move(ask);

            return true;
        }

        // ─────────── The carry itself ───────────

        /// <summary>
        /// Take the body off the NavMesh. The entry half of <c>RequestLeap</c>, minus the arc.
        /// </summary>
        private void BeginCarry()
        {
            NavMeshAgent nav = Agent;
            if (nav == null) return;

            carried = true;

            if (nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.ResetPath();
                nav.isStopped = true;
            }

            nav.updatePosition = false;
            nav.updateRotation = false;

            // Back-dated by one step rather than assigned, because the fall integrator below reads
            // this body's velocity out of how far it moved rather than out of a field. Seeding the
            // previous position a step behind the agent's own velocity is what hands a running
            // creature its momentum as it is snatched off the ground -- assigning carryVelocity
            // here would be overwritten by the first measurement before it was ever used.
            carryVelocity = Vector3.zero;
            lastCarryPos = transform.position - nav.velocity * Time.fixedDeltaTime;
        }

        /// <summary>
        /// Put the creature back on the mesh at <paramref name="landing"/> and hand navigation back.
        /// The exit half of <c>UpdateMountedLeap</c>.
        /// </summary>
        private void EndCarry(Vector3 landing)
        {
            carried = false;
            carryVelocity = Vector3.zero;
            liftedLastStep = false;

            NavMeshAgent nav = Agent;
            if (nav == null) return;

            transform.position = landing;
            nav.updatePosition = defaultUpdatePosition;
            nav.updateRotation = defaultUpdateRotation;
            nav.Warp(landing);
            nav.isStopped = false;
        }

        /// <summary>
        /// Abandon a carry without landing it: the component is going away, or this machine has
        /// stopped being the one that drives this body. The agent's flags have to go back either
        /// way, or the creature comes back with <c>updatePosition</c> still off and stands rooted
        /// wherever it was for the rest of the session.
        /// </summary>
        private void AbandonCarry()
        {
            if (!carried) return;

            carried = false;
            carryVelocity = Vector3.zero;
            liftedLastStep = false;

            if (agent == null) return;

            agent.updatePosition = defaultUpdatePosition;
            agent.updateRotation = defaultUpdateRotation;
        }

        // In FixedUpdate rather than in Tick, where the leap arc runs, because this is a fall and a
        // fall belongs on the physics clock -- and because Tick is only called while the brain is
        // deciding, so a dropped creature would hang in the air the moment anything stopped
        // ticking it. The motor's [DefaultExecutionOrder(-100)] is what puts this step BEFORE the
        // rope's own FixedUpdate, which is the same ordering PlayerMovement and LeashedBody have:
        // the body moves under its own weight first, the rope corrects the result second.
        private void FixedUpdate()
        {
            if (!carried) return;

            if (agent == null || selfDriveSuspended)
            {
                AbandonCarry();
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            // Consumed before the fall, and re-set by whichever ropes ask later in this same step.
            bool held = liftedLastStep;
            liftedLastStep = false;

            // Measured, never accumulated. Whatever the rope did to this transform since the last
            // step IS this body's velocity, so the pull needs no impulse channel of its own and
            // cannot be counted into the fall twice.
            //
            // It is also where the hang gets its damping. A kinematic end reports no velocity to
            // the rope (LeashEnd.Velocity reads the Rigidbody, which is not the thing moving here),
            // so the constraint contributes no arrest term on this side and repays everything as
            // position. Measuring the fall out of the position instead means each step's upward
            // correction is subtracted from the next step's velocity for free, and a hanging
            // creature settles a couple of centimetres below the rope rather than bouncing on it.
            carryVelocity = AgentCarry.Fall((transform.position - lastCarryPos) / deltaTime,
                                            Physics.gravity, deltaTime, maxCarryFallSpeed);

            transform.position += carryVelocity * deltaTime;
            lastCarryPos = transform.position;

            if (!held) TryLand(deltaTime);
        }

        /// <summary>
        /// Land if there is mesh close enough underneath. A body that is still rising is not
        /// landing on anything, and a body over a canyon keeps falling until there is something to
        /// arrive at -- the same answer the motor already gives an agent that finds no NavMesh,
        /// including its warning.
        /// </summary>
        private void TryLand(float deltaTime)
        {
            if (carryVelocity.y > 0f) return;

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                                        navMeshSnapDistance, NavMesh.AllAreas))
            {
                return;
            }

            if (!AgentCarry.HasLanded(transform.position.y, hit.position.y, carryVelocity.y,
                                      deltaTime, carryLandTolerance))
            {
                return;
            }

            EndCarry(hit.position);
        }
    }
}
