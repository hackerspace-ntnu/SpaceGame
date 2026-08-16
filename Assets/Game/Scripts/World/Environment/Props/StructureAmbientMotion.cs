// Ambient idle motion for the derelict mining rig's twelve rig handles.
//
// The FBX ships a 12-bone armature (Arm_MiningRig) and no animation clips at
// all. Those bones are NOT deformation bones -- nothing is skinned to them.
// Each one is a plain transform with exactly one mesh parented under it, the
// same bone-parented-prop arrangement the Golem uses for its rocks. That fact
// is what decides the approach:
//
//   * Authoring clips would mean an Animator, an AnimatorController and six
//     imported takes to spin two fans and rock a flue -- and a clip that plays
//     on every instance in lockstep, so a row of rigs pulses as one machine.
//   * Driving the transforms directly costs one component, no clips, and gets
//     per-instance phase for free (see phaseOffset below).
//
// So this drives them procedurally. It is deliberately dumb: no state, no
// physics, no allocation, just a sine or a constant rate per handle.
//
// Bindings are a serialized array rather than a name lookup at runtime, because
// BuildingPrefabBuilder resolves the bones once at build time. A renamed bone
// then shows up as an empty slot in the inspector on the prefab, instead of a
// silent no-op discovered months later in a scene.
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Rotates or oscillates a set of transforms in place to keep a static
    /// structure from reading as a photograph. Drives bone handles on
    /// mining_rig_derelict; nothing here deforms a mesh.
    /// </summary>
    public class StructureAmbientMotion : MonoBehaviour
    {
        public enum MotionKind
        {
            /// <summary>Constant rotation -- vent fans.</summary>
            Spin = 0,
            /// <summary>Sine sweep about the axis -- floodlights panning.</summary>
            Sweep = 1,
            /// <summary>Small sine rock -- a flue stack or slack cable.</summary>
            Rock = 2,
        }

        [System.Serializable]
        public struct Handle
        {
            public Transform target;
            public MotionKind kind;

            [Tooltip("Local axis to turn about, in the bone's own space.")]
            public Vector3 axis;

            [Tooltip("Spin: degrees per second. Sweep/Rock: peak amplitude in degrees.")]
            public float amount;

            [Tooltip("Sweep/Rock only: full cycles per second. Ignored for Spin.")]
            public float frequency;

            [Tooltip("Cycle offset in turns (0..1) so sibling handles do not move in lockstep.")]
            public float phase;
        }

        [SerializeField] private Handle[] handles = new Handle[0];

        [Tooltip("Extra whole-object phase, randomised per instance at spawn so that two " +
                 "copies of the same derelict standing side by side do not tick together.")]
        [SerializeField] private bool randomisePhasePerInstance = true;

        // Rest pose is captured, not assumed to be identity: the bones arrive from
        // Blender with their own rest rotations, and driving them from identity
        // would snap every handle to a new orientation on the first frame.
        private Quaternion[] restRotations;
        private float phaseOffset;

        private void Awake()
        {
            restRotations = new Quaternion[handles.Length];
            for (int i = 0; i < handles.Length; i++)
                if (handles[i].target != null)
                    restRotations[i] = handles[i].target.localRotation;

            // Random.value, not a time-based seed -- two rigs spawned on the same
            // frame by the streamer would otherwise get identical offsets.
            phaseOffset = randomisePhasePerInstance ? Random.value : 0f;
        }

        private void Update()
        {
            if (restRotations == null) return;

            float t = Time.time;
            for (int i = 0; i < handles.Length; i++)
            {
                Handle h = handles[i];
                if (h.target == null) continue;

                Vector3 axis = h.axis.sqrMagnitude < 1e-6f ? Vector3.up : h.axis;

                if (h.kind == MotionKind.Spin)
                {
                    // Accumulates on the transform rather than being recomputed from
                    // t: a fan driven by an absolute angle visibly steps whenever
                    // Time.time gets large enough to lose float precision.
                    h.target.Rotate(axis, h.amount * Time.deltaTime, Space.Self);
                    continue;
                }

                float cycle = (t * h.frequency) + h.phase + phaseOffset;
                float angle = Mathf.Sin(cycle * Mathf.PI * 2f) * h.amount;
                h.target.localRotation =
                    restRotations[i] * Quaternion.AngleAxis(angle, axis);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Build-time entry point. BuildingPrefabBuilder owns the binding list;
        /// this exists so it does not have to reach in through SerializedObject
        /// for a type it does own.
        /// </summary>
        public void SetHandles(Handle[] value) => handles = value;
#endif
    }
}
