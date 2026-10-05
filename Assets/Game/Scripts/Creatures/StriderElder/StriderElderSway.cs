// The Strider elder's upper body. Its humanoid torso rides four robotic legs and has no clips of
// its own (humanoid clips cannot drive a rigid-part rig), so it would read as a statue on legs.
// This rolls the chest with the legs' gait and leans it into the walk, and holds the head against
// the chest so the gaze stays steady. Purely presentational: driven on every machine from the
// locomotion's own gait phase and measured speed, which a followed (client) copy derives from the
// replicated body, so nothing is sent.
//
// Order 160, after LeggedLocomotion (100) has posed the body this frame. Nothing else writes the
// chest or head bones: the elder has no Animator.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures
{
    [DefaultExecutionOrder(160)]
    [DisallowMultipleComponent]
    public class StriderElderSway : MonoBehaviour
    {
        [Tooltip("The bone the whole upper body hangs from; it rolls and leans.")]
        [SerializeField] private Transform chest;

        [Tooltip("The head bone, held against the chest's motion.")]
        [SerializeField] private Transform head;

        [Tooltip("Side-to-side roll of the chest at full walking speed, in degrees.")]
        [Range(0f, 15f)]
        [SerializeField] private float rollDegrees = 3f;

        [Tooltip("Forward lean of the chest at full walking speed, in degrees.")]
        [Range(0f, 20f)]
        [SerializeField] private float leanDegrees = 4f;

        [Tooltip("How much of the chest's motion the head undoes (0 rides with it, 1 holds still).")]
        [Range(0f, 1f)]
        [SerializeField] private float headSteadiness = 0.6f;

        [Tooltip("How quickly the sway follows a change of speed, per second. Terrain-rate smoothing " +
                 "only: the roll itself follows the gait phase unfiltered.")]
        [SerializeField] private float speedResponse = 3f;

        private LeggedLocomotion legs;
        private Quaternion chestRest, headRest;
        private float speed01;

        private void Awake()
        {
            legs = GetComponent<LeggedLocomotion>();
            if (chest != null) chestRest = chest.localRotation;
            if (head != null) headRest = head.localRotation;
        }

        private void LateUpdate()
        {
            if (chest == null || legs == null) return;

            float target = 0f;
            if (legs.isActiveAndEnabled && legs.IsReady && legs.MaxSpeed > 0f)
            {
                Vector3 v = legs.MeasuredVelocity;
                v.y = 0f;
                target = v.magnitude / legs.MaxSpeed;
            }
            speed01 = Mathf.MoveTowards(speed01, target, speedResponse * Time.deltaTime);

            Vector3 chestOffset = StriderElderSwayMath.Chest(legs.LastFrame.Phase, speed01, rollDegrees, leanDegrees);
            chest.localRotation = InBodyAxes(chest, chestOffset) * chestRest;
            if (head != null)
                head.localRotation = InBodyAxes(head, StriderElderSwayMath.Head(chestOffset, headSteadiness)) * headRest;
        }

        /// <summary>
        /// A lean (x) and roll (z) about the BODY's right and forward axes, expressed in the bone's
        /// parent space. The bones' own axes are whatever the export gave them, so a rotation in bone
        /// space would lean the chest sideways on one rig and forward on another.
        /// </summary>
        private Quaternion InBodyAxes(Transform bone, Vector3 offset)
        {
            Transform parent = bone.parent;
            Vector3 right = parent != null ? parent.InverseTransformDirection(transform.right) : transform.right;
            Vector3 forward = parent != null ? parent.InverseTransformDirection(transform.forward) : transform.forward;
            return Quaternion.AngleAxis(offset.x, right) * Quaternion.AngleAxis(offset.z, forward);
        }
    }
}
