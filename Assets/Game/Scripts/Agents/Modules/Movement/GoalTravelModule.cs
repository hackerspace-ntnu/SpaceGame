// Walks to whatever AgentGoal currently says, and nothing else.
//
// Sits one step above Fallback on purpose. Above wander and patrol, so an agent with somewhere to
// be goes there instead of milling about; below everything reactive, so a fight, a flee or a
// formation order preempts the journey without the journey needing to know they exist.
//
// Returning null once the goal is reached rather than holding position is the other half of that:
// it hands the frame down to WanderModule, so an agent that has arrived somewhere pokes around
// inside the site instead of standing to attention on the exact coordinate. That is why "dwelling
// at a destination" needs no code anywhere.
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

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback + 1);

        // Existing prefabs recompiled against this file would otherwise keep a serialized 0 and tie
        // with WanderModule, and AgentController's tie-break is component order — which would make
        // whether an agent travels or wanders depend on which was dragged on first.
        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Fallback + 1);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
            stopDistanceMargin = Mathf.Max(0f, stopDistanceMargin);
        }

        public override string ModuleDescription =>
            "Walks to the destination held by AgentGoal. Set that goal from NpcTaskModule, a cutscene, " +
            "or any script — this module only executes it.\n\n" +
            "Yields the frame (returns null) once the goal is reached, so WanderModule takes over and " +
            "the agent mills about inside the destination instead of standing on the exact point.\n\n" +
            "• speedMultiplier — locomotion speed while travelling\n" +
            "• run — run rather than walk\n" +
            "• Priority sits one above Fallback: beats wander/patrol, loses to everything reactive";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentGoal goal = context.Goal;

            if (goal == null || !goal.HasGoal || goal.HasArrived)
                return null;

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
