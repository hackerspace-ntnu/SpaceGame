// Sand churned up where a track or a wheel meets the ground, laid as a lingering cloud behind the
// machine: the Strider dune barges' tracks today (the monowheels have their own MonowheelPresentation,
// which also spins rings and throws spray). One DustCloudRecipe cloud per contact, built by DustWiring.
//
// Each contact's rate follows how fast THAT POINT is seen to move over the ground -- read off its own
// transform (GroundSpeedGauge, shared with TrackBelts) -- so a hull turning on the spot dusts at
// its track ends and a standing one does not, and the host, a client watching the replicated hull and
// a hull driven by its brain all present alike, with no message and no saved state
// (GDC-L1-FEEL-0004). A contact off the ground (a ridge crest, a drop) throws nothing.
//
// The cap per cloud is fixed at build time and the rate fades out with camera distance
// (GDC-L1-TECH-0002).
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public sealed class RollingDust : MonoBehaviour
    {
        [Tooltip("Where the machine meets the ground, at ground level. One per cloud.")]
        [SerializeField] private Transform[] contacts = new Transform[0];
        [Tooltip("One lingering cloud per contact, in the same order.")]
        [SerializeField] private ParticleSystem[] clouds = new ParticleSystem[0];

        [Header("Speed")]
        [Tooltip("Ground speed (m/s) of a contact at which its dust reaches the full rate: the machine's cruise speed.")]
        [SerializeField] private float fullSpeed = 4f;
        [Tooltip("Seconds over which each contact's speed is smoothed, so a replicated pose does not flicker it.")]
        [SerializeField] private float speedSmoothing = 0.3f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (a load, a correction), not motion.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Emission per contact (puffs/s)")]
        [SerializeField] private float rateAtFullSpeed = 3f;

        [Header("Ground")]
        [Tooltip("What counts as ground for the tracks to churn.")]
        [SerializeField] private LayerMask groundLayers = 1 << 0;   // Builder sets Default + Ground.
        [Tooltip("How far above or below a contact (m) ground still counts as touching.")]
        [SerializeField] private float groundProbe = 0.6f;

        [Header("Distance LOD")]
        [Tooltip("Full dust up to this camera distance (m).")]
        [SerializeField] private float lodNear = 80f;
        [Tooltip("No dust beyond this camera distance (m).")]
        [SerializeField] private float lodFar = 200f;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private GroundSpeedGauge[] gauges = new GroundSpeedGauge[0];

        public int ContactCount => contacts.Length;
        public Transform Contact(int i) => contacts[i];
        public ParticleSystem CloudAt(int i) => clouds[i];
        public float RateAtFullSpeed => rateAtFullSpeed;

        /// <summary>Builder only: the contacts, their clouds, and the speed and rate at which they peak.</summary>
        public void Configure(Transform[] groundContacts, ParticleSystem[] contactClouds, float cruiseSpeed, float peakRate)
        {
            contacts = groundContacts;
            clouds = contactClouds;
            fullSpeed = cruiseSpeed;
            rateAtFullSpeed = peakRate;
            groundLayers = LayerMask.GetMask("Default", "Ground");
        }

        private void OnValidate()
        {
            fullSpeed = Mathf.Max(0.01f, fullSpeed);
            speedSmoothing = Mathf.Max(0f, speedSmoothing);
            rateAtFullSpeed = Mathf.Max(0f, rateAtFullSpeed);
            lodFar = Mathf.Max(lodNear + 1f, lodFar);
        }

        /// <summary>Forget where every contact was: after a spawn, a load or any snap into place.</summary>
        public void ResetBaseline()
        {
            if (gauges.Length != contacts.Length) gauges = new GroundSpeedGauge[contacts.Length];
            for (int i = 0; i < contacts.Length; i++) gauges[i].Reset(contacts[i].position);
        }

        private void OnEnable() => ResetBaseline();

        private void Update()
        {
            Camera cam = Camera.main;
            Present(Time.deltaTime, cam == null ? float.NaN : Vector3.Distance(cam.transform.position, transform.position));
        }

        /// <summary>One frame at a given camera distance (NaN = no camera: full dust).</summary>
        public void Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return;
            if (gauges.Length != contacts.Length) ResetBaseline();

            float lod = MonowheelPresentationMath.LodFactor(cameraDistance, lodNear, lodFar);
            Vector3 up = transform.up;
            for (int i = 0; i < contacts.Length; i++)
            {
                Vector3 position = contacts[i].position;
                // Measured along its own step: a contact's speed over the ground whichever way it goes.
                float speed = gauges[i].MeasureAlongStep(position, dt, speedSmoothing, maxPlausibleSpeed);

                bool grounded = MonowheelGround.TryHit(gameObject, transform, position + up * groundProbe, -up,
                                                       groundProbe * 2f, groundLayers, hits, out _);
                float fraction = MonowheelPresentationMath.SpeedFraction(speed, fullSpeed);
                ParticleSystem.EmissionModule emission = clouds[i].emission;
                emission.rateOverTime = grounded ? MonowheelPresentationMath.Rate(0f, rateAtFullSpeed, fraction) * lod : 0f;
            }
        }
    }
}
