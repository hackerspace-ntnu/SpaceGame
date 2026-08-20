using UnityEngine;

namespace SpaceGame.Agents
{
    [CreateAssetMenu(menuName = "Agents/Fire Profile", fileName = "NewAgentFireProfile")]
    public class AgentFireProfile : ScriptableObject
    {
        [Header("Range")]
        [Tooltip("Hard floor. The agent cannot fire closer than this — for weapons that genuinely " +
                 "can't be used point-blank (rockets). Leave at 0 for normal guns; use preferredRange " +
                 "to control standoff instead.")]
        public float minRange = 0f;

        [Tooltip("Hard ceiling. Beyond this the agent can't fire and hands movement back to " +
                 "ChaseModule to close the gap.")]
        public float maxRange = 15f;

        [Header("Engagement")]
        [Tooltip("Where the agent wants to stand while shooting. Coming closer than this makes it " +
                 "back away rather than keep advancing — without it the agent walks into its target's " +
                 "face while firing, because ChaseModule owns the approach and has no reason to stop.")]
        public float preferredRange = 10f;

        [Tooltip("Slop around preferredRange. The agent only backs off once it is this much closer " +
                 "than preferred, so it isn't constantly shuffling to hit an exact distance.")]
        public float rangeTolerance = 2f;

        [Tooltip("Keep firing while backing off or strafing. Off means the agent only shoots from a " +
                 "standstill — right for heavy, slow, accurate weapons.")]
        public bool allowFireWhileRunning = true;

        [Header("Strafing")]
        [Tooltip("Sidestep within the engagement band instead of standing still. Costs nothing when " +
                 "off, but a stationary shooter is a much easier target and reads as a turret.")]
        public bool strafeWhileEngaged = true;

        [Tooltip("How far to each side the agent steps.")]
        public float strafeDistance = 3.5f;

        [Tooltip("Seconds before picking a new strafe direction.")]
        public float strafeInterval = 1.6f;

        [Header("Cadence")]
        [Tooltip("Seconds between trigger pulls (or burst starts).")]
        public float fireCooldown = 1.2f;

        [Header("Burst")]
        [Tooltip("Projectiles fired per trigger pull.")]
        [Min(1)]
        public int burstCount = 1;
        [Tooltip("Seconds between shots within a burst.")]
        public float burstInterval = 0.12f;

        private void OnValidate()
        {
            minRange = Mathf.Max(0f, minRange);
            maxRange = Mathf.Max(minRange + 0.1f, maxRange);

            // Kept strictly inside the fire band: a preferred range outside it would have the agent
            // backing away to a distance it cannot shoot from.
            preferredRange = Mathf.Clamp(preferredRange, minRange, maxRange);
            rangeTolerance = Mathf.Clamp(rangeTolerance, 0f, Mathf.Max(0.1f, preferredRange - minRange));

            strafeDistance = Mathf.Max(0f, strafeDistance);
            strafeInterval = Mathf.Max(0.1f, strafeInterval);
            fireCooldown = Mathf.Max(0.05f, fireCooldown);
            burstCount = Mathf.Max(1, burstCount);
            burstInterval = Mathf.Max(0.01f, burstInterval);
        }
    }
}
