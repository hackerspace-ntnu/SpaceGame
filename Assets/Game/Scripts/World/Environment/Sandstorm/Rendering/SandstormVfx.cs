// Layer 3: the grit going past your face.
//
// Added automatically to whatever the profile names as its near-detail prefab, so that prefab can
// be an ordinary VFX Graph or particle effect with nothing special about it. This component finds
// whatever is inside and drives it from one number.
//
// Purely decorative. Delete the prefab reference and the storm still looks and behaves right —
// which is exactly why this is the first layer a quality tier turns off.
using UnityEngine;
using UnityEngine.VFX;

namespace SpaceGame.World.Weather
{
    [DisallowMultipleComponent]
    public class SandstormVfx : MonoBehaviour
    {
        [Tooltip("Float property on a VFX Graph asset that takes the storm's intensity, 0 to 1. " +
                 "Missing properties are skipped, so a graph that does not want it costs nothing.")]
        [SerializeField] private string intensityProperty = "Intensity";

        [Tooltip("Vector3 property on a VFX Graph asset that takes the direction the sand blows.")]
        [SerializeField] private string windProperty = "WindDirection";

        [Tooltip("How fast the grit reaches full strength as you walk into a storm, as an " +
                 "exponent on the storm's density. A storm's density ramps across its whole " +
                 "edge feather — hundreds of metres — so a linear response (1) leaves the entire " +
                 "border with almost no sand in the air, which is not what the edge of a haboob " +
                 "looks like: you are in blowing sand from the moment you enter it, and what " +
                 "changes with depth is how far you can see, not whether there is sand. Below 1 " +
                 "the grit arrives at the border and only its violence scales inward.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float presenceCurve = 0.4f;

        private VisualEffect[] graphs;
        private ParticleSystem[] particles;
        private float[] baseEmission;
        private int intensityId;
        private int windId;

        private void Awake()
        {
            intensityId = Shader.PropertyToID(intensityProperty);
            windId = Shader.PropertyToID(windProperty);

            graphs = GetComponentsInChildren<VisualEffect>(true);
            particles = GetComponentsInChildren<ParticleSystem>(true);

            // The authored rate is the rate at full intensity; everything below scales down from
            // it. Captured once, because reading it back after scaling would ratchet toward zero.
            baseEmission = new float[particles.Length];
            for (int i = 0; i < particles.Length; i++)
                baseEmission[i] = particles[i].emission.rateOverTimeMultiplier;
        }

        /// <summary>
        /// Sets how hard the near detail is blowing, from the storm's density at the camera.
        /// Zero should look like nothing at all.
        /// </summary>
        public void Drive(float intensity, Vector3 windDirection)
        {
            // How much sand is in the air, which is NOT the same number as how dense the storm is.
            // See presenceCurve: the density ramp is a visibility ramp, and driving the grit
            // straight off it means the outer half of every storm has no sand blowing through it.
            float presence = Mathf.Pow(Mathf.Clamp01(intensity), presenceCurve);

            for (int i = 0; i < graphs.Length; i++)
            {
                VisualEffect graph = graphs[i];
                if (graph.HasFloat(intensityId))
                    graph.SetFloat(intensityId, presence);
                if (graph.HasVector3(windId))
                    graph.SetVector3(windId, windDirection);
            }

            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem.EmissionModule emission = particles[i].emission;
                emission.rateOverTimeMultiplier = baseEmission[i] * presence;
            }
        }
    }
}
