// A cloud of sand thrown out from under every foot that lands: the walking houses, the desert crawler,
// the crab outriders. Each footfall is a burst of puffs ringed round the sole and thrown outward along
// the ground, sized to the foot, which then hang and churn the way the monowheel's dust does -- the
// puffs are one DustCloudRecipe cloud per machine, built by DustWiring.
//
// The signal is LeggedLocomotion.Footfalls, which every machine computes for itself: the legs
// simulate on the host and on every client (Locomotion.md, Multiplayer), so this needs no message and
// holds no state worth saving (GDC-L1-FEEL-0004: the cloud answers a real event, a foot taking weight).
//
// Overdraw is paid per covered pixel, so the cloud's cap is fixed at build time from the machine's
// fastest footfall rate, and the burst thins out with camera distance (GDC-L1-TECH-0002).
using SpaceGame.Core;
using SpaceGame.Locomotion;
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    // After LeggedLocomotion (100), so the footfalls read are this frame's.
    [DefaultExecutionOrder(150)]
    public sealed class FootfallDust : MonoBehaviour, IDustLodBand
    {
        [Tooltip("The legs whose landings throw dust.")]
        [SerializeField] private LeggedLocomotion locomotion;
        [Tooltip("The lingering cloud the puffs are emitted into. World space, emission off.")]
        [SerializeField] private ParticleSystem cloud;

        [Header("Burst")]
        [Tooltip("Puffs thrown per foot landing, ringed round the sole.")]
        [SerializeField] private int puffsPerFootfall = 5;
        [Tooltip("Puff diameter at birth, in footprint radii. The cloud recipe then billows it about 4x.")]
        [SerializeField] private float sizePerFootRadius = 0.9f;
        [Tooltip("Outward speed (m/s) per metre of footprint radius. Drag stops a puff within about a second.")]
        [SerializeField] private float throwSpeedPerFootRadius = 3f;
        [Tooltip("How far a puff's flight leans up from the ground plane: 0 skims the sand, 1 is 45 degrees.")]
        [SerializeField] private float upwardTilt = 0.8f;
        [Tooltip("Birth height above the contact point, in footprint radii, so a puff starts just off the sand.")]
        [SerializeField] private float liftPerFootRadius = 0.5f;
        [Tooltip("How far each puff may stray round the ring from its even slot, as a fraction of the slot.")]
        [Range(0f, 1f)]
        [SerializeField] private float ringJitter = 0.6f;
        [Tooltip("Puff size varies by up to this fraction either way.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float sizeJitter = 0.2f;

        [Header("Distance LOD")]
        [Tooltip("Full bursts up to this camera distance (m).")]
        [SerializeField] private float lodNear = 80f;
        [Tooltip("No dust beyond this camera distance (m).")]
        [SerializeField] private float lodFar = 200f;

        private int seenStep;

        public ParticleSystem Cloud => cloud;
        public LeggedLocomotion Locomotion => locomotion;
        public int PuffsPerFootfall => puffsPerFootfall;
        public float SizePerFootRadius => sizePerFootRadius;
        public float LodNear => lodNear;
        public float LodFar => lodFar;

        /// <summary>Builder only: the legs to watch, the cloud to fill and how many puffs a footfall throws.</summary>
        public void Configure(LeggedLocomotion legs, ParticleSystem puffs, int puffsPerLanding)
        {
            locomotion = legs;
            cloud = puffs;
            puffsPerFootfall = puffsPerLanding;
        }

        private void OnValidate()
        {
            puffsPerFootfall = Mathf.Max(0, puffsPerFootfall);
            sizePerFootRadius = Mathf.Max(0f, sizePerFootRadius);
            throwSpeedPerFootRadius = Mathf.Max(0f, throwSpeedPerFootRadius);
            lodFar = Mathf.Max(lodNear + 1f, lodFar);
        }

        private void LateUpdate()
        {
            Present(ViewCamera.DistanceTo(transform.position));
        }

        /// <summary>
        /// Throw the dust for every foot that has landed since the last call, at a given camera
        /// distance (NaN = no camera: full bursts). Returns the puffs emitted.
        /// </summary>
        public int Present(float cameraDistance)
        {
            if (locomotion == null || cloud == null || locomotion.StepCount == seenStep) return 0;
            seenStep = locomotion.StepCount;

            int puffs = Mathf.RoundToInt(puffsPerFootfall *
                                         MonowheelPresentationMath.LodFactor(cameraDistance, lodNear, lodFar));
            if (puffs <= 0) return 0;
            // Emit adds nothing to a system that is not playing: one never played (a preview scene,
            // an edit-mode test) or one stopped by whatever last cleared it.
            if (!cloud.isPlaying) cloud.Play();

            int emitted = 0;
            for (int i = 0; i < locomotion.Footfalls.Count; i++)
                emitted += Burst(locomotion.Footfalls[i], puffs);
            return emitted;
        }

        private int Burst(in Footfall footfall, int puffs)
        {
            float radius = footfall.FootprintRadius;
            Vector3 normal = footfall.Normal;
            // The ring starts from the machine's heading laid on the ground: a walker is never on its side.
            Vector3 along = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
            float start = Random.value * 360f;
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < puffs; i++)
            {
                // Evenly round the sole, jittered, so a burst never reads as a regular star.
                float angle = start + (i + Random.value * ringJitter) * 360f / puffs;
                Vector3 outward = Quaternion.AngleAxis(angle, normal) * along;
                emit.position = footfall.Point + outward * radius + normal * (radius * liftPerFootRadius);
                emit.velocity = (outward + normal * upwardTilt).normalized * (throwSpeedPerFootRadius * radius);
                emit.startSize = sizePerFootRadius * radius * 2f * (1f + Random.Range(-sizeJitter, sizeJitter));
                cloud.Emit(emit, 1);
            }
            return puffs;
        }
    }
}
