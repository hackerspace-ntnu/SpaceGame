// The Strider city's dust seen from afar: a second, huge and sparse DustCloudRecipe cloud per vehicle,
// puffs ~4x the near ones and a fifth the rate, rising round the hull. It fades in over exactly the band
// the vehicle's near dust fades out (IDustLodBand) and stays on to the vehicle's cull distance, so a
// merged far level (SettlementLods.md) -- whose legs and wheels are frozen -- stands inside a cloud.
//
// Driven by camera distance alone, moving or parked: the cloud is what hides the frozen far level, and a
// new world parks the city for its first ten minutes (user decision 2026-10-06; it used to follow ground
// speed and left a parked city bare). Every machine reads only its own view camera (ViewCamera), so the
// host, a client and the distant silhouette (DistantGroupSilhouette, which instantiates a copy of this
// GameObject) present alike, with nothing sent or saved. Overdraw is paid per covered pixel, and these
// clouds are, by construction, far (GDC-L1-TECH-0002).
using SpaceGame.Core;
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpaceGame.Vehicles
{
    public sealed class FarDust : MonoBehaviour
    {
        [Tooltip("The huge, sparse cloud, on this GameObject. World space, emission driven here.")]
        [SerializeField] private ParticleSystem cloud;

        [Tooltip("Puffs/s wherever the far dust is fully faded in.")]
        [FormerlySerializedAs("rateAtFullSpeed")]
        [SerializeField] private float rate = 4f;

        [Header("Distance crossfade")]
        [Tooltip("The near dust is full up to this camera distance (m), so the far dust is off.")]
        [SerializeField] private float fadeNear = 80f;
        [Tooltip("The near dust is gone beyond this camera distance (m), so the far dust is full.")]
        [SerializeField] private float fadeFar = 200f;
        [Tooltip("No far dust beyond this camera distance (m): where the vehicle itself is culled.")]
        [SerializeField] private float cullDistance = 1500f;

        public ParticleSystem Cloud => cloud;
        public float Rate => rate;
        public float FadeNear => fadeNear;
        public float FadeFar => fadeFar;
        public float CullDistance => cullDistance;

        /// <summary>Builder only: the cloud, its full rate, the near dust it crossfades with, and its cull.</summary>
        public void Configure(ParticleSystem puffs, float peakRate, IDustLodBand nearBand, float cull)
        {
            cloud = puffs;
            rate = peakRate;
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

        private void OnValidate()
        {
            rate = Mathf.Max(0f, rate);
            fadeFar = Mathf.Max(fadeNear + 1f, fadeFar);
            cullDistance = Mathf.Max(fadeFar, cullDistance);
        }

        private void Update() => Present(ViewCamera.DistanceTo(transform.position));

        // Switched off (a hidden silhouette): stop throwing, and let what is up settle out.
        private void OnDisable() => SetRate(0f);

        /// <summary>One frame at a given camera distance (NaN = no camera: none). Returns the rate set (puffs/s).</summary>
        public float Present(float cameraDistance)
        {
            float now = rate * Fade(cameraDistance, fadeNear, fadeFar, cullDistance);
            SetRate(now);
            // A stopped system ignores its rate: one never played (a preview scene, an edit-mode test).
            if (now > 0f && !cloud.isPlaying) cloud.Play();
            return now;
        }

        private void SetRate(float puffsPerSecond)
        {
            ParticleSystem.EmissionModule emission = cloud.emission;
            emission.rateOverTime = puffsPerSecond;
        }
    }
}
