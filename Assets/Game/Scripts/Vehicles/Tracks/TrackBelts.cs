// A tracked hull's running gear, moving: every link of each track steps round its loop and every
// wheel on that track turns, at the speed that side of the hull is seen to move over the ground. So
// the ground run lies still on the sand while the hull rolls over it, and a hull turning on the spot
// runs its two sides in opposite directions (GDC-L1-ANIM-0004: the gear follows the code-driven
// motion, never the other way round; GDC-L1-ANIM-0005).
//
// Each side's speed is read off its own ground contacts' transforms (GroundSpeedGauge, as the track
// dust reads them), so the host, a client watching the replicated hull and a test all present alike,
// with no message and no saved state. The links are separate meshes baked in place round the loop:
// a link is moved by the rigid step from its own slot to the slot it has travelled to, so grousers,
// pads and chevrons keep their order as they circulate. Nothing is written beyond `animateDistance`
// from the camera (GDC-L1-TECH-0002). Built by TrackBeltWiring.
using System;
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public sealed class TrackBelts : MonoBehaviour
    {
        /// <summary>One closed track and the wheels it runs on. Every pose is in the links' (or wheel's) parent space.</summary>
        [Serializable]
        public sealed class Belt
        {
            [Tooltip("True for the track on the hull's left (-x) side.")]
            public bool left;
            [Tooltip("Where the track's ground run starts and ends; their midpoint's speed is the belt's.")]
            public Transform frontContact;
            public Transform rearContact;
            [Tooltip("Metres of belt between two slots, in world space at the built scale.")]
            public float pitch = 1f;

            [Tooltip("Slot k's pose round the loop, in order; along the ground run the order heads for the rear.")]
            public Vector3[] slotPositions = new Vector3[0];
            public Quaternion[] slotRotations = new Quaternion[0];

            [Tooltip("The link baked into slot k, and its pose relative to that slot's frame.")]
            public Transform[] links = new Transform[0];
            public Vector3[] linkOffsetPositions = new Vector3[0];
            public Quaternion[] linkOffsetRotations = new Quaternion[0];

            [Tooltip("Every wheel the track runs round, spun about its axle.")]
            public Transform[] wheels = new Transform[0];
            public Quaternion[] wheelRestRotations = new Quaternion[0];
            public Vector3[] wheelAxles = new Vector3[0];
            [Tooltip("Wheel radius, m in world space at the built scale.")]
            public float[] wheelRadii = new float[0];
        }

        [SerializeField] private Belt[] belts = new Belt[0];

        [Header("Speed")]
        [Tooltip("Seconds over which each side's speed is smoothed, so a replicated pose does not judder the belt.")]
        [SerializeField] private float speedSmoothing = 0.3f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (a load, a correction), not motion.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;
        [Tooltip("Below this belt speed (m/s) the gear is left where it is.")]
        [SerializeField] private float stillSpeed = 0.02f;

        [Header("Distance LOD")]
        [Tooltip("The gear moves only within this camera distance (m); beyond it nobody can see a link.")]
        [SerializeField] private float animateDistance = 120f;

        private GroundSpeedGauge[] gauges = new GroundSpeedGauge[0];
        private float[] phases = new float[0];
        private float[][] wheelAngles = new float[0][];

        public int BeltCount => belts.Length;
        public bool IsLeft(int belt) => belts[belt].left;
        public Transform[] LinksOf(int belt) => belts[belt].links;
        public Transform[] WheelsOf(int belt) => belts[belt].wheels;
        /// <summary>The belt's smoothed ground speed, m/s; positive when that side rolls forward.</summary>
        public float SpeedOf(int belt) => gauges.Length == belts.Length ? gauges[belt].Speed : 0f;

        /// <summary>Builder only.</summary>
        public void Configure(Belt[] builtBelts) => belts = builtBelts;

        private void OnValidate()
        {
            speedSmoothing = Mathf.Max(0f, speedSmoothing);
            maxPlausibleSpeed = Mathf.Max(0.01f, maxPlausibleSpeed);
            stillSpeed = Mathf.Max(0f, stillSpeed);
            animateDistance = Mathf.Max(0f, animateDistance);
        }

        /// <summary>Forget each side's motion: after a spawn, a load or any snap into place. The gear stays where it is.</summary>
        public void ResetBaseline()
        {
            if (gauges.Length != belts.Length)
            {
                gauges = new GroundSpeedGauge[belts.Length];
                phases = new float[belts.Length];
                wheelAngles = new float[belts.Length][];
                for (int b = 0; b < belts.Length; b++) wheelAngles[b] = new float[belts[b].wheels.Length];
            }
            for (int b = 0; b < belts.Length; b++) gauges[b].Reset(Midpoint(belts[b]));
        }

        private void OnEnable() => ResetBaseline();

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            Present(Time.deltaTime, cam == null ? float.NaN : Vector3.Distance(cam.transform.position, transform.position));
        }

        /// <summary>One frame at a given camera distance (NaN = no camera: animate).</summary>
        public void Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return;
            if (gauges.Length != belts.Length) ResetBaseline();

            bool seen = float.IsNaN(cameraDistance) || cameraDistance <= animateDistance;
            Vector3 forward = transform.forward;
            for (int b = 0; b < belts.Length; b++)
            {
                Belt belt = belts[b];
                // Measured even out of sight, so the belt is already at speed when the camera comes near.
                float speed = gauges[b].Measure(Midpoint(belt), forward, dt, speedSmoothing, maxPlausibleSpeed);
                if (!seen || Mathf.Abs(speed) < stillSpeed) continue;

                phases[b] = Mathf.Repeat(phases[b] + speed * dt / belt.pitch, belt.links.Length);
                PlaceLinks(belt, phases[b]);
                SpinWheels(belt, wheelAngles[b], speed, dt);
            }
        }

        private static Vector3 Midpoint(Belt belt) => (belt.frontContact.position + belt.rearContact.position) * 0.5f;

        /// <summary>The link baked into slot i now sits <paramref name="phase"/> slots further round the loop.</summary>
        private static void PlaceLinks(Belt belt, float phase)
        {
            int count = belt.links.Length;
            for (int i = 0; i < count; i++)
            {
                float along = i + phase;
                int from = (int)along;
                float t = along - from;
                from %= count;
                int to = (from + 1) % count;

                Vector3 slot = Vector3.LerpUnclamped(belt.slotPositions[from], belt.slotPositions[to], t);
                Quaternion turn = Quaternion.SlerpUnclamped(belt.slotRotations[from], belt.slotRotations[to], t);
                belt.links[i].SetLocalPositionAndRotation(slot + turn * belt.linkOffsetPositions[i],
                                                          turn * belt.linkOffsetRotations[i]);
            }
        }

        private static void SpinWheels(Belt belt, float[] angles, float speed, float dt)
        {
            for (int w = 0; w < belt.wheels.Length; w++)
            {
                angles[w] = Mathf.Repeat(angles[w] + MonowheelPresentationMath.SpinDegrees(speed, belt.wheelRadii[w], dt), 360f);
                belt.wheels[w].localRotation = Quaternion.AngleAxis(angles[w], belt.wheelAxles[w]) * belt.wheelRestRotations[w];
            }
        }
    }
}
