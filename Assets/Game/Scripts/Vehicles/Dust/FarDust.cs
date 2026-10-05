// The Strider city's dust seen from afar: a second, huge and sparse DustCloudRecipe cloud per vehicle,
// puffs ~4x the near ones and a fifth the rate, rising round the hull while the vehicle moves. It fades
// in over exactly the band the vehicle's near dust fades out (IDustLodBand) and stays on to the
// vehicle's cull distance, so a merged far level (SettlementLods.md) -- whose legs and wheels are frozen
// -- marches inside a cloud.
//
// The rate follows how fast this transform is seen to move (GroundSpeedGauge), so the host, a client
// watching a replicated hull and the distant silhouette (DistantGroupSilhouette, which instantiates a
// copy of this GameObject) all present alike, with nothing sent or saved (GDC-L1-FEEL-0004). Overdraw is
// paid per covered pixel, and these clouds are, by construction, far (GDC-L1-TECH-0002).
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public sealed class FarDust : MonoBehaviour
    {
        [Tooltip("The huge, sparse cloud, on this GameObject. World space, emission driven here.")]
        [SerializeField] private ParticleSystem cloud;

        [Header("Speed")]
        [Tooltip("Ground speed (m/s) at which the far dust reaches its full rate: the city's march.")]
        [SerializeField] private float fullSpeed = 2.7f;
        [Tooltip("Seconds over which the speed is smoothed, so a replicated or stepped pose does not flicker it.")]
        [SerializeField] private float speedSmoothing = 0.3f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (a load, a refold), not motion.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Emission (puffs/s)")]
        [SerializeField] private float rateAtFullSpeed = 4f;

        [Header("Distance crossfade")]
        [Tooltip("The near dust is full up to this camera distance (m), so the far dust is off.")]
        [SerializeField] private float fadeNear = 80f;
        [Tooltip("The near dust is gone beyond this camera distance (m), so the far dust is full.")]
        [SerializeField] private float fadeFar = 200f;
        [Tooltip("No far dust beyond this camera distance (m): where the vehicle itself is culled.")]
        [SerializeField] private float cullDistance = 1500f;

        private GroundSpeedGauge gauge;

        public ParticleSystem Cloud => cloud;
        public float FullSpeed => fullSpeed;
        public float RateAtFullSpeed => rateAtFullSpeed;
        public float FadeNear => fadeNear;
        public float FadeFar => fadeFar;
        public float CullDistance => cullDistance;

        /// <summary>Builder only: the cloud, the speed and rate it peaks at, the near dust it crossfades with, and its cull.</summary>
        public void Configure(ParticleSystem puffs, float cruiseSpeed, float peakRate, IDustLodBand nearBand, float cull)
        {
            cloud = puffs;
            fullSpeed = cruiseSpeed;
            rateAtFullSpeed = peakRate;
            fadeNear = nearBand.LodNear;
            fadeFar = nearBand.LodFar;
            cullDistance = cull;
        }

        /// <summary>
        /// How much far dust at <paramref name="cameraDistance"/>: 1 minus the near dust's LodFactor inside
        /// the cull, 0 beyond it and with no camera (NaN, where the near dust is full).
        /// </summary>
        public static float Fade(float cameraDistance, float near, float far, float cull)
        {
            if (float.IsNaN(cameraDistance) || cameraDistance >= cull) return 0f;
            return 1f - MonowheelPresentationMath.LodFactor(cameraDistance, near, far);
        }

        /// <summary>Forget where this was: after a spawn, a load or any snap into place.</summary>
        public void ResetBaseline() => gauge.Reset(transform.position);

        private void OnEnable() => ResetBaseline();

        private void OnValidate()
        {
            fullSpeed = Mathf.Max(0.01f, fullSpeed);
            speedSmoothing = Mathf.Max(0f, speedSmoothing);
            rateAtFullSpeed = Mathf.Max(0f, rateAtFullSpeed);
            fadeFar = Mathf.Max(fadeNear + 1f, fadeFar);
            cullDistance = Mathf.Max(fadeFar, cullDistance);
        }

        private void Update()
        {
            Camera cam = Camera.main;
            Present(Time.deltaTime, cam == null ? float.NaN : Vector3.Distance(cam.transform.position, transform.position));
        }

        /// <summary>One frame at a given camera distance (NaN = no camera: none). Returns the rate set (puffs/s).</summary>
        public float Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return cloud.emission.rateOverTime.constant;

            float speed = gauge.MeasureAlongStep(transform.position, dt, speedSmoothing, maxPlausibleSpeed);
            float rate = MonowheelPresentationMath.Rate(0f, rateAtFullSpeed, MonowheelPresentationMath.SpeedFraction(speed, fullSpeed))
                         * Fade(cameraDistance, fadeNear, fadeFar, cullDistance);
            ParticleSystem.EmissionModule emission = cloud.emission;
            emission.rateOverTime = rate;
            // A stopped system ignores its rate: one never played (a preview scene, an edit-mode test).
            if (rate > 0f && !cloud.isPlaying) cloud.Play();
            return rate;
        }
    }
}
