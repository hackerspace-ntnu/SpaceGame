using UnityEngine;
using SpaceGame.Persistence;

using SpaceGame.Teleporting;
namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// Moves the craft. Sole owner of the hull transform — the same rule
    /// <c>SpiderWalkerLocomotion</c> follows, and for the same reason: two things writing one
    /// transform fight every frame.
    ///
    /// Each frame it asks the rig what the sails made, turns that into velocity, asks the foil
    /// where the wheel is pointing it and how high to sit, and writes one pose.
    ///
    /// There is no throttle. The wind is the engine, the two sheets and the mast cant are how you
    /// tune it, and the wheel points the one thing this craft has in the sand. A rig that makes no
    /// drive makes no speed, and a craft with no way on has almost no steerage — both of those are
    /// load-bearing, not flavour.
    ///
    /// Transform-driven rather than physics-driven, so a rider standing on the deck needs
    /// <c>WalkerPlatformCarrier</c> to come along with it.
    ///
    /// <see cref="IPersistentEntity"/> for that same reason. Being transform-driven means this craft
    /// has no Rigidbody on its root at all and no AgentController either, so it matched none of the
    /// clauses the save policy used to infer "this moves" — the DuneFoil was the one vehicle in the
    /// world that no amount of component sniffing could have found. Sailing it somewhere and reloading
    /// put it back at its authored berth.
    /// </summary>
    [DefaultExecutionOrder(100)]    // after SailRig (50), before WalkerPlatformCarrier (200)
    [RequireComponent(typeof(SailRig))]
    [RequireComponent(typeof(FoilLift))]
    public class DuneFoilLocomotion : MonoBehaviour, IPersistentEntity, ITeleportAware
    {
        [Header("Mass")]
        [Tooltip("Craft mass in kg. Sail force divided by this is acceleration, so it sets how " +
                 "quickly the rig powers the hull up.")]
        [SerializeField, Min(1f)] private float mass = 1800f;

        [Header("Hull")]
        [Tooltip("Top speed the hull will reach however hard it is driven, m/s.")]
        [SerializeField, Min(1f)] private float maxSpeed = 45f;

        [Tooltip("Resistance that does not come from the sand: air on the hull, rig windage.")]
        [SerializeField, Min(0f)] private float parasiticDrag = 0.008f;

        [Tooltip("How hard the foil refuses leeway, per second. Higher means the craft tracks " +
                 "its heading more exactly and slides sideways less.")]
        [SerializeField, Min(0.1f)] private float leewayResistance = 8f;

        [Header("Steering")]
        [Tooltip("How quickly the hull takes up the turn the foil is asking for. This is the " +
                 "craft's yaw inertia expressed as a response rate: 1.2 means about eight tenths " +
                 "of a second to settle into a turn, which is what a seventeen-metre hull feels " +
                 "like.")]
        [SerializeField, Min(0.1f)] private float yawResponse = 1.2f;

        [Tooltip("Hard cap on turn rate, deg/s. A craft this size should never spin.")]
        [SerializeField, Min(1f)] private float maxTurnRate = 26f;

        [Tooltip("Sail torque needed to bias the turn by one degree per second, N·m. This is what " +
                 "makes the rig a second helm: a main sheeted in hard rounds the craft up into " +
                 "the wind and makes the wheel heavy, and easing it lets the bow fall off.")]
        [SerializeField, Min(1f)] private float yawResistance = 2600f;

        [Header("Heel and trim")]
        [Tooltip("Righting moment, N·m per degree of heel. Larger means a stiffer craft.")]
        [SerializeField, Min(1f)] private float rightingMoment = 1500f;

        [Tooltip("Most the craft will lean, degrees. Kept modest because the deck is a walking " +
                 "surface with a player standing on it, not a picture.")]
        [SerializeField, Range(0f, 45f)] private float maxHeel = 16f;

        [Tooltip("How quickly it rolls to the heel the sails call for.")]
        [SerializeField, Min(0.1f)] private float heelSpeed = 1.8f;

        [Tooltip("Degrees of bow-down pitch per m/s² of acceleration.")]
        [SerializeField] private float pitchPerAcceleration = 0.6f;

        [Tooltip("Most it will pitch, degrees.")]
        [SerializeField, Range(0f, 20f)] private float maxPitch = 6f;

        [SerializeField, Min(0.1f)] private float pitchSpeed = 2.5f;

        [Header("Terrain")]
        [Tooltip("Also lean the hull to follow the slope of the dune underneath. Off by " +
                 "default: at full ride height the craft is flying and should stay level.")]
        [SerializeField] private bool followGroundSlope;

        [Header("Robustness")]
        [Tooltip("Longest frame this model will integrate, seconds. A chunk streaming in can " +
                 "hand out a half-second frame, and a craft doing forty metres a second would " +
                 "cross twenty metres of dune in one step — through the terrain, past the ground " +
                 "probe, and out the other side. Slowing down beats teleporting.")]
        [SerializeField, Range(0.02f, 0.2f)] private float maxTimeStep = 0.05f;

        /// <summary>
        /// Hold the hull where it is: the rig still draws and the sails still trim, but none of it
        /// is allowed to move the craft.
        ///
        /// The sails are shipped set, so without this a parked craft sails itself over the horizon
        /// on the frame the scene loads. Driven from outside — <c>DuneFoilMooring</c> lifts it the
        /// moment somebody is standing on the deck — because who is aboard is a question this
        /// assembly cannot see the answer to.
        /// </summary>
        public bool HoldStation { get; set; }

        /// <summary>
        /// Turn the craft's way on with the craft.
        ///
        /// <c>velocity</c> is in WORLD space and the hull is driven from it every frame
        /// (<c>transform.position + Flatten(velocity) * dt</c>), so a teleport that turns the craft
        /// leaves it sailing the compass bearing it had in the room it left — across the new one,
        /// sideways to its own hull, with the rig still drawing. The position itself needs nothing:
        /// this craft integrates FROM its transform rather than from a stored path, so a teleport of
        /// the transform is a teleport of the craft.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            velocity = move.Direction(velocity);
        }

        private SailRig rig;
        private FoilLift foil;
        private FoilRudder rudder;

        private Vector3 velocity;
        private float yawRate;
        private float heel;
        private float pitch;
        private float smoothedAcceleration;
        private float lastSpeed;
        private bool helmDriven;

        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity => velocity;

        /// <summary>Horizontal speed, m/s.</summary>
        public float Speed => new Vector3(velocity.x, 0f, velocity.z).magnitude;

        /// <summary>Speed along the heading, m/s. Negative is sternway.</summary>
        public float ForwardSpeed => Vector3.Dot(velocity, Heading());

        /// <summary>Speed as a fraction of the craft's maximum.</summary>
        public float Speed01 => Mathf.Clamp01(Speed / maxSpeed);

        /// <summary>Current heel, degrees. Positive to starboard.</summary>
        public float Heel => heel;

        /// <summary>Turn rate, deg/s. Positive to starboard.</summary>
        public float YawRate => yawRate;

        /// <summary>The rig driving this craft.</summary>
        public SailRig Rig => rig;

        /// <summary>The foil under it.</summary>
        public FoilLift Foil => foil;

        /// <summary>The steerable foil, or null on a craft built without one.</summary>
        public FoilRudder Rudder => rudder;

        /// <summary>Where the wheel has the blade, -1 hard a-port to +1 hard a-starboard.</summary>
        public float RudderCommand => rudder != null ? rudder.Command : 0f;

        /// <summary>True when the helm was worked since the last step.</summary>
        public bool IsHelmDriven => helmDriven;

        /// <summary>
        /// Steer the craft. Call every frame while the helm is manned; stop calling to let go, and
        /// the blade centres itself rather than leaving the craft locked in the last turn.
        ///
        /// The flag is consumed by <see cref="Step"/> rather than compared against
        /// <c>Time.frameCount</c>. A frame counter says "was this called during the current render
        /// frame", which is the same thing right up until something drives the craft at a fixed
        /// step outside the render loop — a harness, a rollback, a catch-up — and then the helm
        /// looks permanently manned and the blade never centres. Consuming a flag asks the
        /// question the code actually means: was it steered since the last time we integrated.
        /// </summary>
        public void SetRudder(float value)
        {
            Initialise();
            if (rudder != null) rudder.SetCommand(value);
            helmDriven = true;
        }

        private bool initialised;

        private void Awake() => Initialise();

        private void Initialise()
        {
            if (initialised) return;
            rig = GetComponent<SailRig>();
            foil = GetComponent<FoilLift>();
            rudder = GetComponent<FoilRudder>();
            initialised = true;
        }

        private void Update() => Step(Time.deltaTime);

        /// <summary>
        /// Advance the craft by one frame.
        ///
        /// Public and explicitly frame-driven, the same way <c>WalkerPlatformCarrier.CarryRiders</c>
        /// is and for the same reason: "does this thing sail" is not a question a screenshot can
        /// answer, and a harness that can step the whole stack — rig, foil, helm, hull — at a fixed
        /// time step can answer it in one pass, deterministically, without a play session.
        /// </summary>
        /// <param name="deltaTime">Frame time. Clamped internally; see <see cref="maxTimeStep"/>.</param>
        public void Step(float deltaTime)
        {
            Initialise();

            // One clamped step. Everything downstream — the ground probe, the trim loop, the
            // exponential smoothers — assumes a frame it can integrate in one go.
            float dt = Mathf.Min(deltaTime, maxTimeStep);
            if (dt <= 0f) return;

            Vector3 heading = Heading();

            // Resolve the sails against the velocity we came into the frame with, so the
            // apparent wind and the force it produces belong to the same instant.
            rig.Tick(velocity, heading, dt);

            if (!helmDriven && rudder != null)
            {
                // Centre the blade the moment nobody is at the wheel. Eased rather than snapped,
                // so stepping away mid-turn finishes the turn instead of jerking the hull straight.
                rudder.SetCommand(Mathf.MoveTowards(rudder.Command, 0f, dt * 1.5f));
            }
            helmDriven = false;

            if (HoldStation)
            {
                // Moored. The rig above still ran, so the sails draw and flog against the real
                // wind and the craft looks alive at its berth; it simply makes no way. Velocity is
                // zeroed rather than left to decay, so letting go does not release stored speed.
                velocity = Vector3.zero;
                yawRate = 0f;
                lastSpeed = 0f;
                smoothedAcceleration = 0f;
                // Still swung, so the wheel and the blade answer at the berth and a player can
                // see what the helm does before casting off.
                if (rudder != null) rudder.Tick(0f, foil.RideHeight01, maxTurnRate, dt);
            }
            else
            {
                IntegrateVelocity(heading, dt);
                IntegrateHeading(dt);
            }

            ApplyPose(dt);
        }

        private Vector3 Heading()
        {
            Vector3 h = SailAerodynamics.Flatten(transform.forward);
            return h.sqrMagnitude < 1e-6f ? Vector3.forward : h.normalized;
        }

        private void IntegrateVelocity(Vector3 heading, float dt)
        {
            Vector3 force = SailAerodynamics.Flatten(rig.TotalForce);
            velocity += force / mass * dt;

            // The foil is what makes sailing upwind possible: it refuses most of the sideways
            // motion, leaving the forward component of a mostly-sideways force to drive the
            // craft. Without this the sails would simply blow it to leeward.
            //
            // Decayed as a true exponential rather than by a clamped linear fraction, so the
            // leeway a player sees is the same at thirty frames a second as at a hundred and
            // forty. The old form quietly gripped harder on short frames.
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float forward = Vector3.Dot(velocity, heading);
            float lateral = Vector3.Dot(velocity, right);
            lateral *= Mathf.Exp(-leewayResistance * foil.LateralGrip() * dt);

            float speed = Mathf.Abs(forward);

            // Everything that slows the craft, gathered in one place so "it will not stop" and
            // "it will not go" are both answerable by reading four lines.
            float decel = foil.DragDeceleration(speed)          // sand, quadratic
                        + parasiticDrag * speed * speed          // air, quadratic
                        + foil.RollingDeceleration();            // packing and bearings, constant
            forward -= Mathf.Sign(forward) * Mathf.Min(Mathf.Abs(forward), decel * dt);

            // Climbing. Applied only to forward way and only ever subtracted, so a dune face takes
            // the craft's speed and leaves it stopped on the slope — it never pushes it backwards
            // and never gives it speed running down the other side.
            if (forward > 0f)
            {
                float climb = foil.ClimbDeceleration() * dt;
                forward = Mathf.Max(0f, forward - climb);
            }

            // A sand craft does not sail backwards; a back-winded sail stops it instead.
            forward = Mathf.Clamp(forward, -maxSpeed * 0.08f, maxSpeed);

            velocity = heading * forward + right * lateral;

            // And then it actually stops. Quadratic resistance approaches zero without reaching
            // it, so without this last step a craft with every sail struck creeps across the pan
            // at a few centimetres a second indefinitely — which on a transform-driven hull is
            // the whole vessel very slowly sliding, and reads as the craft being broken.
            //
            // Conditional on the rig, not just on the speed: a craft that is barely moving but
            // being properly driven is accelerating away from a standstill, and must not be
            // pinned to zero on the frame it starts.
            bool crawling = velocity.magnitude < FoilPhysics.StopThreshold;
            bool unpowered = rig.Drive / mass <= foil.RollingDeceleration();
            if (crawling && unpowered) velocity = Vector3.zero;
        }

        private void IntegrateHeading(float dt)
        {
            // Two helms, added rather than exclusive.
            //
            // The wheel points the foil, and a steered blade puts the craft on a circle whose
            // radius comes from the blade angle rather than from the speed — so the wheel is
            // something you can aim with. The rig is the other helm: sail force acting forward or
            // aft of the craft's centre of resistance biases that turn, which is why a main
            // sheeted in hard rounds the craft up and easing it lets the bow fall off.
            //
            // Written as a rate the craft settles at rather than as a torque integrated against an
            // inertia. Both give the same steady turn; only this one cannot ring. A torque model
            // needs damping tuned against the frame rate to stay quiet, and an under-damped one on
            // a craft this long reads as the hull weaving down the course under its own power —
            // which is indistinguishable, from the deck, from a bug.
            float steered = rudder != null
                ? rudder.Tick(Vector3.Dot(velocity, Heading()), foil.RideHeight01, maxTurnRate, dt)
                : 0f;

            float fromSails = rig.TotalTorque / yawResistance;

            float commanded = Mathf.Clamp(steered + fromSails, -maxTurnRate, maxTurnRate);
            yawRate = Mathf.Lerp(yawRate, commanded, 1f - Mathf.Exp(-yawResponse * dt));

            transform.Rotate(Vector3.up, yawRate * dt, Space.World);
        }

        private void ApplyPose(float dt)
        {
            float speed = Speed;

            float targetHeel = Mathf.Clamp(rig.TotalHeelMoment / rightingMoment, -maxHeel, maxHeel);
            heel = Mathf.Lerp(heel, targetHeel, 1f - Mathf.Exp(-heelSpeed * dt));

            // Acceleration is smoothed BEFORE it becomes pitch, not after.
            //
            // A raw frame-to-frame speed difference divided by the frame time is mostly noise:
            // a millimetre of numerical wobble over a four-millisecond frame reads as a quarter
            // of a g. Fed straight into the bow attitude that is a hull visibly shivering, and
            // because the deck carries its riders by its own delta, the player shivers with it.
            float rawAcceleration = (speed - lastSpeed) / Mathf.Max(dt, 1e-4f);
            lastSpeed = speed;
            smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, rawAcceleration,
                                              1f - Mathf.Exp(-6f * dt));

            float targetPitch = Mathf.Clamp(smoothedAcceleration * pitchPerAcceleration,
                                            -maxPitch, maxPitch);
            pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchSpeed * dt));

            float y = foil.Tick(speed, Heading(), dt);

            Vector3 p = transform.position + SailAerodynamics.Flatten(velocity) * dt;
            transform.position = new Vector3(p.x, y, p.z);

            // Rebuild the whole rotation from yaw + heel + pitch each frame rather than
            // accumulating: incremental roll and pitch drift, and on a craft that leans this
            // far the drift shows up as a hull slowly winding over.
            float yaw = transform.eulerAngles.y;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Quaternion lean = Quaternion.Euler(pitch, 0f, -heel);

            if (followGroundSlope && foil.HasGround)
            {
                // Only while the hull is down in the sand. Once it is up on the foil the craft
                // is flying and should hold its own attitude, not copy the dune it is over —
                // so this fades out exactly as the hull lifts clear.
                float onSand = 1f - foil.RideHeight01;
                Quaternion slope = Quaternion.FromToRotation(Vector3.up, foil.GroundNormal);
                flat = Quaternion.Slerp(Quaternion.identity, slope, onSand) * flat;
            }

            transform.rotation = flat * lean;
        }

        /// <summary>Stop the craft dead. For spawning, teleports and tests.</summary>
        public void Halt()
        {
            Initialise();
            velocity = Vector3.zero;
            yawRate = 0f;
            lastSpeed = 0f;
            smoothedAcceleration = 0f;
            if (rudder != null) rudder.Centre();
            if (foil != null) foil.Prime();
        }
    }
}
