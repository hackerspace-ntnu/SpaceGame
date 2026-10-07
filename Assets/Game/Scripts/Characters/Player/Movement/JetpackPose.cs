// How a jetpack pilot is angled, on every machine that can see one.
//
// There is no flight clip here — the player keeps their ordinary animation and this leans the
// whole body on top of it. That is a deliberate limit: authoring a flight clip is art work the
// pack does not have yet, and a lean composed onto the idle reads as somebody hanging under
// thrust, where a wrong clip would read as somebody standing up in mid-air.
using SpaceGame.Gear.Jetpack;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Tilts a flying player's skeleton to match where the nozzles are pointing.
    ///
    /// <para>
    /// <b>It reads the nozzles, not the motion</b>, which is the opposite of what
    /// <see cref="WingsuitPose"/> does and is right for the opposite reason. A glider's attitude
    /// IS its flight path, so measuring the path is measuring the truth. A jetpack's attitude is
    /// how it is pushing, and a pack fighting a headwind or hanging still under full rake is
    /// leaning hard while going nowhere — motion would show none of it. The nozzle angle already
    /// crosses the network on the item's hold stream for the pods themselves, so reading it here
    /// costs nothing extra and makes the body and the hardware agree by construction.
    /// </para>
    /// <para>
    /// The rotation is laid on the HIPS after the Animator has written them, as a delta rather
    /// than a pose: everything hangs off the hips, so one bone tilts the whole body, and composing
    /// with the clip keeps the arms and legs the clip's business. Ordered before
    /// <c>PlayerHeadLook</c> (950), which lays a world rotation on the neck and head — a head
    /// posed first would be dragged off its aim by its own parent.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(920)]
    public class JetpackPose : MonoBehaviour
    {
        [Tooltip("How far the body leans per degree the nozzles are deflected. Below 1 the pilot " +
                 "hangs more upright than the hardware, which is what a person under a pack " +
                 "actually does — the pods swing further than the body they are bolted to.")]
        [SerializeField, Range(0f, 2f)] private float leanShare = 0.65f;

        [Tooltip("Most the body will lean in any direction, degrees. The capsule never rotates, " +
                 "so this only ever moves what is drawn — but past about 45 the feet come " +
                 "through the collider and it reads as clipping rather than as flying.")]
        [SerializeField, Min(0f)] private float maxLean = 38f;

        [Tooltip("Extra degrees of nose-down lean at full throttle, on top of the nozzle lean. " +
                 "What sells the difference between falling and driving: a pilot with the key up " +
                 "hangs upright under dead motors, a thrusting one is pushed over by their own.")]
        [SerializeField, Range(0f, 30f)] private float thrustLean = 10f;

        [Tooltip("How quickly the lean follows the nozzles, per second. Low is syrupy; high " +
                 "passes every twitch of a replicated value straight into the spine.")]
        [SerializeField, Min(0.01f)] private float response = 9f;

        private Animator animator;
        private Transform hips;

        private float pitch;
        private float roll;

        /// <summary>
        /// Set once per frame by <c>JetpackItem</c>, from the owner's live flight or from the last
        /// hold tick that arrived. Kept as properties rather than resolved here so this stays a
        /// pure presentation component with one source of truth, whichever machine it is on.
        /// </summary>
        public bool Active { get; set; }

        /// <summary>Where the nozzles are pointing. See <see cref="Active"/>.</summary>
        public JetNozzle Nozzle { get; set; }

        /// <summary>Throttle 0..1, for the extra lean under power. See <see cref="Active"/>.</summary>
        public float Throttle { get; set; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float targetPitch = 0f;
            float targetRoll = 0f;

            if (Active)
            {
                targetPitch = Mathf.Clamp(Nozzle.Pitch * leanShare + Throttle * thrustLean,
                                          -maxLean, maxLean);
                targetRoll = Mathf.Clamp(Nozzle.Roll * leanShare, -maxLean, maxLean);
            }

            // Frame-rate independent ease, the same shape PlayerLook's look-down slide uses. It
            // runs even while inactive, so a flight that ends leaves the body easing upright
            // rather than snapping.
            float t = 1f - Mathf.Exp(-response * dt);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            roll = Mathf.Lerp(roll, targetRoll, t);

            if (hips == null) return;
            if (!Active && Mathf.Abs(pitch) < 0.05f && Mathf.Abs(roll) < 0.05f) return;

            hips.rotation = Lean(pitch, roll, transform) * hips.rotation;
        }

        /// <summary>
        /// The lean, as a world rotation to lay on the body.
        ///
        /// <para>
        /// Right, forward and up come off the BODY, not the bone: the hips' own axes depend on how
        /// the rig was exported and on whatever the clip has just done to them, and neither is a
        /// frame anyone wants to think in.
        /// </para>
        /// <para>
        /// <b>A SWING, not a pitch multiplied by a roll.</b> The product of two rotations about
        /// different horizontal axes is not a pure tilt: it carries a twist about the VERTICAL of
        /// roughly pitch·roll/2 — 3.6° with both leans at 20, and 13.5° at full stick on both.
        /// Nothing here asks for yaw, so it read as the pack sitting crooked across the pilot's
        /// back, and only ever in the air. Leaning the body's up and rotating onto it by the
        /// shortest arc has no twist by construction, and both leans still land where they were
        /// asked to. Static so the rule can be pinned without a rig.
        /// </para>
        /// </summary>
        public static Quaternion Lean(float pitchDegrees, float rollDegrees, Transform body)
        {
            Vector3 leanedUp = Quaternion.AngleAxis(pitchDegrees, body.right)
                               * Quaternion.AngleAxis(-rollDegrees, body.forward)
                               * body.up;

            return Quaternion.FromToRotation(body.up, leanedUp);
        }
    }
}
