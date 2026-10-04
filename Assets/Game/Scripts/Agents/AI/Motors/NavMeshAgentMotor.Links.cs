// Crossing off-mesh links: ladders and jumps.
//
// A NavMeshAgent crosses a link by itself only as a straight slide at walking speed. That is wrong
// for every link this game has -- an agent that "walks" up a six-metre ladder, or slides across a gap
// in a line -- so autoTraverseOffMeshLink is switched off and the agent stops at the start of a link
// and waits to be told it has crossed. This file is that telling.
//
// The shape is the leap's, exactly (see NavMeshAgentMotor.Carry.cs for the same borrowing): navigation
// off, the transform driven by hand, agent.Warp onto the mesh at the far end. A jump IS a leap -- the
// link hands over to BeginLeap -- and a ladder or a plain link is the same driven body moving along
// waypoints instead of an arc.
//
// Server-authoritative like the rest of the motor: Tick only runs where the agent is simulated, and the
// transform that comes out is what the NetworkTransform replicates. Nothing here is on the wire.
//
// A fourth kind is gated: a link whose owner is an INavLinkGate (an airlock) hands the crossing to
// the gate. The agent stands at the link's start, ridden by the motor like any other link, and the gate says
// each tick whether to wait or walk on, and when it is across.
//
// Nothing is saved. A jump in flight comes back through the leap's saved state; a ladder ride is not
// saved, so an agent loaded mid-ladder is put on the nearest mesh by DeferredNavMeshWarp and plans again.
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public partial class NavMeshAgentMotor
    {
        // A ladder ride is three legs: onto the climbing line in front of the rungs, along it, and off.
        private const int MaxRideLegs = 3;

        [Header("Off-Mesh Links")]
        [Tooltip("Whether this agent plans routes over ladders. Off for anything that cannot climb: " +
                 "it is then never sent to one, instead of standing at the foot of it.")]
        [SerializeField] private bool usesLadders = true;

        [Tooltip("Metres per second up a ladder.")]
        [SerializeField, Min(0.1f)] private float ladderClimbSpeed = 1.6f;

        [Tooltip("Metres per second down a ladder.")]
        [SerializeField, Min(0.1f)] private float ladderDescendSpeed = 2f;

        [Tooltip("Metres from the rung line to the body's centre while on the ladder. The same figure " +
                 "the player's LadderClimber uses.")]
        [SerializeField, Min(0f)] private float ladderStandoff = 0.65f;

        [Tooltip("Metres per second for the steps onto and off a ladder.")]
        [SerializeField, Min(0.1f)] private float ladderStepSpeed = 1.6f;

        [Tooltip("Metres per second along a link that is neither a ladder nor a jump.")]
        [SerializeField, Min(0.1f)] private float plainLinkSpeed = 3f;

        [Tooltip("Metres per second along a jump link, measured straight from takeoff to landing.")]
        [SerializeField, Min(0.1f)] private float jumpLinkSpeed = 5f;

        [Tooltip("Height of a jump's arc above the straight line from takeoff to landing, however short.")]
        [SerializeField, Min(0f)] private float jumpLinkArcHeight = 0.8f;

        [Tooltip("Extra arc height per metre of horizontal distance, so a long gap is cleared higher.")]
        [SerializeField, Min(0f)] private float jumpLinkArcPerMetre = 0.25f;

        private bool ridingLink;
        private bool ridingLadder;
        private INavLinkGate gate;
        private Vector3 gateDestination;
        private int rideLeg;
        private int rideLegCount;
        private Vector3 rideBody;
        private Vector3 rideFacing;
        private readonly Vector3[] rideTargets = new Vector3[MaxRideLegs];
        private readonly float[] rideSpeeds = new float[MaxRideLegs];

        /// <summary>
        /// Is this body being walked along a ladder or a link by hand? Read by <c>AgentGroundConform</c>
        /// for the reason it reads <see cref="IsLeaping"/>: the ground under a climber is not the ground
        /// it is standing on.
        /// </summary>
        public bool IsRidingLink => ridingLink;

        /// <summary>Is this body on a ladder right now: the climb pose, not the walk, is what it should show.</summary>
        public bool IsClimbingLadder => ridingLink && ridingLadder;

        private void ConfigureLinkTraversal()
        {
            agent.autoTraverseOffMeshLink = false;
            if (!usesLadders)
                agent.areaMask = NavLinkAreas.Without(agent.areaMask, NavLinkAreas.Ladder);
        }

        // Called on the first tick the agent stands at the start of a link.
        private void BeginLinkTraversal()
        {
            OffMeshLinkData link = agent.currentOffMeshLinkData;
            if (!link.valid) return;

            if (link.owner is Ladder ladder && ladder.IsValid)
            {
                BeginLadderRide(ladder, link);
            }
            else if (link.owner is INavLinkGate linkGate)
            {
                BeginGatedRide(linkGate, link);
            }
            else if (IsPlainLink(link))
            {
                BeginRide(link.endPos, link.startPos, 1);
                rideTargets[0] = link.endPos;
                rideSpeeds[0] = plainLinkSpeed;
            }
            else
            {
                BeginJump(link);
            }
        }

        // A link somebody authored as an ordinary walkway, by area on a NavMeshLink component. Every
        // other link -- Jump area, a baked drop or a link a baker registered without a component -- is
        // a gap, and is crossed as one.
        private static bool IsPlainLink(in OffMeshLinkData link) =>
            link.owner is NavMeshLink component && component.area != NavLinkAreas.Jump;

        // The gate decides everything from here: the body stays at the link's start until it says walk.
        private void BeginGatedRide(INavLinkGate linkGate, in OffMeshLinkData link)
        {
            BeginRide(link.endPos, link.startPos, 0);
            gate = linkGate;
            gateDestination = link.endPos;
        }

        private void BeginLadderRide(Ladder ladder, in OffMeshLinkData link)
        {
            Vector3 line = ladder.ClimbLine(ladderStandoff);
            bool up = link.endPos.y > link.startPos.y;

            BeginRide(link.endPos, link.startPos, MaxRideLegs);
            ridingLadder = true;
            rideFacing = -ladder.TowardClimber;

            rideTargets[0] = new Vector3(line.x, link.startPos.y, line.z);
            rideSpeeds[0] = ladderStepSpeed;
            rideTargets[1] = new Vector3(line.x, link.endPos.y, line.z);
            rideSpeeds[1] = up ? ladderClimbSpeed : ladderDescendSpeed;
            rideTargets[2] = link.endPos;
            rideSpeeds[2] = ladderStepSpeed;
        }

        // Takes the agent off its link and puts the body in the motor's hands. The agent is told the
        // link is crossed up front, which moves ITS position to the far end; updatePosition is off by
        // then, so the transform stays where the body is and the leg targets carry it there.
        private void BeginRide(Vector3 end, Vector3 start, int legs)
        {
            rideBody = agent.nextPosition;
            rideLeg = 0;
            rideLegCount = legs;
            ridingLink = true;
            ridingLadder = false;

            Vector3 toEnd = end - start;
            toEnd.y = 0f;
            rideFacing = toEnd.sqrMagnitude > 1e-4f ? toEnd.normalized : transform.forward;

            HandOverToMotor();
        }

        private void HandOverToMotor()
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.CompleteOffMeshLink();
            agent.ResetPath();
            agent.isStopped = true;
        }

        private void BeginJump(in OffMeshLinkData link)
        {
            Vector3 flat = link.endPos - transform.position;
            flat.y = 0f;
            float duration = Vector3.Distance(transform.position, link.endPos) / jumpLinkSpeed;

            agent.updatePosition = false;
            agent.CompleteOffMeshLink();
            BeginLeap(link.endPos, jumpLinkArcHeight + flat.magnitude * jumpLinkArcPerMetre, duration);
        }

        private void AdvanceGatedRide(float deltaTime)
        {
            LinkGateOrder order = gate.Direct(this, rideBody, gateDestination);
            if (!ridingLink) return;   // the gate teleported the body, which ended the ride

            if (order.Step == LinkGateStep.Done)
            {
                gate = null;
                ridingLink = false;
                LandAt(gateDestination);
                return;
            }

            if (order.Step == LinkGateStep.Walk)
            {
                rideBody = Vector3.MoveTowards(rideBody, order.Target, order.Speed * deltaTime);
                Vector3 heading = order.Target - rideBody;
                heading.y = 0f;
                if (heading.sqrMagnitude > 1e-4f) rideFacing = heading.normalized;
            }

            transform.position = rideBody + Vector3.up * agent.baseOffset;
            FacePosition(transform.position + rideFacing, deltaTime);
        }

        private void AdvanceLinkRide(float deltaTime)
        {
            if (gate != null)
            {
                AdvanceGatedRide(deltaTime);
                return;
            }

            Vector3 target = rideTargets[rideLeg];
            rideBody = Vector3.MoveTowards(rideBody, target, rideSpeeds[rideLeg] * deltaTime);
            transform.position = rideBody + Vector3.up * agent.baseOffset;
            FacePosition(transform.position + rideFacing, deltaTime);

            if (rideBody != target) return;

            rideLeg++;
            if (rideLeg < rideLegCount) return;

            ridingLink = false;
            LandAt(rideBody);
        }

        // The body is no longer ridden: it was disabled, teleported or handed to another machine.
        // The agent gets its flags back and sorts the position out itself.
        private void AbandonLinkRide()
        {
            if (!ridingLink) return;

            ridingLink = false;
            gate?.Release(this);
            gate = null;
            if (!agent) return;

            agent.updatePosition = defaultUpdatePosition;
            agent.updateRotation = defaultUpdateRotation;
        }
    }
}
