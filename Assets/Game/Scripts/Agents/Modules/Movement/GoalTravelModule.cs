// Walks to whatever AgentGoal currently says, and nothing else.
//
// Sits one step above Fallback on purpose. Above wander and patrol, so an agent with somewhere to
// be goes there instead of milling about; below everything reactive, so a fight, a flee or a
// formation order preempts the journey without the journey needing to know they exist.
//
// Returning null once an ordinary goal is reached rather than holding position is the other half of
// that: it hands the frame down to WanderModule, so an agent that has arrived somewhere pokes around
// inside the site instead of standing to attention on the exact coordinate. That is why "dwelling
// at a destination" needs no code anywhere.
//
// A HOLDING goal (AgentGoal.HoldOnArrival) is the exception: a guard at a gate, a vendor at a stall.
// Arrived, it keeps the frame and stands, facing the goal's FacePoint. It only lets go beyond the
// arrive radius plus a release margin — hysteresis, so a nudge from a passer-by does not toggle
// walking and standing every frame.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class GoalTravelModule : BehaviourModuleBase
    {
        [Header("Travel")]
        [SerializeField] private float speedMultiplier = 1f;

        [Tooltip("Run rather than walk while travelling. Long hauls generally look better walked; " +
                 "turn this on for anything chasing a lead.")]
        [SerializeField] private bool run = false;

        [Tooltip("Extra margin added to the goal's own arrive radius before the motor is told to " +
                 "stop. Kept small — the goal's radius is the real arrival test.")]
        [SerializeField] private float stopDistanceMargin = 0.5f;

        [Tooltip("A holding agent walks back only once pushed this far beyond the goal's arrive " +
                 "radius. Smaller and a shove sets it pacing; larger and it stands visibly off its spot.")]
        [SerializeField] private float holdReleaseMargin = 2f;

        [Tooltip("With no face point, a holding agent looks this far ahead along its arrival heading.")]
        [SerializeField] private float holdLookAhead = 2f;

        [Header("Exact Stand")]
        [Tooltip("An exact-stand goal's last step: metres to stop short of the point. Small, so the body ends on it.")]
        [SerializeField, Min(0.01f)] private float alignStopDistance = 0.02f;

        [Tooltip("Speed multiplier for that last step: unhurried, a person settling into place.")]
        [SerializeField, Min(0.05f)] private float alignSpeedMultiplier = 0.5f;

        [Tooltip("Once aligned, a body is nudged back onto its point only after being pushed this far off it.")]
        [SerializeField, Min(0.05f)] private float alignReleaseMargin = 0.3f;

        [Tooltip("Seconds of trying before an unreachable exact point (blocked by a prop or a person) is stood near instead.")]
        [SerializeField, Min(0.5f)] private float alignGiveUpSeconds = 4f;

        // Holding is a property of THIS goal: a new destination, even a near one, is walked to.
        private bool holding;
        private Vector3 heldGoal;
        private bool aligned;
        private bool alignGaveUp;
        private float alignSeconds;

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback + 1);

        // Existing prefabs recompiled against this file would otherwise keep a serialized 0 and tie
        // with WanderModule, and AgentController's tie-break is component order — which would make
        // whether an agent travels or wanders depend on which was dragged on first.
        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Fallback + 1);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
            stopDistanceMargin = Mathf.Max(0f, stopDistanceMargin);
            holdReleaseMargin = Mathf.Max(AgentGoal.HoldArriveSlack, holdReleaseMargin);
            holdLookAhead = Mathf.Max(0.1f, holdLookAhead);
            alignStopDistance = Mathf.Max(0.01f, alignStopDistance);
            alignSpeedMultiplier = Mathf.Max(0.05f, alignSpeedMultiplier);
            alignReleaseMargin = Mathf.Max(0.05f, alignReleaseMargin);
            alignGiveUpSeconds = Mathf.Max(0.5f, alignGiveUpSeconds);
        }

        public override string ModuleDescription =>
            "Walks to the destination held by AgentGoal. Set that goal from NpcTaskModule, a cutscene, " +
            "or any script — this module only executes it.\n\n" +
            "An ordinary goal: yields the frame (returns null) once reached, so WanderModule takes over " +
            "and the agent mills about inside the destination.\n" +
            "A holding goal (AgentGoal.Set(..., hold: true, facePoint)): stands on the spot facing the " +
            "face point, and walks back only once pushed beyond the arrive radius + holdReleaseMargin.\n\n" +
            "• speedMultiplier — locomotion speed while travelling\n" +
            "• run — run rather than walk\n" +
            "• holdReleaseMargin / holdLookAhead — hysteresis and default gaze of a holding goal\n" +
            "• Priority sits one above Fallback: beats wander/patrol, loses to everything reactive";

        /// <summary>
        /// Should an agent <paramref name="distance"/> from a holding goal stand? Arriving takes
        /// <see cref="AgentGoal.ArrivalRadius"/>; once standing, only going beyond the arrive radius
        /// plus <paramref name="releaseMargin"/> lets go. Pure, for the tests.
        /// </summary>
        public static bool HoldsPosition(bool wasHolding, float distance, float arriveRadius, float releaseMargin)
        {
            float limit = wasHolding ? arriveRadius + releaseMargin : AgentGoal.ArrivalRadius(arriveRadius, true);
            return distance <= limit;
        }

        /// <summary>
        /// Should a body <paramref name="distance"/> from an exact-stand point still be walking onto it?
        /// Aligned within <see cref="AgentGoal.AlignedWithin"/>, then stays put until pushed
        /// <c>alignReleaseMargin</c> off; gives up after <c>alignGiveUpSeconds</c> so a point somebody stands
        /// on cannot make a resident shuffle for ever.
        /// </summary>
        private bool NeedsAlignment(float distance, float deltaTime)
        {
            if (alignGaveUp) return false;

            aligned = distance <= AgentGoal.AlignedWithin || (aligned && distance <= AgentGoal.AlignedWithin + alignReleaseMargin);
            alignSeconds = aligned ? 0f : alignSeconds + deltaTime;
            alignGaveUp = alignSeconds > alignGiveUpSeconds;
            return !aligned && !alignGaveUp;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentGoal goal = context.Goal;

            if (goal == null || !goal.HasGoal)
            {
                holding = false;
                return null;
            }

            if (goal.HoldOnArrival)
            {
                if (goal.Position != heldGoal)
                {
                    holding = false;
                    aligned = alignGaveUp = false;
                    alignSeconds = 0f;
                }

                holding = HoldsPosition(holding, goal.DistanceToGoal, goal.ArriveRadius, holdReleaseMargin);
                heldGoal = goal.Position;

                if (holding && goal.ExactStand && NeedsAlignment(goal.DistanceToGoal, deltaTime))
                    return MoveIntent.MoveTo(goal.Position, alignStopDistance, alignSpeedMultiplier * goal.SpeedMultiplier);

                if (holding)
                    return MoveIntent.StopAndFace(goal.FacePoint ?? context.Position + context.Self.forward * holdLookAhead);
            }
            else
            {
                holding = false;

                if (goal.HasArrived)
                    return null;
            }

            // The goal's multiplier compounds with this module's, rather than replacing it: this one
            // says how fast this AGENT travels, the goal's says how urgent this ERRAND is, and both
            // are true at once.
            return MoveIntent.MoveTo(
                goal.Position,
                goal.ArriveRadius + stopDistanceMargin,
                speedMultiplier * goal.SpeedMultiplier,
                isRunning: run);
        }
    }
}
