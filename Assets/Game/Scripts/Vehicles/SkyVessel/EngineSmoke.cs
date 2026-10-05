// Black smoke from a hull's engines: a thin trail while it idles, a heavier one under way.
//
// The puffs are DustCloudRecipe clouds (built by the fleet builder, one per duct, aimed astern), so
// they hang where they are thrown and a moving hull leaves its smoke behind it. This component only
// drives how many: the rate follows the speed the hull is actually seen to move at — read off its own
// transform, the way MonowheelPresentation reads a wheel's — so a host, a client watching the
// replicated hull and a hull flown by its brain all smoke alike, with no message and no saved state
// (GDC-L1-FEEL-0004: the effect answers a real event, the engines working).
//
// Clouds are the expensive part (overdraw per covered pixel), so the cap per engine is fixed at build
// time and the rate fades out with camera distance (GDC-L1-TECH-0002). The fade starts far out: a sky
// fleet is mostly seen from the ground, hundreds of metres off, and that is where its smoke has to read.
using UnityEngine;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.Vehicles
{
    public sealed class EngineSmoke : MonoBehaviour
    {
        [Tooltip("One cloud per engine, built by the fleet builder.")]
        [SerializeField] private ParticleSystem[] engines = new ParticleSystem[0];

        [Header("Speed")]
        [Tooltip("Speed (m/s) at which the smoke reaches its full rate: this hull's cruising speed.")]
        [SerializeField] private float fullSpeed = 2f;
        [Tooltip("Seconds over which the measured speed is smoothed, so a replicated pose does not flicker it.")]
        [SerializeField] private float speedSmoothing = 1f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (a load, a correction), not motion.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Emission per engine (puffs/s)")]
        [SerializeField] private float idleRate = 1f;
        [SerializeField] private float fullRate = 4f;

        [Header("Distance LOD")]
        [Tooltip("Full smoke up to this camera distance (m).")]
        [SerializeField] private float lodNear = 600f;
        [Tooltip("No new smoke beyond this camera distance (m).")]
        [SerializeField] private float lodFar = 1500f;

        private Vector3 lastPosition;
        private float speed;

        public int EngineCount => engines.Length;
        public float FullRate => fullRate;

        /// <summary>Builder only: the clouds to drive and the speed at which they peak.</summary>
        public void Configure(ParticleSystem[] clouds, float cruiseSpeed)
        {
            engines = clouds;
            fullSpeed = cruiseSpeed;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            speed = 0f;
        }

        private void OnValidate()
        {
            fullSpeed = Mathf.Max(0.01f, fullSpeed);
            speedSmoothing = Mathf.Max(0f, speedSmoothing);
            idleRate = Mathf.Max(0f, idleRate);
            fullRate = Mathf.Max(idleRate, fullRate);
            lodFar = Mathf.Max(lodNear + 1f, lodFar);
        }

        private void Update()
        {
            Camera cam = Camera.main;
            Present(Time.deltaTime, cam == null ? float.NaN : Vector3.Distance(cam.transform.position, transform.position));
        }

        /// <summary>One frame at a given camera distance (NaN = no camera: full smoke).</summary>
        public void Present(float dt, float cameraDistance)
        {
            if (dt <= 0f)
                return;

            Vector3 position = transform.position;
            speed = MonowheelPresentationMath.StepSpeed(speed, lastPosition, position, transform.forward,
                                                        dt, speedSmoothing, maxPlausibleSpeed, out _);
            lastPosition = position;

            float fraction = MonowheelPresentationMath.SpeedFraction(speed, fullSpeed);
            float rate = MonowheelPresentationMath.Rate(idleRate, fullRate, fraction) *
                         MonowheelPresentationMath.LodFactor(cameraDistance, lodNear, lodFar);

            foreach (ParticleSystem engine in engines)
            {
                if (engine == null) continue;
                ParticleSystem.EmissionModule emission = engine.emission;
                emission.rateOverTime = rate;
            }
        }
    }
}
