// Poses a monowheel's art on every machine: rolls it into its turns, tips it about the hub so the
// ski rests on the sand, and swings the helm -- the panel hinged on the tail post -- with the turn.
//
// It reads nothing but how the root transform actually moved since the last frame -- yaw rate and
// speed along the heading -- and where the ground is under the wheel and the ski, so the owner
// driving it, an NPC driving it and a client watching the replicated copy all show the same pose,
// with no message and no saved state. The roll numbers are the sibling MonowheelMotor's Settings,
// so the roll and the handling are tuned in one place; the motor may be disabled here (NetAuthority
// switches it off on a non-owner), its Settings still read. The ski and helm come from the art's
// MonowheelPresentation, which the builder measured.
//
// Only the Body child is posed. The physics root and its colliders stay upright: the pitch and roll
// are what the art shows, not how the vehicle collides. The seats hang under Body, so riders tip with it.
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
        [Tooltip("The nested art root to pose. Rolled and pitched, never yawed.")]
        [SerializeField] private Transform body;
        [Tooltip("How quickly the roll follows the turn, 1/s. Higher is snappier; lower hides a jittery replicated transform.")]
        [SerializeField] private float leanFollow = 6f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (spawn, load, teleport), not motion -- " +
                 "the same rule MonowheelPresentation uses.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Ski on the sand")]
        [Tooltip("The most the chassis tips about the hub either way (degrees) to keep the ski on the ground.")]
        [SerializeField] private float maxPitch = 30f;
        [Tooltip("How quickly the pitch follows the ground, 1/s.")]
        [SerializeField] private float pitchFollow = 5f;
        [Tooltip("How far above a probe point (m) the ground probe starts, so a rise in front is still found.")]
        [SerializeField] private float probeAbove = 3f;
        [Tooltip("How far the ground probe reaches down (m). Beyond it the ski is in the air and the chassis levels out.")]
        [SerializeField] private float probeReach = 8f;

        [Header("Helm")]
        [Tooltip("Degrees the helm swings about its tail post per degree of chassis roll: its weight goes to the inside of the turn.")]
        [SerializeField] private float helmSwingPerLean = 2f;
        [Tooltip("The most the helm swings either way (degrees).")]
        [SerializeField] private float maxHelmSwing = 40f;
        [Tooltip("How quickly the helm follows the turn, 1/s.")]
        [SerializeField] private float helmFollow = 4f;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private MonowheelMotor motor;
        private MonowheelPresentation art;
        private Vector3 lastPosition;
        private float lastYaw;
        private float lean;
        private float pitch;
        private float helmSwing;

        // The Body's authored pose. Roll and pitch are applied on top of it, so an art prefab nested
        // with any rest rotation keeps it.
        private Quaternion restRotation = Quaternion.identity;
        private Vector3 restPosition;
        private Vector3 hub;       // Body space: the pitch pivot
        private Vector3 contact;   // Body space: where the wheel meets the sand

        private Quaternion helmRest;
        private Vector3 helmRestPosition;
        private Vector3 helmHinge, helmAxis;   // in the helm's parent space

        private void Awake()
        {
            motor = GetComponent<MonowheelMotor>();
            if (!body) return;
            restRotation = body.localRotation;
            restPosition = body.localPosition;
            art = body.GetComponent<MonowheelPresentation>();
            if (art == null || art.Wheels.Count == 0) return;

            foreach (MonowheelWheel wheel in art.Wheels)
            {
                hub += wheel.localHub;
                contact += wheel.localContact;
            }
            hub /= art.Wheels.Count;
            contact /= art.Wheels.Count;

            Transform helm = art.Helm;
            if (helm == null) return;
            helmRest = helm.localRotation;
            helmRestPosition = helm.localPosition;
            Transform parent = helm.parent;
            helmHinge = parent.InverseTransformPoint(body.TransformPoint(art.HelmHinge));
            helmAxis = parent.InverseTransformDirection(body.TransformDirection(art.HelmAxis)).normalized;
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

        private void LateUpdate() => Pose(Time.deltaTime);

        /// <summary>One frame of chassis pose. Public so an EditMode test, which gets no LateUpdate, can step it.</summary>
        public void Pose(float dt)
        {
            if (dt <= 0f || !body) return;
            if (!motor) Awake();

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

            lean = Mathf.Lerp(lean, MonowheelDrive.Lean(speed, yawRate, motor.Settings), Follow(leanFollow, dt));
            pitch = Mathf.Lerp(pitch, SkiPitchTarget(), Follow(pitchFollow, dt));

            // Pitch about the hub, roll about the Body's origin. Lean > 0 is a right turn (yaw rate > 0
            // turns +Z toward +X); Euler(0, 0, +a) tips the Body's up toward -X, so the roll is negated
            // to lean INTO the turn. A positive pitch about +X tips the nose down.
            Quaternion pitchRotation = Quaternion.Euler(pitch, 0f, 0f);
            body.localRotation = restRotation * pitchRotation * Quaternion.Euler(0f, 0f, -lean);
            body.localPosition = restPosition + restRotation * (hub - pitchRotation * hub);

            PoseHelm(dt);
        }

        // Where the ground is under the wheel and under the ski, probed from the UNPITCHED pose so the
        // answer does not chase its own tilt. No ski, or no ground under either, levels the chassis.
        private float SkiPitchTarget()
        {
            if (art == null || !art.HasSki) return 0f;

            Vector3 up = transform.up, forward = transform.forward;
            Vector3 hubWorld = RestPoint(hub), contactWorld = RestPoint(contact), skiWorld = RestPoint(art.SkiLowPoint);
            if (!Ground(contactWorld, up, out float groundAtContact) || !Ground(skiWorld, up, out float groundAtSki))
                return 0f;

            MonowheelPoseMath.GroundUnderHub(Vector3.Dot(contactWorld, forward), groundAtContact,
                                             Vector3.Dot(skiWorld, forward), groundAtSki,
                                             Vector3.Dot(hubWorld, forward), Vector3.Dot(hubWorld, up),
                                             out float slope, out float clearance);
            Vector3 toSki = skiWorld - hubWorld;
            var hubToSki = new Vector2(Vector3.Dot(toSki, forward), Vector3.Dot(toSki, up));
            return Mathf.Clamp(MonowheelPoseMath.SkiPitch(hubToSki, clearance, slope), -maxPitch, maxPitch);
        }

        private Vector3 RestPoint(Vector3 bodyPoint) =>
            transform.TransformPoint(restPosition + restRotation * bodyPoint);

        private bool Ground(Vector3 point, Vector3 up, out float height)
        {
            height = 0f;
            if (!MonowheelGround.TryHit(gameObject, transform, point + up * probeAbove, -up, probeAbove + probeReach,
                                        art.GroundLayers, hits, out RaycastHit hit))
                return false;
            height = Vector3.Dot(hit.point, up);
            return true;
        }

        // The helm panel hangs behind its tail post; in a turn it swings about the post so its weight
        // goes to the inside of the turn. Lean > 0 is a right turn, and a positive turn about the
        // upright post carries a point BEHIND it toward -X, so the swing is negated.
        private void PoseHelm(float dt)
        {
            if (art == null || art.Helm == null) return;

            float target = Mathf.Clamp(lean * helmSwingPerLean, -maxHelmSwing, maxHelmSwing);
            helmSwing = Mathf.Lerp(helmSwing, target, Follow(helmFollow, dt));

            Quaternion q = Quaternion.AngleAxis(-helmSwing, helmAxis);
            art.Helm.localRotation = q * helmRest;
            art.Helm.localPosition = helmHinge + q * (helmRestPosition - helmHinge);
        }

        private static float Follow(float rate, float dt) => 1f - Mathf.Exp(-rate * dt);

        private void OnValidate()
        {
            leanFollow = Mathf.Max(0f, leanFollow);
            maxPlausibleSpeed = Mathf.Max(0.1f, maxPlausibleSpeed);
            maxPitch = Mathf.Clamp(maxPitch, 0f, 89f);
            pitchFollow = Mathf.Max(0f, pitchFollow);
            probeAbove = Mathf.Max(0f, probeAbove);
            probeReach = Mathf.Max(0.1f, probeReach);
            maxHelmSwing = Mathf.Max(0f, maxHelmSwing);
            helmFollow = Mathf.Max(0f, helmFollow);
        }
    }
}
