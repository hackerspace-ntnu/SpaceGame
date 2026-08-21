// Walks toward a target and stops at conversationDistance. Faces target once arrived.
// Good for friendly NPCs, bounty hunters greeting the player, vendors walking over, etc.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class ApproachModule : BehaviourModuleBase
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Faction relationship the nearest candidate must have. Requires EntityFaction on both entities.")]
        [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Allied;

        private EntityFaction selfFaction;
        private float retargetTimer;

        /// <summary>Set by a restore, consumed by the next <see cref="OnEnable"/>.</summary>
        private bool restoredThisEnable;

        private void Awake() => selfFaction = GetComponent<EntityFaction>();

        private void OnEnable()
        {
            if (restoredThisEnable) { restoredThisEnable = false; return; }

            retargetTimer = 0f;
        }

        // ─────────── For the save system ───────────
        // `target` is a serialized field, so an authored one survives a reload on its own. What does
        // not is a target picked at runtime by TargetResolution, and the countdown to the next pick.

        public Transform ApproachTarget => target;
        public float RetargetTimer => retargetTimer;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreApproach(Transform approachTarget, float timer)
        {
            target = approachTarget;
            retargetTimer = Mathf.Max(0f, timer);
            restoredThisEnable = true;
        }

        [Header("Range")]
        [SerializeField] private float detectRadius = 6f;
        [SerializeField] private float conversationDistance = 1.6f;

        [Header("Movement")]
        [SerializeField] private float speedMultiplier = 1.1f;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

        public override string ModuleDescription =>
            "Walks toward a target and stops at conversationDistance. Faces the target once arrived. Good for friendly NPCs or vendors.\n\n" +
            "• detectRadius — how close the target must be to trigger approach\n" +
            "• conversationDistance — how far away to stop\n" +
            "• speedMultiplier — walk speed while approaching";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            target = TargetResolution.Refresh(target, ref retargetTimer, 1f, deltaTime,
                                              selfFaction, requiredRelationship, context.Position);
            if (!target)
                return null;

            float distance = Vector3.Distance(context.Position, target.position);
            if (distance > detectRadius)
                return null;

            if (distance <= conversationDistance)
                return MoveIntent.StopAndFace(target.position);

            return MoveIntent.MoveTo(target.position, conversationDistance, speedMultiplier);
        }

        protected override void OnValidate()
        {
            detectRadius = Mathf.Max(0.1f, detectRadius);
            conversationDistance = Mathf.Max(0.1f, conversationDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
