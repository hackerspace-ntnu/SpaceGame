// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcOrnithopterWings.cs
// Feeds the wing animator and the audio on an NPC-flown craft (IOrnithopterFlightState — the seam both
// bind in Awake). Measured from the transform on EVERY machine: the server's FlyingRigidbodyMotor moves
// it, clients see the replicated pose, and both derive the same wings with nothing sent.
//
// A dynamic body moves only on physics steps, so at a frame rate above the physics rate some frames see
// no movement and the next sees two steps' worth. The measurement therefore spans every frame since the
// pose last changed (up to maxSampleGap, past which the craft reads as parked) and is low-passed.
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    [DisallowMultipleComponent]
    public class NpcOrnithopterWings : MonoBehaviour, IOrnithopterFlightState
    {
        [SerializeField] private NpcWingSettings settings = new NpcWingSettings();

        [Tooltip("Longest wait for the pose to change before the craft reads as stopped, seconds. Longer " +
                 "than a physics step or a replicated pose update, or a moving craft reads as parked.")]
        [SerializeField, Min(0.02f)] private float maxSampleGap = 0.2f;

        [Tooltip("Time constant of the low-pass on the measured velocity and turn rate, seconds.")]
        [SerializeField, Min(0.01f)] private float velocitySmoothingSeconds = 0.15f;

        private NpcWingState state;
        private Vector3 lastPosition;
        private float lastYaw;
        private float sampleGap;
        private Vector3 velocity;
        private float turnRate;
        private bool sampled;

        public float Airspeed => state.Airspeed;
        public float FlapPhase => state.FlapPhase;
        public float FlapEffort => state.FlapEffort;
        public float WingSpread => state.WingSpread;
        public float BankAngle => state.Bank;
        public float PitchInput => 0f;
        public float TurnInput => state.Turn;
        public bool IsStalled => false;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One measurement over <paramref name="dt"/>. Public so tests can drive it, like the wing animator's.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            if (!sampled)
            {
                lastPosition = position;
                lastYaw = yaw;
                sampleGap = 0f;
                sampled = true;
                return;
            }

            sampleGap += dt;
            bool moved = position != lastPosition || !Mathf.Approximately(yaw, lastYaw);
            if (moved || sampleGap >= maxSampleGap)
            {
                float blend = 1f - Mathf.Exp(-sampleGap / velocitySmoothingSeconds);
                velocity = Vector3.Lerp(velocity, (position - lastPosition) / sampleGap, blend);
                turnRate = Mathf.Lerp(turnRate, Mathf.DeltaAngle(lastYaw, yaw) / sampleGap, blend);
                lastPosition = position;
                lastYaw = yaw;
                sampleGap = 0f;
            }

            // Positive bank is right wing down, as IOrnithopterFlightState.BankAngle reads it.
            float bank = -Mathf.DeltaAngle(0f, transform.eulerAngles.z);
            state = NpcWings.Step(state, velocity, bank, turnRate, dt, settings);
        }

        private void OnEnable()
        {
            sampled = false;
            velocity = Vector3.zero;
            turnRate = 0f;
        }
    }
}
