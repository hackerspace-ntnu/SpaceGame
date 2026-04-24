// Main runtime coordinator for entity agents.
// Each frame: ticks all side-effect modules (ClaimsMovement==false) unconditionally, then
// evaluates movement modules (ClaimsMovement==true) highest-priority first — first non-null wins.
// Also supports the legacy IAgentBrain interface so old prefabs don't break immediately.
using System.Collections.Generic;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private MonoBehaviour MotorComponent;
    [SerializeField] private AgentAnimatorDriver animatorDriver;

    [Header("Nearby Agents (Flocking)")]
    [Tooltip("Radius within which nearby agents are gathered for FlockingModule. 0 = disabled.")]
    [SerializeField] private float nearbyAgentScanRadius = 0f;
    [SerializeField] private LayerMask nearbyAgentLayer;

    [Header("Speed Variation")]
    [Tooltip("How much the agent's speed can drift above and below its base. 0.1 = ±10%.")]
    [SerializeField] private float speedVariationAmount = 0.1f;
    [Tooltip("How many seconds one full drift cycle takes.")]
    [SerializeField] private float speedVariationPeriod = 6f;

    public IMovementMotor Motor { get; private set; }
    private IBehaviourModule[] movementModules;   // ClaimsMovement == true, sorted by priority
    private IBehaviourModule[] sideEffectModules; // ClaimsMovement == false, ticked every frame
    private IAgentBrain legacyBrain;
    private HerdModule herdModule;
    private float speedVariationPhase;

    // Reused buffers for neighbour scan — instance-level to avoid cross-agent corruption.
    private readonly Collider[] neighbourBuffer = new Collider[32];
    private readonly Vector3[] nearbyPositionBuffer = new Vector3[32];
    private readonly Vector3[] nearbyVelocityBuffer = new Vector3[32];

    private void Awake()
    {
        ResolveMotor();
        ResolveModules();
        speedVariationPhase = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        if (Motor == null)
            return;

        float deltaTime = Time.deltaTime;
        AgentContext context = BuildContext();
        MoveIntent intent = EvaluateModules(in context, deltaTime);

        if (speedVariationAmount > 0f && intent.Type == AgentIntentType.MoveToPosition)
        {
            float drift = 1f + Mathf.Sin(Time.time * (Mathf.PI * 2f / speedVariationPeriod) + speedVariationPhase) * speedVariationAmount;
            intent.SpeedMultiplier *= drift;
        }

        Motor.Tick(in intent, deltaTime);

        if (animatorDriver)
            animatorDriver.Tick(Motor.Velocity, Motor.IsImmobile, intent.IsRunning);
    }

    // ──────────────────────────────────────────────
    // Context
    // ──────────────────────────────────────────────

    private AgentContext BuildContext()
    {
        AgentContext ctx = new AgentContext
        {
            Self = transform,
            Position = transform.position,
            Velocity = Motor.Velocity,
            HasReachedDestination = Motor.HasReachedDestination,
            IsImmobile = Motor.IsImmobile,
        };

        if (nearbyAgentScanRadius > 0f)
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, nearbyAgentScanRadius, neighbourBuffer, nearbyAgentLayer);
            int written = 0;
            for (int i = 0; i < count && written < nearbyPositionBuffer.Length; i++)
            {
                Transform t = neighbourBuffer[i].transform;
                if (t == transform)
                    continue;
                nearbyPositionBuffer[written] = t.position;
                // Populate velocity from NavMeshAgentMotor if available.
                IMovementMotor neighbourMotor = t.GetComponent<IMovementMotor>();
                nearbyVelocityBuffer[written] = neighbourMotor != null ? neighbourMotor.Velocity : Vector3.zero;
                written++;
            }
            ctx.NearbyAgentPositions = nearbyPositionBuffer;
            ctx.NearbyAgentVelocities = nearbyVelocityBuffer;
            ctx.NearbyAgentCount = written;
        }

        return ctx;
    }

    // ──────────────────────────────────────────────
    // Module evaluation
    // ──────────────────────────────────────────────

    private MoveIntent EvaluateModules(in AgentContext context, float deltaTime)
    {
        // Always tick side-effect modules (attacks, audio, etc.) — they never produce a MoveIntent.
        if (sideEffectModules != null)
        {
            foreach (IBehaviourModule module in sideEffectModules)
            {
                if (module.IsActive)
                    module.Tick(in context, deltaTime);
            }
        }

        // First movement module to return non-null wins this frame.
        if (movementModules != null)
        {
            foreach (IBehaviourModule module in movementModules)
            {
                if (!module.IsActive)
                    continue;

                MoveIntent? result = module.Tick(in context, deltaTime);
                if (result.HasValue)
                {
                    // Don't broadcast Idle — it would lock the whole herd in place.
                    if (result.Value.Type != AgentIntentType.Idle)
                        herdModule?.Publish(module.Priority, result.Value);
                    return result.Value;
                }
            }
        }

        // Fall back to legacy brain if present (old NpcBrain / EnemyBrain on same prefab).
        if (legacyBrain != null)
            return legacyBrain.Tick(in context, deltaTime);

        return MoveIntent.Idle();
    }

    // ──────────────────────────────────────────────
    // Setup
    // ──────────────────────────────────────────────

    private void ResolveModules()
    {
        List<IBehaviourModule> movement = new List<IBehaviourModule>();
        List<IBehaviourModule> sideEffects = new List<IBehaviourModule>();

        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is IBehaviourModule module)
            {
                if (module.ClaimsMovement)
                    movement.Add(module);
                else
                    sideEffects.Add(module);
            }
        }

        // Stable sort movement modules: highest priority first.
        movement.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        movementModules   = movement.ToArray();
        sideEffectModules = sideEffects.ToArray();
        herdModule = GetComponentInChildren<HerdModule>(true);

        // Legacy fallback: pick up any old IAgentBrain that isn't also IBehaviourModule.
        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is IAgentBrain brain && mb is not IBehaviourModule)
            {
                legacyBrain = brain;
                break;
            }
        }

        if (movementModules.Length == 0 && legacyBrain == null)
            Debug.LogWarning($"{name}: AgentController found no movement IBehaviourModule or IAgentBrain. Add at least one module.", this);
    }

    private void ResolveMotor()
    {
        if (MotorComponent != null && MotorComponent is not IMovementMotor)
        {
            Debug.LogWarning($"{name}: MotorComponent does not implement IMovementMotor. Auto-resolving.", this);
            MotorComponent = null;
        }

        if (MotorComponent == null)
        {
            foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IMovementMotor)
                {
                    MotorComponent = mb;
                    break;
                }
            }
        }

        if (animatorDriver == null)
            animatorDriver = GetComponentInChildren<AgentAnimatorDriver>(true);

        Motor = MotorComponent as IMovementMotor;

        if (Motor == null)
            Debug.LogError($"{name}: AgentController could not find an IMovementMotor. Add NavMeshAgentMotor (pathfinding) or RigidbodyMotor (physics vehicle).", this);
    }

    // Allow modules or external systems to force a live refresh (e.g. after adding components at runtime).
    public void RefreshModules() => ResolveModules();
}
