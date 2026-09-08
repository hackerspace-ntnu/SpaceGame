// Bridges motor output into animator parameters for agent characters.
// Converts world velocity into local animation-space values each frame.
// Keeps animation updates centralized and independent from brain logic.
//
// It also drives ITSELF on any frame nobody drove it, and that is the half that makes a creature
// look alive on a machine that is only watching it. AgentController stops ticking on a client that
// does not own the agent — and NetAuthority goes further and disables the component outright — so
// the only thing left moving the body there is the replicated NetworkTransform. Reading the motor
// would report zero and the creature would slide across the sand with still feet; measuring the
// transform reports what the server actually did with it.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class AgentAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float animationSpeedMultiplier = 1.5f;
        [Tooltip("Extra velocity scale applied when walking (not running), to compensate for the reduced walk speed so animations don't look sluggish.")]
        [SerializeField] private float walkAnimBoost = 2f;

        [Tooltip("Playback rate for the whole Animator, applied once at Awake. 1 = leave alone.\n\n" +
                 "This is the fix for feet that skate. The two fields above only choose WHICH clip " +
                 "the blend tree lands on; neither changes how fast that clip plays, so a character " +
                 "whose motor speed does not match the clip's authored stride slides no matter how " +
                 "they are tuned — forwards if it moves slower than the stride, backwards if faster. " +
                 "Set this to groundSpeed / strideSpeed.\n\n" +
                 "Per-Animator, not per-controller, so a shared controller can drive a slow amble on " +
                 "one character and a brisk walk on another.")]
        [SerializeField] private float animatorSpeedScale = 1f;

        [Tooltip("Speed (m/s) above which a measured, replicated motion is animated as a run.\n\n" +
                 "Only used on machines that are watching this agent rather than driving it. The " +
                 "machine that drives it is told whether the intent was a run; a watcher can only " +
                 "see how fast the body moved, and guessing wrong costs a visibly different " +
                 "playback rate (see walkAnimBoost) rather than the wrong clip.")]
        [SerializeField] private float measuredRunSpeed = 3.5f;

        // The frame something else called Tick. Anything else means nobody is driving this agent's
        // animation on this machine, which is the watching case.
        private int lastDrivenFrame = -1;

        // Sampled in the parent's space, not the world's: a creature standing still on a walker's
        // moving deck is at rest, and measuring it in world space would animate it sprinting.
        // Degrades to world space when there is no parent, which is every loose agent.
        private Vector3 previousLocalPosition;
        private bool hasPreviousPosition;

        // Above this, the body did not move — it was moved. A NetworkTransform teleport, a chunk
        // streaming in under the agent, a respawn. Nothing this game drives goes this fast, and
        // feeding the jump in as a velocity would flash a full-speed run for a frame every time.
        private const float TeleportSpeed = 60f;

        // ---- finishing the stride ----------------------------------------------------
        //
        // A body stops faster than a walk cycle ends, and the two together read as a bug
        // rather than as a stop. A locomotion blend tree maps speed onto PLAYBACK RATE, so a
        // creature coasting to a halt plays its walk slower and slower as it slides, and
        // then crossfades out of it from wherever the cycle happened to have got to -- a leg
        // left hanging in the air while the body glides on underneath it, and the animation
        // apparently finishing only once everything has already come to rest.
        //
        // A hold breaks the link between body and cadence for the length of one stop. The
        // walk keeps playing at the rate it was already playing while the body brakes
        // underneath it, and the hold lets go only once the body is at rest AND the walk has
        // taken one more step onto a footfall. The blend down into the standing pose then
        // leaves from a planted pose instead of from mid-swing.
        //
        // Which frames those footfalls are is authored, not guessed -- see strideEndPhases.
        //
        // A hold is latched from the ORDER to stop, not from the velocity. Watching the
        // velocity would be guesswork -- every slowdown that is not a stop, a run easing into
        // a walk, a corner taken wide, would guess wrong and stride on at the old cadence --
        // and by the time a body is measurably stopping, the cadence worth keeping has
        // already decayed. The motor's immobile flag goes up on the frame the stop is
        // decided, which is a frame or two before the body has shed anything, so that is
        // what Tick latches on.
        //
        // Callers may also ask directly (HoldStride), and ConjurerCastModule does, because a
        // module that has decided to stop knows it one frame earlier than the motor does.

        // Below this the body has stopped, in m/s. The same order as the walk's exit
        // threshold in a locomotion controller: above it there is still a stride being
        // travelled, and holding one would only fight the real motion.
        private const float StrideRestSpeed = 0.25f;

        // Longest a hold may run before it releases itself. A creature knocked out of its
        // walk by a hurt reaction, or stopped by something that never gets round to
        // releasing, must not stride on the spot forever.
        private const float MaxStrideHoldSeconds = 4f;

        // Smoothing on SpeedX/SpeedY. Long enough to absorb a corner taken at speed, and
        // bypassed entirely on the frame a stride hold releases -- see snapLocomotion.
        private const float LocomotionDamping = 0.1f;

        [Tooltip("Where in the locomotion cycle a held stride is allowed to END, as " +
                 "normalized time in 0-1. These are the frames where a foot is on the " +
                 "floor: clip frame / frame count.\n\n" +
                 "Leave it empty and a hold runs to the end of the cycle, which is only " +
                 "the right place to stop if the clip was authored starting with a foot " +
                 "down. Most walk cycles are authored from the PASSING pose instead, so " +
                 "the cycle boundary is mid-swing - release there and the blend into the " +
                 "standing pose starts from a foot in the air, which is the exact thing " +
                 "the hold exists to avoid.")]
        [SerializeField] private float[] strideEndPhases;

        private bool strideHeld;
        private bool hasHeldCadence;

        // Last frame's immobile flag, so a stop can be spotted the frame it is ORDERED
        // rather than the frame it finishes. See the latch in Tick.
        private bool wasImmobile;

        // Animator space, and already scaled -- it is stored on its way to SpeedX/SpeedY, so
        // replaying it reproduces exactly the parameters the walk was playing at. World
        // space would be wrong twice over: a settling creature turns to face what it is
        // about to attack, and a held world vector would swing across the body as it did,
        // dragging the cadence -- and the sign of it -- around with the yaw.
        private Vector3 heldLocalVelocity;

        private float strideHoldElapsed;

        // Set on the frame a hold lets go, and worth its own field. SpeedX is written
        // through a 0.1 s damp, and damping the release would spend a third of a second
        // walking the parameter down from a full-speed stride -- ten more frames of clip
        // past the footfall the hold just spent half a cycle waiting for. The step lands
        // where it was aimed only if the parameter arrives with it.
        private bool snapLocomotion;

        private bool strideEndKnown;
        private float strideEndTime;
        private int strideStateHash;

        private void Awake()
        {
            if (!animator)
            {
                animator = GetComponent<Animator>();
            }

            if (!animator)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (!animator)
            {
                Debug.LogWarning($"{name}: AgentAnimatorDriver could not find an Animator on this object or children.", this);
                return;
            }

            // Applied once rather than every frame: nothing else on the agent writes Animator.speed,
            // and re-asserting it per tick would stamp on a hit-stop or slow-motion effect that did.
            if (!Mathf.Approximately(animatorSpeedScale, 1f))
                animator.speed = animatorSpeedScale;
        }

        private void OnEnable()
        {
            hasPreviousPosition = false;
            wasImmobile = false;
            ReleaseStride();
        }

        // A reparent moves the frame the sample is taken in, so the delta across that one frame is
        // the distance between two different origins rather than any motion. Mounting a creature
        // would otherwise flash a sprint on the frame it is seated.
        private void OnTransformParentChanged() => hasPreviousPosition = false;

        /// <summary>
        /// Fill in for whoever is not driving this animation.
        ///
        /// <para>
        /// LateUpdate, so every Update — this agent's controller when it has authority, and the
        /// NetworkTransform's own application of the server's pose — has already happened. The
        /// measurement is kept up to date on every frame, driven or not, so the first watching
        /// frame after an ownership change measures one frame of motion rather than the whole
        /// distance travelled since the last time anyone looked.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            Vector3 sample = SampleLocalPosition();
            float deltaTime = Time.deltaTime;

            Vector3 measured = hasPreviousPosition
                ? MeasureVelocity(ToWorldVector(sample - previousLocalPosition), deltaTime)
                : Vector3.zero;

            previousLocalPosition = sample;
            hasPreviousPosition = true;

            if (lastDrivenFrame == Time.frameCount)
                return;

            // isImmobile is false rather than measured: it means "this agent has been rooted in
            // place by something", which is a decision, and a watching machine has not been told
            // it. Reporting a stationary creature as immobilised would play the wrong idle.
            Tick(measured, false, measured.sqrMagnitude >= measuredRunSpeed * measuredRunSpeed);
        }

        /// <summary>
        /// Turn one frame of observed movement into a velocity, or into nothing when it was not
        /// movement at all.
        ///
        /// <para>
        /// Static and free of the transform so the rule can be tested without a frame: a
        /// non-positive delta is a paused or first frame and measures nothing, and anything past
        /// <see cref="TeleportSpeed"/> was a placement rather than a stride.
        /// </para>
        /// </summary>
        public static Vector3 MeasureVelocity(Vector3 worldDelta, float deltaTime)
        {
            if (deltaTime <= 0f) return Vector3.zero;

            Vector3 velocity = worldDelta / deltaTime;

            return velocity.sqrMagnitude > TeleportSpeed * TeleportSpeed ? Vector3.zero : velocity;
        }

        private Vector3 SampleLocalPosition()
        {
            Transform parent = transform.parent;
            return parent != null ? parent.InverseTransformPoint(transform.position) : transform.position;
        }

        private Vector3 ToWorldVector(Vector3 localDelta)
        {
            Transform parent = transform.parent;
            return parent != null ? parent.TransformVector(localDelta) : localDelta;
        }

        public void Tick(Vector3 worldVelocity, bool isImmobile, bool isRunning = false)
        {
            lastDrivenFrame = Time.frameCount;

            if (!animator)
            {
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                return;
            }

            // A stop was just ordered. The motor raises this on the frame it is told to stop
            // -- MoveIntent.Idle, or every module passing and the controller falling back to
            // one -- which is a frame or two before the body has shed any speed at all, so
            // the cadence the hold latches is still the one it was travelling at.
            //
            // This is the half the callers cannot cover. ConjurerCastModule asks for a hold
            // because it knows a cast is coming, but that is only ONE of the ways this
            // creature stops: a roam leg ends with WanderModule simply passing, and nothing
            // in that path has an opinion about legs at all. Every stop that goes through
            // the motor comes past here.
            //
            // Gated on the clip having said where its feet land, so this stays off for every
            // creature nobody has measured -- there, a hold could only guess at the end of
            // the cycle, and the loop point of an unmeasured walk is as likely to be a foot
            // in the air as not.
            if (isImmobile && !wasImmobile && strideEndPhases != null && strideEndPhases.Length > 0)
                HoldStride();

            wasImmobile = isImmobile;

            float speedScale = animationSpeedMultiplier * (isRunning ? 1f : walkAnimBoost);

            // Convert velocity into the animator rig's local space (important when the rig is on a
            // child transform that is yawed relative to the agent root).
            //
            // InverseTransformDirection rather than worldToLocalMatrix.MultiplyVector: the matrix
            // carries the transform's SCALE, and dividing metres per second by it turns a real
            // speed into a meaningless one. A rig imported at 100x -- which is what a Blender model
            // scaled by the importer rather than by the armature looks like -- reported an 8.99 m/s
            // charge as 0.09, every locomotion threshold in its controller stayed unmet, and the
            // creature slid across the ground in its idle pose. Direction is what this wants; scale
            // is not part of it.
            Vector3 localVelocity = animator.transform.InverseTransformDirection(worldVelocity) * speedScale;

            // Only the locomotion pair goes through the hold. FallSpeed and the two flags
            // below describe what is happening to the body RIGHT NOW, and a stop is exactly
            // when they stop agreeing with the legs -- freezing them too would tell the
            // controller the creature is still walking somewhere.
            localVelocity = ApplyStrideHold(localVelocity, worldVelocity.sqrMagnitude);

            float damp = snapLocomotion ? 0f : LocomotionDamping;
            snapLocomotion = false;

            animator.SetFloat("SpeedX", localVelocity.x, damp, Time.deltaTime);
            animator.SetFloat("SpeedY", localVelocity.z, damp, Time.deltaTime);
            animator.SetFloat("FallSpeed", worldVelocity.y, 0.1f, Time.deltaTime);
            animator.SetBool("IsGrounded", true);
            animator.SetBool("IsImmobalized", isImmobile);
        }

        /// <summary>
        /// Keep the walk playing at the cadence it has right now, all the way through a stop.
        ///
        /// <para>
        /// Call it every frame while the body is braking; it latches on the first call that
        /// has a stride to hold and then releases ITSELF, once the body is at rest and the
        /// cycle that was in flight has finished. Calling it from a standstill does nothing,
        /// because there is no stride to finish.
        /// </para>
        ///
        /// <para>
        /// What it buys is a stop that reads as a stop: the legs carry on at the cadence
        /// they had while the body runs down, take one more step onto a
        /// <see cref="strideEndPhases"/> footfall, and only then blend into the standing
        /// pose. See the note above the fields.</para>
        ///
        /// <para>
        /// "The cycle that was in flight" is <see cref="NextStrideEnd"/>'s answer, not the
        /// loop point -- most walks are authored from the passing pose, where the loop point
        /// is the worst frame in the clip to stand up out of.
        /// </para>
        /// </summary>
        public void HoldStride()
        {
            if (strideHeld || !hasHeldCadence) return;
            if (!animator || animator.runtimeAnimatorController == null) return;

            strideHeld = true;
            strideHoldElapsed = 0f;
            strideEndKnown = false;
        }

        /// Drop a hold without waiting for the stride to finish.
        ///
        /// For the caller that changes its mind -- a settle abandoned because the target
        /// walked away -- and for anything that is about to take the animator over. Safe to
        /// call when nothing is held.
        public void ReleaseStride()
        {
            if (strideHeld) snapLocomotion = true;

            strideHeld = false;
            strideEndKnown = false;
        }

        public bool IsHoldingStride => strideHeld;

        // ---- read-only, for the stop probe ----
        //
        // A stride hold is a decision spread over three or four seconds and two components,
        // and none of it leaves a trace in the pose until it is already too late to see what
        // went wrong. These let a diagnostic watch it happen.
        public bool StrideEndKnown => strideEndKnown;
        public float StrideEndTime => strideEndTime;
        public float StrideHoldElapsed => strideHoldElapsed;
        public Vector3 HeldCadence => heldLocalVelocity;
        public bool HasHeldCadence => hasHeldCadence;
        public float[] StrideEndPhases => strideEndPhases;

        /// <summary>
        /// Substitute the held cadence for the real one while a stop is in progress, and
        /// keep the cadence a later stop will hold the rest of the time.
        /// </summary>
        private Vector3 ApplyStrideHold(Vector3 localVelocity, float worldSpeedSqr)
        {
            bool bodyAtRest = worldSpeedSqr < StrideRestSpeed * StrideRestSpeed;

            if (!strideHeld)
            {
                // Sampled only while the creature is actually travelling, so what a stop
                // finds waiting for it is a stride rather than the tail end of the last one.
                if (!bodyAtRest)
                {
                    heldLocalVelocity = localVelocity;
                    hasHeldCadence = true;
                }

                return localVelocity;
            }

            strideHoldElapsed += Time.deltaTime;

            if (strideHoldElapsed >= MaxStrideHoldSeconds)
            {
                ReleaseStride();
                return localVelocity;
            }

            // Still running down. The cycle the hold finishes is whichever one is in flight
            // when the body finally comes to rest, so the mark is not taken until then.
            if (!bodyAtRest)
            {
                strideEndKnown = false;
                return heldLocalVelocity;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

            if (!strideEndKnown)
            {
                // Mid-crossfade the current state is already the DESTINATION, so a mark taken
                // here would be an offset into the wrong clip. Wait a frame; the held cadence
                // is what keeps the walk running while we do.
                if (animator.IsInTransition(0)) return heldLocalVelocity;

                strideEndTime = NextStrideEnd(state.normalizedTime, strideEndPhases);
                strideStateHash = state.fullPathHash;
                strideEndKnown = true;
                return heldLocalVelocity;
            }

            // Something else took the animator -- a hurt reaction, a death, a trigger from
            // another module. There is no walk cycle left to finish, and holding a cadence
            // into someone else's clip would only fight it.
            if (state.fullPathHash != strideStateHash)
            {
                ReleaseStride();
                return localVelocity;
            }

            if (state.normalizedTime >= strideEndTime)
            {
                ReleaseStride();
                return localVelocity;
            }

            return heldLocalVelocity;
        }

        public void TriggerHurt() => SetTriggerSafe("Hurt");
        public void TriggerDie() => SetTriggerSafe("Die");
        public void TriggerShootRifle() => SetTriggerSafe("ShootRifle");
        public void TriggerSpearAttack() => SetTriggerSafe("SpearAttack");
        public void TriggerByName(string triggerName) => SetTriggerSafe(triggerName);
        public void SetIsAiming(bool aiming) => animator?.SetBool("IsAiming", aiming);

        // The counterpart of TriggerByName, for a state a module has to HOLD rather than enter
        // once. Guarded the same way SetTriggerSafe is: an Animator with no controller assigned
        // logs a warning per call, and a module that writes a flag every frame turns that into a
        // warning per frame per creature.
        public void SetBoolByName(string boolName, bool value)
        {
            if (animator && animator.runtimeAnimatorController != null)
                animator.SetBool(boolName, value);
        }

        private void SetTriggerSafe(string triggerName)
        {
            if (animator && animator.runtimeAnimatorController != null)
                animator.SetTrigger(triggerName);
        }

        /// <summary>
        /// The next point at or after <paramref name="normalizedTime"/> where the stride may
        /// be put down, in the same units the animator reports.
        ///
        /// <para>
        /// Static and free of the animator so the rule can be checked without a rig.
        /// normalizedTime on a looping state counts UP without resetting -- 3.4 is the
        /// fourth pass, 40% through -- so the answer is the whole cycle it is in plus the
        /// first phase it has not reached yet, and the end of the cycle when there is none.
        /// With no phases given at all it is the end of the cycle, which is the honest
        /// answer for a clip nobody has told us anything about.
        /// </para>
        /// </summary>
        public static float NextStrideEnd(float normalizedTime, float[] phases)
        {
            float cycle = Mathf.Floor(normalizedTime);

            if (phases == null || phases.Length == 0) return cycle + 1f;

            // Not seeded with the cycle boundary: a stride caught PAST its last phase has to
            // wait for the first phase of the next cycle, and a boundary sitting between the
            // two would win that race and put the foot down in mid-air.
            float best = float.PositiveInfinity;

            foreach (float p in phases)
            {
                float at = cycle + Mathf.Repeat(p, 1f);
                if (at <= normalizedTime) at += 1f;
                if (at < best) best = at;
            }

            return best;
        }

        private void OnValidate()
        {
            animationSpeedMultiplier = Mathf.Max(0.1f, animationSpeedMultiplier);
            animatorSpeedScale = Mathf.Clamp(animatorSpeedScale, 0.05f, 4f);
            measuredRunSpeed = Mathf.Max(0.1f, measuredRunSpeed);
        }
    }
}
