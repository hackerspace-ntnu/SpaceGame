using System.Reflection;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Feeds wind into every renderer using the SpaceGame/ClothWind shader on this character.
    ///
    /// The shader does the deformation; this only decides which way and how hard the wind blows,
    /// and pushes that through a MaterialPropertyBlock so all garments can share one material.
    ///
    /// Wind comes from the DuneFoil <c>WindField</c> when one is in the scene, so the nomad's
    /// cape and the sailer's canvas agree about the weather. That class lives in the
    /// SpaceGame.Vehicles.DuneFoil assembly, which this one cannot reference — the assembly
    /// definitions do not depend on Assembly-CSharp and adding the edge the other way would
    /// couple every character to the vehicle code. So it is resolved reflectively, once, and
    /// falls back to a self-contained breeze when the type or the instance is absent.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClothWindDriver : MonoBehaviour
    {
        [Header("Wind")]
        [Tooltip("Compass bearing the wind blows FROM, in degrees. Used only when no WindField " +
                 "is present in the scene.")]
        [SerializeField] private float fallbackBearingFrom = 215f;

        [Tooltip("Wind displacement in metres when no WindField is present.")]
        [SerializeField] private float fallbackStrength = 0.22f;

        [Tooltip("How much the wind direction wanders, in degrees.")]
        [SerializeField] private float directionWander = 18f;

        [Tooltip("How quickly the direction wanders.")]
        [SerializeField] private float wanderSpeed = 0.11f;

        [Header("Motion Response")]
        [Tooltip("Cloth trails a moving character. This scales the character's own velocity " +
                 "into the apparent wind, so the cape streams out when the nomad walks.")]
        [SerializeField] private float motionInfluence = 0.6f;

        [Tooltip("Metres of displacement per metre/second of apparent wind.")]
        [SerializeField] private float strengthPerSpeed = 0.05f;

        [Tooltip("Upper bound on displacement, so a sprint does not tear the cloth off.")]
        [SerializeField] private float maxStrength = 0.65f;

        [Header("Renderers")]
        [Tooltip("Leave empty to auto-collect every renderer under this object using the " +
                 "ClothWind shader.")]
        [SerializeField] private Renderer[] clothRenderers;

        private static readonly int WindDirId = Shader.PropertyToID("_WindDirection");
        private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");

        private MaterialPropertyBlock block;
        private Vector3 lastPosition;
        private Vector3 smoothedVelocity;

        // Reflective handle on WindField.Active / SampleAt, resolved once in Awake.
        private static PropertyInfo windFieldActive;
        private static MethodInfo windFieldSampleAt;
        private static bool windFieldProbed;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            lastPosition = transform.position;

            if (clothRenderers == null || clothRenderers.Length == 0)
                clothRenderers = CollectClothRenderers();

            ProbeWindField();
        }

        /// <summary>
        /// Every renderer under this object whose material actually uses the cloth shader.
        /// Checking the shader rather than the name means adding another garment to the model
        /// needs no wiring here.
        /// </summary>
        private Renderer[] CollectClothRenderers()
        {
            var found = new System.Collections.Generic.List<Renderer>();
            var all = GetComponentsInChildren<Renderer>(true);

            foreach (var r in all)
            {
                // sharedMaterials, not materials: touching .materials in edit mode leaks a
                // cloned material into the scene every time this runs.
                var mats = r.sharedMaterials;
                if (mats == null) continue;

                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.shader.name == "SpaceGame/ClothWind")
                    {
                        found.Add(r);
                        break;
                    }
                }
            }

            return found.ToArray();
        }

        private static void ProbeWindField()
        {
            if (windFieldProbed) return;
            windFieldProbed = true;

            var type = System.Type.GetType("SpaceGame.Vehicles.DuneFoil.WindField, SpaceGame.Vehicles.DuneFoil");
            if (type == null) return;

            windFieldActive = type.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
            windFieldSampleAt = type.GetMethod("SampleAt", BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>
        /// World-space wind velocity, from the WindField if there is one, otherwise a slowly
        /// wandering breeze so the cloth still moves in scenes that have no weather.
        /// </summary>
        private Vector3 SampleWind()
        {
            if (windFieldActive != null && windFieldSampleAt != null)
            {
                var active = windFieldActive.GetValue(null);
                if (active != null)
                    return (Vector3)windFieldSampleAt.Invoke(active, new object[] { transform.position });
            }

            // Fallback: a bearing is the direction the wind comes FROM, so travel is the opposite.
            float wander = Mathf.Sin(Time.time * wanderSpeed) * directionWander;
            Vector3 from = Quaternion.Euler(0f, fallbackBearingFrom + wander, 0f) * Vector3.forward;
            return -from * (fallbackStrength / Mathf.Max(strengthPerSpeed, 1e-4f));
        }

        private void LateUpdate()
        {
            if (clothRenderers == null || clothRenderers.Length == 0) return;

            // Character velocity, smoothed so a NavMeshAgent's per-frame jitter does not make
            // the cape twitch.
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;

            Vector3 frameVelocity = Time.deltaTime > 1e-5f ? delta / Time.deltaTime : Vector3.zero;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, frameVelocity,
                                            1f - Mathf.Exp(-8f * Time.deltaTime));

            // Apparent wind: what the cloth feels is the weather minus the character's own
            // motion. Walking into a headwind streams the cape harder; running downwind
            // slackens it. This is why the cape trails behind a moving nomad for free.
            Vector3 wind = SampleWind();
            Vector3 apparent = wind - smoothedVelocity * motionInfluence;

            float speed = apparent.magnitude;
            Vector3 dir = speed > 1e-4f ? apparent / speed : transform.forward;

            float strength = Mathf.Min(speed * strengthPerSpeed, maxStrength);

            block.Clear();
            block.SetVector(WindDirId, new Vector4(dir.x, dir.y, dir.z, 0f));
            block.SetFloat(WindStrengthId, strength);

            foreach (var r in clothRenderers)
            {
                if (r != null)
                    r.SetPropertyBlock(block);
            }
        }
    }
}
