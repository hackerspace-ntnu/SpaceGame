// OBSOLETE: Replaced by the modular behaviour system (BehaviourModuleBase / IBehaviourModule).
// Kept for prefab compatibility. Migrate to ChaseModule + WanderModule + EntityCombatModule.
// This brain still works — AgentController picks it up as a legacy IAgentBrain fallback.
using UnityEngine;

public class EnemyBrain : MonoBehaviour, IAgentBrain
{
    [Header("Behaviours")]
    [SerializeField] private WanderBehaviour wanderBehaviour;

    [Header("Targeting")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private float detectRange = 10f;
    [SerializeField] private float loseTargetRange = 14f;

    [Header("Combat Movement")]
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private float chaseStopDistance = 1.3f;
    [SerializeField] private float wanderSpeedMultiplier = 1f;
    [SerializeField] private float chaseSpeedMultiplier = 1.3f;

    private bool hasTarget;

    private void Awake()
    {
        if (!wanderBehaviour)
        {
            wanderBehaviour = GetComponent<WanderBehaviour>();
        }
    }

    private void OnEnable()
    {
        hasTarget = false;
        wanderBehaviour?.ResetState();
    }

    public MoveIntent Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();

        if (target)
        {
            float distance = Vector3.Distance(context.Position, target.position);

            if (!hasTarget && distance <= detectRange)
            {
                hasTarget = true;
            }
            else if (hasTarget && distance > loseTargetRange)
            {
                hasTarget = false;
            }

            if (hasTarget)
            {
                if (distance <= attackRange)
                {
                    return MoveIntent.StopAndFace(target.position);
                }

                return MoveIntent.MoveTo(target.position, chaseStopDistance, chaseSpeedMultiplier);
            }
        }

        if (!wanderBehaviour)
        {
            return MoveIntent.Idle();
        }

        if (!wanderBehaviour.Tick(context.Position, context.HasReachedDestination, deltaTime, out Vector3 destination))
        {
            return MoveIntent.Idle();
        }

        return MoveIntent.MoveTo(destination, 0.2f, wanderSpeedMultiplier);
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            return;
        }

        GameObject targetObject = GameObject.FindGameObjectWithTag(targetTag);
        if (targetObject)
        {
            target = targetObject.transform;
        }
    }

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        loseTargetRange = Mathf.Max(detectRange, loseTargetRange);
        attackRange = Mathf.Max(0.1f, attackRange);
        chaseStopDistance = Mathf.Max(0.1f, chaseStopDistance);
        wanderSpeedMultiplier = Mathf.Max(0.01f, wanderSpeedMultiplier);
        chaseSpeedMultiplier = Mathf.Max(0.01f, chaseSpeedMultiplier);
    }
}
