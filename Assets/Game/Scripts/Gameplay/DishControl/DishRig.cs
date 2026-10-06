using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where the satellite dish points, which every machine in the session agrees about.
    ///
    /// <para>
    /// Azimuth and elevation are server-decided and replicated in two <see cref="NetworkVariable{T}"/>s,
    /// so a late joiner spawns looking at the dish where it is. The server runs the motor
    /// (<see cref="DishSlew"/>) from the command the operator's <see cref="DishConsole"/> forwards; every
    /// other machine follows the replicated angles at the motor's own speed, so a dish seen from a client
    /// turns as smoothly as on the host rather than stepping at the network tick.
    /// </para>
    /// <para>
    /// The angles are the source model's: azimuth 0..360 about the tower's vertical, elevation in degrees
    /// above the horizon (the elevation pivot's rest is 50 degrees about its own axis at elevation 40,
    /// which is what <see cref="restElevation"/> records). Each pivot turns about a local axis from its
    /// authored rest pose, so nothing here depends on how the FBX import converted the empties' axes.
    /// </para>
    /// <para>
    /// <see cref="IPersistentEntity"/> because a dish has none of the components
    /// <c>SaveablePolicy.NeedsSaving</c> otherwise looks for, and where a player left it pointing is
    /// world state; <c>DishRigSaveable</c> keeps it.
    /// </para>
    /// </summary>
    public sealed class DishRig : NetworkBehaviour, IPersistentEntity
    {
        [Header("Pivots")]
        [Tooltip("Turns the whole head about the tower's vertical.")]
        [SerializeField] private Transform azimuthPivot;

        [Tooltip("Tilts the dish. A child of the azimuth pivot.")]
        [SerializeField] private Transform elevationPivot;

        [Tooltip("The azimuth pivot's turning axis in its own local space, signed so that a positive azimuth turns the way the source rig does.")]
        [SerializeField] private Vector3 azimuthAxis = Vector3.up;

        [Tooltip("The elevation pivot's tilting axis in its own local space, signed so that a positive elevation raises the dish.")]
        [SerializeField] private Vector3 elevationAxis = Vector3.right;

        [Header("Angles, degrees")]
        [Tooltip("The azimuth the model was authored at.")]
        [SerializeField] private float restAzimuth;

        [Tooltip("The elevation the model was authored at.")]
        [SerializeField] private float restElevation = 40f;

        [Tooltip("Where a new world's dish points, degrees above the horizon: nearly straight up, so the " +
                 "transmitter on its feed horn is out of reach until somebody turns the dish down from the " +
                 "control room. Clamped to the elevation motor's limits. Azimuth starts at its rest.")]
        [SerializeField] private float startElevation = 85f;

        [Header("Motors")]
        [SerializeField] private SlewMotor azimuthMotor = new() { MaxSpeed = 6f, Acceleration = 4f, Wraps = true };

        [Tooltip("Below about 14 degrees the dish's hanging beams strike the shack roof.")]
        [SerializeField] private SlewMotor elevationMotor = new() { MaxSpeed = 4f, Acceleration = 3f, Min = 15f, Max = 85f };

        [Tooltip("How much faster than the motor a peer may close on the replicated angle, to catch up after a hitch.")]
        [SerializeField, Min(1f)] private float followCatchUp = 2f;

        [Header("Audio")]
        [Tooltip("Degrees per second below which the motor counts as stopped and its hum is silenced.")]
        [SerializeField, Min(0f)] private float motorAudibleSpeed = 0.2f;

        [SerializeField] private SfxId motorLoopId = SfxId.DishMotorLoop;
        [SerializeField] private EventReference motorLoopOverride;

        private readonly NetworkVariable<float> networkAzimuth = new();
        private readonly NetworkVariable<float> networkElevation = new();

        private readonly LoopingEmitter motorLoop = new();

        private Quaternion azimuthRest;
        private Quaternion elevationRest;
        private SlewAxis azimuth;
        private SlewAxis elevation;
        private Vector2 command;
        private bool spawned;

        /// <summary>Degrees, 0..360, as this machine shows it.</summary>
        public float Azimuth => azimuth.Angle;

        /// <summary>Degrees above the horizon, as this machine shows it.</summary>
        public float Elevation => elevation.Angle;

        public float MinElevation => elevationMotor.Min;
        public float MaxElevation => elevationMotor.Max;

        /// <summary>True while either axis is turning, on this machine.</summary>
        public bool IsSlewing { get; private set; }

        /// <summary>
        /// The operator's command, x = azimuth, y = elevation, each -1..1. Server only: the console
        /// forwards it there after checking who sent it.
        /// </summary>
        public void Drive(Vector2 input)
        {
            if (!Network.Simulates(this)) return;
            command = Vector2.Max(Vector2.one * -1f, Vector2.Min(Vector2.one, input));
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay. Lands the dish on the
        /// angles at once — a world that greets the player by slewing its dish back is a world
        /// replaying its own past.
        /// </summary>
        public void RestoreAngles(float azimuthDegrees, float elevationDegrees)
        {
            if (!Network.Simulates(this)) return;

            command = Vector2.zero;
            azimuth = new SlewAxis(DishSlew.Clamp(azimuthDegrees, azimuthMotor));
            elevation = new SlewAxis(DishSlew.Clamp(elevationDegrees, elevationMotor));
            Publish();
            ApplyPose();
        }

        /// <summary>The starting angles: what a dish nobody has touched stands at, and what no save record means.</summary>
        public bool IsAtStart(float tolerance) =>
            Mathf.Abs(Mathf.DeltaAngle(Azimuth, restAzimuth)) <= tolerance &&
            Mathf.Abs(Elevation - StartElevation) <= tolerance;

        public float StartAzimuth => restAzimuth;
        public float StartElevation => DishSlew.Clamp(startElevation, elevationMotor);

        private void Awake()
        {
            // In Awake: the pose is measured from the authored pivots, and a replicated value can
            // arrive before Start.
            if (azimuthPivot != null) azimuthRest = azimuthPivot.localRotation;
            if (elevationPivot != null) elevationRest = elevationPivot.localRotation;

            azimuth = new SlewAxis(DishSlew.Clamp(restAzimuth, azimuthMotor));
            elevation = new SlewAxis(StartElevation);
            ApplyPose();
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;

            if (IsServer)
            {
                Publish();
                return;
            }

            // A joiner lands on the dish where it is, rather than watching it slew there from rest.
            azimuth = new SlewAxis(networkAzimuth.Value);
            elevation = new SlewAxis(networkElevation.Value);
            ApplyPose();
        }

        public override void OnNetworkDespawn() => spawned = false;

        private void OnDisable()
        {
            motorLoop.Stop(true);
            IsSlewing = false;
        }

        public override void OnDestroy()
        {
            motorLoop.Stop(false);
            base.OnDestroy();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float wasAzimuth = azimuth.Angle;
            float wasElevation = elevation.Angle;

            if (Network.Simulates(this))
            {
                azimuth = DishSlew.Step(azimuth, command.x, dt, azimuthMotor);
                elevation = DishSlew.Step(elevation, command.y, dt, elevationMotor);
                Publish();
            }
            else
            {
                Follow(dt);
            }

            float speed = dt > 0f
                ? Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(wasAzimuth, azimuth.Angle)),
                            Mathf.Abs(elevation.Angle - wasElevation)) / dt
                : 0f;

            ApplyPose();
            UpdateMotorSound(speed > motorAudibleSpeed);
        }

        /// <summary>A peer closes on the server's angles at a bounded rate, never snapping.</summary>
        private void Follow(float dt)
        {
            azimuth.Angle = Mathf.Repeat(Mathf.MoveTowardsAngle(azimuth.Angle, networkAzimuth.Value,
                                         azimuthMotor.MaxSpeed * followCatchUp * dt), DishSlew.FullTurn);
            elevation.Angle = Mathf.MoveTowards(elevation.Angle, networkElevation.Value,
                                                elevationMotor.MaxSpeed * followCatchUp * dt);
        }

        private void Publish()
        {
            if (!spawned || !IsServer) return;

            // Written only on a change, so a dish at rest costs no bandwidth.
            if (!Mathf.Approximately(networkAzimuth.Value, azimuth.Angle)) networkAzimuth.Value = azimuth.Angle;
            if (!Mathf.Approximately(networkElevation.Value, elevation.Angle)) networkElevation.Value = elevation.Angle;
        }

        private void ApplyPose()
        {
            if (azimuthPivot != null)
                azimuthPivot.localRotation = azimuthRest *
                    Quaternion.AngleAxis(Mathf.DeltaAngle(restAzimuth, azimuth.Angle), azimuthAxis);

            if (elevationPivot != null)
                elevationPivot.localRotation = elevationRest *
                    Quaternion.AngleAxis(elevation.Angle - restElevation, elevationAxis);
        }

        private void UpdateMotorSound(bool turning)
        {
            IsSlewing = turning;

            if (turning)
                motorLoop.Play(motorLoopId, elevationPivot != null ? elevationPivot.gameObject : gameObject, motorLoopOverride);
            else
                motorLoop.Stop(true);
        }
    }
}
