// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs
using SpaceGame.Core;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// Spins a monowheel's rings and makes them throw sand, lay a lingering dust cloud and smoke
    /// from their hubs — from nothing but how the root actually moves (spec §2). It runs on every
    /// machine and reads only the transform it already sees, so a client watching a replicated
    /// monowheel, a player driving one and an NPC driving one all present the same way, with no
    /// message and no saved state. The Strider MonowheelMotor's "presentation" is this component.
    ///
    /// <para>The sand is feedback on a real event — the paddles biting the ground
    /// (GDC-L1-FEEL-0004) — so it follows actual speed and actual ground contact. The dust is the
    /// expensive part, so every wheel is capped and the effect fades with camera distance
    /// (GDC-L1-TECH-0002, GDC-L1-PERF-0004).</para>
    /// </summary>
    public sealed class MonowheelPresentation : MonoBehaviour, IDustLodBand
    {
        [Tooltip("Measured by MonowheelPresentationBuilder. One entry per ring.")]
        [SerializeField] private MonowheelWheel[] wheels = new MonowheelWheel[0];

        [Header("Measured parts the chassis pose uses (MonowheelLean)")]
        [Tooltip("Whether this variant has a ski out front. Without one the chassis is never pitched.")]
        [SerializeField] private bool hasSki;
        [Tooltip("The lowest point of the ski's runners, in root space: what is brought down onto the sand.")]
        [SerializeField] private Vector3 skiLowPoint;
        [Tooltip("The helm: the panel hinged on the tail post, the part furthest back. Swung with the rider's weight.")]
        [SerializeField] private Transform helm;
        [Tooltip("A point on the tail post's axis, in root space: what the helm turns about.")]
        [SerializeField] private Vector3 helmHinge;
        [Tooltip("The tail post's axis, in root space.")]
        [SerializeField] private Vector3 helmAxis = Vector3.up;

        [Header("Speed")]
        [Tooltip("Ground speed (m/s) at which sand and smoke reach their maximum.")]
        [SerializeField] private float fullSpeed = 20f;
        [Tooltip("Seconds over which speed is smoothed, so a jittery replicated transform does not flicker the effects.")]
        [SerializeField] private float speedSmoothing = 0.15f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap, not motion. About twice the fastest monowheel's top speed.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Ground")]
        [Tooltip("What counts as ground for the paddles to throw.")]
        [SerializeField] private LayerMask groundLayers = 1 << 0;   // Builder sets Default + Ground.
        [Tooltip("How far below the contact point (m) ground still counts as touching.")]
        [SerializeField] private float groundProbe = 0.6f;

        [Header("Emission per wheel (particles/s)")]
        [SerializeField] private float sprayAtFullSpeed = 150f;
        [SerializeField] private float dustAtFullSpeed = 20f;
        [SerializeField] private float smokeIdle = 2f;
        [SerializeField] private float smokeAtFullSpeed = 12f;

        [Header("Distance LOD")]
        [Tooltip("Full effects up to this camera distance (m).")]
        [SerializeField] private float lodNear = 60f;
        [Tooltip("No sand, dust or smoke beyond this camera distance (m). Spin never stops.")]
        [SerializeField] private float lodFar = 150f;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private Vector3 lastPosition;
        private float speed;

        public IReadOnlyList<MonowheelWheel> Wheels => wheels;
        public float Speed => speed;
        public bool HasSki => hasSki;
        public Vector3 SkiLowPoint => skiLowPoint;
        public Transform Helm => helm;
        public Vector3 HelmHinge => helmHinge;
        public Vector3 HelmAxis => helmAxis;
        public float DustAtFullSpeed => dustAtFullSpeed;
        public float LodNear => lodNear;
        public float LodFar => lodFar;
        public LayerMask GroundLayers => groundLayers;

        /// <summary>Builder only: install the measured wheels and parts. <paramref name="ski"/> is null on a variant without one.</summary>
        public void Configure(MonowheelWheel[] measured, Vector3? ski, Transform helmPanel, Vector3 hinge, Vector3 axis)
        {
            wheels = measured;
            hasSki = ski.HasValue;
            skiLowPoint = ski ?? Vector3.zero;
            helm = helmPanel;
            helmHinge = hinge;
            helmAxis = axis.normalized;
            groundLayers = LayerMask.GetMask("Default", "Ground");
        }

        /// <summary>Forget the last position — after a spawn, a load or any snap into place.</summary>
        public void ResetBaseline()
        {
            lastPosition = transform.position;
            speed = 0f;
        }

        private void OnEnable() => ResetBaseline();

        private void Update() => Present(Time.deltaTime);

        /// <summary>One frame of presentation, LOD'd against the camera drawing this machine's view.</summary>
        public void Present(float dt) => Present(dt, ViewCamera.DistanceTo(transform.position));

        /// <summary>
        /// One frame at a given camera distance (<c>float.NaN</c> = no camera, full effect). The
        /// builder's self-check passes NaN: in the editor, the view camera may be a game camera far
        /// away, and the LOD would silence the very effects the check is looking for.
        /// </summary>
        public void Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            speed = MonowheelPresentationMath.StepSpeed(speed, lastPosition, position, transform.forward,
                                                        dt, speedSmoothing, maxPlausibleSpeed, out _);
            lastPosition = position;

            float fraction = MonowheelPresentationMath.SpeedFraction(speed, fullSpeed);
            float lod = MonowheelPresentationMath.LodFactor(cameraDistance, lodNear, lodFar);

            foreach (MonowheelWheel wheel in wheels)
            {
                wheel.ringBone.Rotate(wheel.localAxle,
                                      MonowheelPresentationMath.SpinDegrees(speed, wheel.paddleRadius, dt),
                                      Space.Self);

                bool grounded = Touching(transform.TransformPoint(wheel.localContact));
                float ground = grounded ? lod : 0f;
                SetRate(wheel.spray, MonowheelPresentationMath.Rate(0f, sprayAtFullSpeed, fraction) * ground);
                SetRate(wheel.dust, MonowheelPresentationMath.Rate(0f, dustAtFullSpeed, fraction) * ground);
                SetRate(wheel.smoke, MonowheelPresentationMath.Rate(smokeIdle, smokeAtFullSpeed, fraction) * lod);
            }
        }


        private bool Touching(Vector3 contact)
        {
            Vector3 up = transform.up;
            return MonowheelGround.TryHit(gameObject, transform, contact + up * groundProbe, -up, groundProbe * 2f,
                                          groundLayers, hits, out _);
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;
        }
    }
}
