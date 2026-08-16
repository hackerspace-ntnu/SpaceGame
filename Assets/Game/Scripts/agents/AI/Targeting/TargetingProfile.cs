// Tuning asset for AgentTargeting. One asset per agent archetype per context, so the same
// prefab can hunt cautiously in the open world and aggressively in the arena without forking it.
//
// Create via Assets > Create > Agents > Targeting Profile.
using UnityEngine;

namespace SpaceGame.Agents
{
    [CreateAssetMenu(menuName = "Agents/Targeting Profile", fileName = "NewTargetingProfile")]
    public class TargetingProfile : ScriptableObject
    {
        [Header("Who")]
        [Tooltip("Relationship a candidate must have to this agent to be considered a target.")]
        public FactionRelationship relationship = FactionRelationship.Hostile;

        [Header("Ranges")]
        [Tooltip("Candidates beyond this are never scored. Set this to comfortably exceed the " +
                 "longest weapon range on the agent, or it will never acquire something it could shoot.")]
        public float acquisitionRange = 35f;

        [Tooltip("An acquired target is dropped once it passes this distance. Must exceed " +
                 "acquisitionRange or targets flicker at the boundary.")]
        public float loseRange = 45f;

        [Header("Cadence")]
        [Tooltip("Seconds between re-scoring candidates. This is not perception latency — the held " +
                 "target's distance and visibility refresh every frame regardless.")]
        public float reevaluateInterval = 0.5f;

        [Header("Memory")]
        [Tooltip("Seconds an unseen target stays acquired before the agent gives up on it. Only " +
                 "applies when requireLineOfSight is on and a PerceptionModule is present.")]
        public float memoryDuration = 6f;

        [Header("Perception")]
        [Tooltip("Require field-of-view + line-of-sight to ACQUIRE a new target. Once acquired, " +
                 "memoryDuration governs how long losing sight is tolerated. Turn this off for " +
                 "arena modes where everyone knows where everyone is.")]
        public bool requireLineOfSightToAcquire = true;

        [Tooltip("Acquire anything inside this radius regardless of field of view, so an agent " +
                 "reacts to something that walked up behind it.")]
        public float proximityAcquireRange = 4f;

        [Header("Scoring")]
        [Tooltip("How strongly the agent favours the target it already has, as a fraction of " +
                 "distance. 0.3 means the current target is treated as 30% closer than it is — a " +
                 "new candidate must be meaningfully better to steal focus. 0 = always take the " +
                 "nearest, which flip-flops between two equidistant enemies.")]
        [Range(0f, 0.9f)] public float currentTargetBias = 0.3f;

        [Tooltip("Same idea for whoever last damaged this agent — makes it turn on its attacker " +
                 "instead of continuing toward someone marginally closer.")]
        [Range(0f, 0.9f)] public float lastAttackerBias = 0.45f;

        [Tooltip("Penalty applied to candidates the agent cannot currently see, as a fraction of " +
                 "distance. 0.5 means an occluded candidate is treated as 50% further away.")]
        [Range(0f, 4f)] public float occludedPenalty = 0.75f;

        private void OnValidate()
        {
            acquisitionRange = Mathf.Max(1f, acquisitionRange);
            loseRange = Mathf.Max(acquisitionRange, loseRange);
            reevaluateInterval = Mathf.Max(0.05f, reevaluateInterval);
            memoryDuration = Mathf.Max(0f, memoryDuration);
            proximityAcquireRange = Mathf.Clamp(proximityAcquireRange, 0f, acquisitionRange);
        }
    }
}
