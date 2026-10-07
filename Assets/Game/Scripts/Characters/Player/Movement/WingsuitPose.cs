// How a gliding astronaut is angled, on every machine that can see one.
//
// The prone attitude itself is in the Glide clip, where a pose belongs — so a peer whose copy of
// this component never ran still sees somebody flying rather than somebody standing up in mid-air.
// What is here is the part a clip cannot hold: the body leaning with the flight path and rolling
// into its turns, which changes every frame.
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Tilts a gliding player's skeleton to match how they are actually moving.
    ///
    /// <para>
    /// Runs on every machine and measures rather than reads. The owner has a real flight state and
    /// could be asked, but a peer has only a replicated transform — and deriving the angle from
    /// motion on both sides means one code path, one appearance, and nothing extra on the wire.
    /// It is the same trick the ornithopter's motor uses to animate a craft it is not flying.
    /// </para>
    /// <para>
    /// The rotation is laid on the HIPS after the Animator has written them, as a delta rather
    /// than a pose: everything else hangs off the hips, so one bone tilts the whole body, and
    /// composing with the clip rather than replacing it keeps the arms and legs the clip's
    /// business. Ordered before <c>PlayerHeadLook</c> (950), which lays a world rotation on the
    /// neck and head — a head posed first would be dragged off its aim by its own parent.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(920)]
    public class WingsuitPose : MonoBehaviour
    {
        [Tooltip("How much of the measured bank the body actually rolls, 0..1. The bank is " +
                 "inferred from how fast the heading is turning, so this is the scale between " +
                 "'degrees per second of turn' and 'degrees of lean'.")]
        [SerializeField, Range(0f, 1f)] private float bankFromTurn = 0.5f;

        [Tooltip("Most the body will roll, degrees.")]
        [SerializeField, Min(0f)] private float maxBank = 60f;

        [Tooltip("How quickly the tilt follows the motion, per second. Low is syrupy; high passes " +
                 "every twitch of a replicated transform straight into the spine.")]
        [SerializeField, Min(0.01f)] private float response = 8f;

        [Tooltip("Speed below which there is no direction worth reading off the motion, m/s.")]
        [SerializeField, Min(0.01f)] private float minSpeed = 2f;

        private Animator animator;
        private Transform hips;

        private Vector3 lastPosition;
        private float lastHeading;
        private float pitch;
        private float bank;

        /// <summary>
        /// Set by whatever put this component here, once per frame, from the animator bool the
        /// glide already replicates. Kept as a property rather than read from the Animator here so
        /// there is one reader of that parameter and this stays a pure presentation component.
        /// </summary>
        public bool Active { get; set; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null) hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            lastPosition = transform.position;
            lastHeading = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Measure(dt);

            if (!Active || hips == null) return;

            // Right and forward come off the BODY, not the bone: the hips' own axes depend on how
            // the rig was exported and on whatever the clip has just done to them, and neither is
            // a frame anyone wants to think in.
            Quaternion tilt = Quaternion.AngleAxis(-pitch, transform.right)
                              * Quaternion.AngleAxis(bank, transform.forward);

            hips.rotation = tilt * hips.rotation;
        }

        /// <summary>
        /// Where the body is going and how hard it is turning, from the transform alone.
        ///
        /// Position deltas rather than the Rigidbody, deliberately: a remote player's body is
        /// kinematic and moved by NetworkTransform, so its velocity reads as zero on every machine
        /// but its owner's — which is exactly the case this component exists for.
        /// </summary>
        private void Measure(float dt)
        {
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;

            float heading = transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(lastHeading, heading) / dt;
            lastHeading = heading;

            float speed = delta.magnitude / dt;

            float targetPitch = 0f;
            if (speed >= minSpeed)
                targetPitch = Mathf.Asin(Mathf.Clamp(delta.y / delta.magnitude, -1f, 1f))
                              * Mathf.Rad2Deg;

            float targetBank = Mathf.Clamp(turnRate * bankFromTurn, -maxBank, maxBank);

            if (!Active)
            {
                targetPitch = 0f;
                targetBank = 0f;
            }

            // Frame-rate independent ease, the same shape PlayerLook's look-down slide uses.
            float t = 1f - Mathf.Exp(-response * dt);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            bank = Mathf.Lerp(bank, targetBank, t);
        }
    }
}
