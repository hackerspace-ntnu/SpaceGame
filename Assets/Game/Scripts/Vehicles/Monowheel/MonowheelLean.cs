// Rolls a monowheel's art into its turns, on every machine.
//
// It reads nothing but how the root transform actually moved since the last frame -- yaw rate and
// speed along the heading -- so the owner driving it, an NPC driving it and a client watching the
// replicated copy all show the same lean, with no message and no saved state. The numbers are the
// sibling MonowheelMotor's Settings, so the roll and the handling are tuned in one place; the motor
// may be disabled here (NetAuthority switches it off on a non-owner), its Settings still read.
//
// Only the Body child is rolled, about its own forward axis. Never yaw or pitch it: the art's
// presentation measures speed along the Body's forward, and the physics root's pose is the motor's.
//
// NetAuthority leaves this running on clients because SimulationDrivers.Discover only collects
// AgentControllers and IMovementMotors; a plain presentation MonoBehaviour is never a driver.
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [RequireComponent(typeof(MonowheelMotor))]
    public sealed class MonowheelLean : MonoBehaviour, ITeleportAware
    {
        [Tooltip("The nested art root to roll. Rolled only, never yawed or pitched.")]
        [SerializeField] private Transform body;
        [Tooltip("How quickly the roll follows the turn, 1/s. Higher is snappier; lower hides a jittery replicated transform.")]
        [SerializeField] private float leanFollow = 6f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (spawn, load, teleport), not motion -- " +
                 "the same rule MonowheelPresentation uses.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        private MonowheelMotor motor;
        private Vector3 lastPosition;
        private float lastYaw;
        private float lean;

        // The Body's authored pose. The roll is applied on top of it, so an art prefab nested with
        // any rest rotation keeps it.
        private Quaternion restRotation = Quaternion.identity;

        private void Awake()
        {
            motor = GetComponent<MonowheelMotor>();
            if (body) restRotation = body.localRotation;
        }

        private void OnEnable() => ResetBaseline();

        private void ResetBaseline()
        {
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
        }

        // A teleport that turns the vehicle without moving it far slips under the speed snap rule and
        // would read as one enormous yaw rate -- a full-lean flick on arrival.
        public void OnTeleported(in TeleportMove move) => ResetBaseline();

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !body) return;

            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            Vector3 moved = position - lastPosition;
            moved.y = 0f;

            if (moved.magnitude / dt > maxPlausibleSpeed)
            {
                ResetBaseline();
                return;
            }

            // Signed along the heading, so reversing through a turn leans the way a wheel would.
            float speed = Vector3.Dot(moved, transform.forward) / dt;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastPosition = position;
            lastYaw = yaw;

            float target = MonowheelDrive.Lean(speed, yawRate, motor.Settings);
            lean = Mathf.Lerp(lean, target, 1f - Mathf.Exp(-leanFollow * dt));

            // Lean > 0 is a right turn (yaw rate > 0 turns +Z toward +X). Euler(0, 0, +a) tips the
            // Body's up toward -X, so the roll is negated to lean INTO the turn.
            body.localRotation = restRotation * Quaternion.Euler(0f, 0f, -lean);
        }

        private void OnValidate()
        {
            leanFollow = Mathf.Max(0f, leanFollow);
            maxPlausibleSpeed = Mathf.Max(0.1f, maxPlausibleSpeed);
        }
    }
}
