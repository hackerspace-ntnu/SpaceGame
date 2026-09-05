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
        private IMountJumpMotor jumpMotor;

        private int lastDrivenFrame = -1;

        // Sampled in the parent's space, not the world's: a creature standing still on a walker's
        // moving deck is at rest, and measuring it in world space would animate it sprinting.
        // Degrades to world space when there is no parent, which is every loose agent.
        private Vector3 previousLocalPosition;
        private bool hasPreviousPosition;

        // Signed yaw rate in degrees/second, positive turning right. Measured from the transform
        // for the same reason the velocity is: a NavMeshAgent turning on the spot reports no
        // velocity at all, so a creature changing its mind about where to go stood frozen and
        // pivoted like a turret. Nothing tells the animator the agent is turning; this does.
        private float previousYaw;
        private bool hasPreviousYaw;
        private float turnRate;

        // Above this, the body did not move — it was moved. A NetworkTransform teleport, a chunk
        // streaming in under the agent, a respawn. Nothing this game drives goes this fast, and
        // feeding the jump in as a velocity would flash a full-speed run for a frame every time.
        private const float TeleportSpeed = 60f;

        // The rotational twin of TeleportSpeed: four full turns a second. Nothing steers this
        // fast, so anything above it was the transform being placed rather than turned.
        private const float TeleportYawRate = 1440f;

        // Optional parameters -- TurnSpeed, and whatever dwell flags a creature's tasks name.
        // Writing a parameter a controller does not have logs a warning every frame per agent in
        // the Editor, so the controller is asked once and the answer cached.
        private static readonly int TurnSpeedHash = Animator.StringToHash("TurnSpeed");
        private readonly System.Collections.Generic.Dictionary<int, bool> parameterCache
            = new System.Collections.Generic.Dictionary<int, bool>();

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

            // Optional: only mounts that can be jumped have one, and an agent without it is
            // permanently grounded, which is what it was before this existed.
            jumpMotor = GetComponentInParent<IMountJumpMotor>();

            // Applied once rather than every frame: nothing else on the agent writes Animator.speed,
            // and re-asserting it per tick would stamp on a hit-stop or slow-motion effect that did.
            if (!Mathf.Approximately(animatorSpeedScale, 1f))
                animator.speed = animatorSpeedScale;
        }

        private void OnEnable()
        {
            hasPreviousPosition = false;
            hasPreviousYaw = false;
            parameterCache.Clear();   // the controller may have been swapped
        }

        // A reparent moves the frame the sample is taken in, so the delta across that one frame is
        // the distance between two different origins rather than any motion. Mounting a creature
        // would otherwise flash a sprint on the frame it is seated.
        private void OnTransformParentChanged()
        {
            hasPreviousPosition = false;
            hasPreviousYaw = false;
        }

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

            float yaw = SampleYaw();
            turnRate = hasPreviousYaw ? MeasureTurnRate(previousYaw, yaw, deltaTime) : 0f;
            previousYaw = yaw;
            hasPreviousYaw = true;

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

        /// <summary>
        /// One frame of turning as a signed rate in degrees/second, positive to the right.
        ///
        /// <para>
        /// Static and free of the transform so the rule is testable without a frame, and shaped
        /// like <see cref="MeasureVelocity"/> for the same reasons: a non-positive delta measures
        /// nothing, and anything past <see cref="TeleportYawRate"/> was a placement — a respawn or
        /// a replicated snap — rather than a turn, and feeding it in would flash a full turn clip
        /// for one frame.
        /// </para>
        /// </summary>
        public static float MeasureTurnRate(float previousDegrees, float currentDegrees, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;

            float rate = Mathf.DeltaAngle(previousDegrees, currentDegrees) / deltaTime;

            return Mathf.Abs(rate) > TeleportYawRate ? 0f : rate;
        }

        // Measured in the parent's frame for the same reason the position is: a creature standing
        // still on a turning deck is not turning itself.
        private float SampleYaw()
        {
            Transform parent = transform.parent;
            return parent != null
                ? (Quaternion.Inverse(parent.rotation) * transform.rotation).eulerAngles.y
                : transform.eulerAngles.y;
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

            float speedScale = animationSpeedMultiplier * (isRunning ? 1f : walkAnimBoost);
            // Convert velocity in the animator rig's local space (important when rig is on a child transform).
            Vector3 localVelocity = animator.transform.worldToLocalMatrix.MultiplyVector(worldVelocity) * speedScale;

            animator.SetFloat("SpeedX", localVelocity.x, 0.1f, Time.deltaTime);
            animator.SetFloat("SpeedY", localVelocity.z, 0.1f, Time.deltaTime);
            animator.SetFloat("FallSpeed", worldVelocity.y, 0.1f, Time.deltaTime);
            // Not a constant any more. A jump on a NavMeshAgent moves baseOffset, which never
            // reaches the velocity this method is handed, so the motor is the only thing that
            // knows the animal is in the air.
            animator.SetBool("IsGrounded", jumpMotor == null || !jumpMotor.IsAirborne);
            animator.SetBool("IsImmobalized", isImmobile);

            // Not scaled by speedScale: this is a real rate in degrees/second, and the controller
            // thresholds it against one. Damped harder than the speeds because a NavMeshAgent's
            // yaw is noisy frame to frame and an undamped value flickers the turn state on and off.
            if (HasParameter(TurnSpeedHash, AnimatorControllerParameterType.Float))
                animator.SetFloat(TurnSpeedHash, turnRate, 0.15f, Time.deltaTime);
        }

        /// <summary>
        /// Hold an animator bool by name, if this controller has one.
        ///
        /// For state that lasts — NpcTaskModule holds "IsGrazing" for as long as the animal is
        /// working a feeding site. A trigger would fire once and leave it standing to attention
        /// for the rest of a forty-second meal.
        /// </summary>
        public void SetBoolByName(string parameterName, bool value)
        {
            if (string.IsNullOrEmpty(parameterName)) return;
            if (!animator || animator.runtimeAnimatorController == null) return;

            int hash = Animator.StringToHash(parameterName);
            if (HasParameter(hash, AnimatorControllerParameterType.Bool))
                animator.SetBool(hash, value);
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (parameterCache.TryGetValue(hash, out bool known))
                return known;

            bool found = false;
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == hash && parameters[i].type == type)
                {
                    found = true;
                    break;
                }
            }

            parameterCache[hash] = found;
            return found;
        }

        public void TriggerHurt() => SetTriggerSafe("Hurt");
        public void TriggerDie() => SetTriggerSafe("Die");
        public void TriggerShootRifle() => SetTriggerSafe("ShootRifle");
        public void TriggerSpearAttack() => SetTriggerSafe("SpearAttack");
        public void TriggerByName(string triggerName) => SetTriggerSafe(triggerName);
        public void SetIsAiming(bool aiming) => animator?.SetBool("IsAiming", aiming);

        private void SetTriggerSafe(string triggerName)
        {
            if (animator && animator.runtimeAnimatorController != null)
                animator.SetTrigger(triggerName);
        }

        private void OnValidate()
        {
            animationSpeedMultiplier = Mathf.Max(0.1f, animationSpeedMultiplier);
            animatorSpeedScale = Mathf.Clamp(animatorSpeedScale, 0.05f, 4f);
            measuredRunSpeed = Mathf.Max(0.1f, measuredRunSpeed);
        }
    }
}
