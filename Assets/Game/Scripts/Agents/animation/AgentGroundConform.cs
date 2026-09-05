// Sitting an agent on the ground instead of on the NavMesh, and leaning it into the slope.
//
// One probe, two answers, and they are delivered to different places for a reason.
//
// HEIGHT goes to NavMeshAgent.baseOffset, which is the one vertical knob the agent will not fight:
// writing transform.position while updatePosition is on drags the agent's own internal position
// with it. It is applied only where the motor is running -- NetAuthority switches the motor and
// the NavMeshAgent off on every remote copy -- and reaches the other machines the way every other
// bit of the agent's position does, through the replicated transform. Correcting it locally as
// well would apply it twice.
//
// TILT goes to the body's visual root, on EVERY machine, because it is presentation: no rotation
// of a child transform is replicated, and recomputing it locally costs one probe and keeps a
// watching client's creature leaning the same way the host's does.
//
// Why tilt matters at all: height alone gets the soles touching, and on the dunes a rigidly
// vertical body with both feet at one height still reads as pasted on. Leaning into the slope is
// what makes it look like standing on it.
using SpaceGame.Locomotion;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Conforms a NavMesh-driven agent to the real ground under it. Add alongside
    /// <see cref="NavMeshAgentMotor"/>; wired onto every agent prefab by
    /// <c>Tools ▸ SpaceGame ▸ Agents ▸ Wire Ground Conform</c>.
    /// </summary>
    // After the agent's other LateUpdates, so AgentAnimatorDriver has already had its say about the
    // pose this frame. The Animator itself always runs before any LateUpdate, so the animated
    // rotation this reads is the finished one.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentGroundConform : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Left empty, resolved in Initialise from the NavMeshAgent on this object.")]
        [SerializeField] private NavMeshAgent agent;

        [Tooltip("Left empty, resolved in Initialise. Without a motor the height correction is " +
                 "skipped and only the slope tilt runs.")]
        [SerializeField] private NavMeshAgentMotor motor;

        [Tooltip("The child transform the slope tilt is written to. Left empty, resolved to the " +
                 "direct child the skin hangs from. Never the agent root: its yaw belongs to " +
                 "navigation, and tilting it would tilt the collider and the NavMeshAgent with it.")]
        [SerializeField] private Transform bodyRoot;

        [Header("Ground probe")]
        [Tooltip("Layers counted as ground. Anything under its own physics is rejected whatever " +
                 "its layer, so a player standing next to the agent is never read as floor.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the body each ray starts, so ground higher than the pivot is still " +
                 "found. Sized from the worst measured NavMesh error (0.60 m) with a little over. " +
                 "Keep it small: a ray starts above the body and looks down, so an overhang inside " +
                 "this band is read as the ground the body should be standing on.")]
        [SerializeField, Min(0f)] private float probeStartHeight = 0.6f;

        [Tooltip("How far down each ray looks from its start height.")]
        [SerializeField, Min(0.5f)] private float probeDistance = 8f;

        [Tooltip("Radius of the probe ring. 0 uses the NavMeshAgent's own radius, which is what " +
                 "the body's footprint is defined as everywhere else.")]
        [SerializeField, Min(0f)] private float footprintRadius = 0f;

        [Header("Height")]
        [Tooltip("Distance from this prefab's pivot to its soles. Every agent prefab in this " +
                 "project is authored soles-at-pivot, so this is 0 unless the model says otherwise.")]
        [SerializeField] private float soleOffset = 0f;

        [Tooltip("Cap on the correction, metres, either way. The largest NavMesh error measured in " +
                 "the world is 0.60 m; the cap is what stops a bad probe teleporting the body.")]
        [SerializeField, Min(0f)] private float maxCorrection = 1f;

        [Tooltip("How fast the height follows the ground, 1/seconds. High enough to keep up with a " +
                 "running creature, low enough that a ray catching a rock edge does not step it.")]
        [SerializeField, Min(0f)] private float heightFollowSpeed = 12f;

        [Header("Slope")]
        [SerializeField] private bool alignToSlope = true;

        [Tooltip("How much of the ground's tilt the body takes on. A many-legged body can lie " +
                 "almost flat on the hillside because every foot is on it; a walking biped keeps " +
                 "its torso near upright and takes the slope up in its legs, so it wants much " +
                 "less. Nine of the ten agents here are bipeds, which is why the default is low.")]
        [SerializeField, Range(0f, 1f)] private float slopeFollow = 0.35f;

        [Tooltip("Cap on the lean in degrees. The world bakes walkable ground up to 60 degrees and " +
                 "a body leaned that far has fallen over.")]
        [SerializeField, Min(0f)] private float maxTiltDegrees = 30f;

        [SerializeField, Min(0f)] private float tiltFollowSpeed = 8f;

        private WalkerGround ground;
        private AgentGrounding grounding;
        private bool initialised;

        private float FootprintRadius => footprintRadius > 0f
            ? footprintRadius
            : agent != null ? Mathf.Max(0.1f, agent.radius) : 0.5f;

        private AgentGroundingSettings Settings => new AgentGroundingSettings
        {
            SoleOffset = soleOffset,
            MaxCorrection = maxCorrection,
            HeightFollowSpeed = heightFollowSpeed,
            SlopeFollow = alignToSlope ? slopeFollow : 0f,
            MaxTiltDegrees = maxTiltDegrees,
            TiltFollowSpeed = tiltFollowSpeed,
        };

        /// <summary>
        /// Resolve references and build the sampler. Public and separate from Awake so an EditMode
        /// test can assemble the whole thing without a running player loop, the way
        /// <c>DesertCrawlerLocomotion.Initialise</c> does.
        /// </summary>
        public void Initialise()
        {
            if (initialised) return;
            initialised = true;

            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!motor) motor = GetComponent<NavMeshAgentMotor>();
            if (!bodyRoot) bodyRoot = ResolveBodyRoot();

            if (!bodyRoot && alignToSlope)
            {
                Debug.LogWarning(
                    $"{name}: no visual root found to lean, so this agent will be put on the " +
                    "ground but will not follow the slope. Assign bodyRoot on the prefab.", this);
            }

            ground = new WalkerGround(transform, groundMask, probeStartHeight, probeDistance);
            grounding = new AgentGrounding(bodyRoot ? bodyRoot.localRotation : Quaternion.identity);
        }

        private void Awake() => Initialise();

        private void OnEnable()
        {
            Initialise();

            // Respawn, chunk stream-in, save restore: each puts the body somewhere new, and easing
            // across from the last correction would show it sliding into place.
            grounding.Reset();
        }

        private void OnDisable()
        {
            if (motor) motor.GroundOffset = 0f;

            // Back to the authored pose, not to the last lean we wrote. On the rigs where nothing
            // animates this node -- the Nomad, the PatrolRobots, the Vrescal -- leaving the lean
            // behind would freeze a dead or streamed-out body at whatever angle the last hillside
            // it stood on happened to be.
            if (bodyRoot && grounding != null) bodyRoot.localRotation = grounding.RestBodyRotation;
        }

        private void LateUpdate() => Conform(Time.deltaTime);

        /// <summary>
        /// One frame of conforming. Public and dt-driven so it can be stepped from a test.
        /// </summary>
        public void Conform(float deltaTime)
        {
            Initialise();

            bool grounded = ground.TrySurface(transform.position, FootprintRadius,
                                              float.NegativeInfinity, out WalkerSurface surface);

            // A leap runs with updatePosition off and the body driven along an arc by hand. The
            // ground under a body mid-arc is not the ground it is standing on, and conforming to it
            // would flatten the leap.
            if (motor != null && motor.IsLeaping) grounded = false;

            // NetAuthority disables the motor and the NavMeshAgent on every remote copy. There the
            // height already arrived inside the replicated transform, and correcting it again here
            // would apply it twice.
            bool drivesHeight = motor != null && motor.isActiveAndEnabled;

            Vector3 localNormal = grounded
                ? transform.InverseTransformDirection(surface.Normal)
                : Vector3.up;

            grounding.Step(grounded,
                           drivesHeight ? motor.NavSurfaceY : transform.position.y,
                           grounded ? surface.Point.y : 0f,
                           localNormal,
                           bodyRoot ? bodyRoot.localRotation : Quaternion.identity,
                           Settings,
                           deltaTime);

            if (drivesHeight) motor.GroundOffset = grounding.HeightOffset;
            if (bodyRoot && alignToSlope) bodyRoot.localRotation = grounding.BodyRotation;
        }

        /// <summary>
        /// The direct child of this agent that the visuals hang from, and the node the lean is
        /// written to. Null when nothing suitable was found, in which case only the height runs.
        /// </summary>
        public Transform BodyRoot => bodyRoot;

        /// <summary>
        /// The direct child of this agent that the visuals actually hang from.
        ///
        /// <para>
        /// Not the agent root, whose yaw navigation owns and whose collider must stay upright, and
        /// not an arbitrary renderer's own transform: a SkinnedMeshRenderer deforms to its BONES,
        /// so tilting the object holding that renderer moves nothing. Walking up from the root bone
        /// to the child of this agent lands on <c>Model</c> for the Nomad and BountyHunter,
        /// <c>Armature</c> for the PatrolRobots and DeathmatchBot, and <c>Arm_DuneRat</c> /
        /// <c>vrescal</c> for two of the creatures.
        /// </para>
        /// <para>
        /// Three probes, because one rig defeats each of the first two. The Golem is assembled from
        /// rigid parts, so it has no SkinnedMeshRenderer to ask at all, and its Animator sits on the
        /// agent root rather than on a child — both of the obvious answers come back empty, and the
        /// node actually wanted is <c>Bone_Root</c>, which only the renderers point at. Falling back
        /// to a plain <see cref="Renderer"/> is what covers a construct like that; without it the
        /// Golem is put on the ground correctly and then never leans, with one warning to say so.
        /// </para>
        /// </summary>
        private Transform ResolveBodyRoot()
        {
            var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform node = skin != null ? skin.rootBone : null;

            if (node == null)
            {
                var animator = GetComponentInChildren<Animator>(true);
                if (animator != null && animator.transform != transform) node = animator.transform;
            }

            if (node == null)
            {
                var renderer = GetComponentInChildren<Renderer>(true);
                if (renderer != null) node = renderer.transform;
            }

            while (node != null && node.parent != null && node.parent != transform)
                node = node.parent;

            return node != null && node != transform ? node : null;
        }

        private void OnValidate()
        {
            probeDistance = Mathf.Max(probeStartHeight + 0.5f, probeDistance);
        }
    }
}
