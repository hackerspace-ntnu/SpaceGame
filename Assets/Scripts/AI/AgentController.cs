using UnityEngine;
using System.Text;

public class AgentController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private MonoBehaviour brainComponent;
    [SerializeField] private MonoBehaviour motorComponent;
    [SerializeField] private AgentAnimatorDriver animatorDriver;

    private IAgentBrain brain;
    private IMovementMotor motor;

    private void Awake()
    {
        ResolveDependencies();
    }

    private void Update()
    {
        if (brain == null || motor == null)
        {
            return;
        }

        AgentContext context = new AgentContext
        {
            Self = transform,
            Position = transform.position,
            Velocity = motor.Velocity,
            HasReachedDestination = motor.HasReachedDestination,
            IsImmobile = motor.IsImmobile
        };

        MoveIntent intent = brain.Tick(in context, Time.deltaTime);
        motor.Tick(in intent, Time.deltaTime);

        if (animatorDriver)
        {
            animatorDriver.Tick(motor.Velocity, motor.IsImmobile);
        }
    }

    private void ResolveDependencies()
    {
        if (brainComponent != null && brainComponent is not IAgentBrain)
        {
            Debug.LogWarning($"{name}: Assigned brainComponent does not implement IAgentBrain. Auto-resolving instead.", this);
            brainComponent = null;
        }

        if (motorComponent != null && motorComponent is not IMovementMotor)
        {
            Debug.LogWarning($"{name}: Assigned motorComponent does not implement IMovementMotor. Auto-resolving instead.", this);
            motorComponent = null;
        }

        if (brainComponent == null)
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                if (component is IAgentBrain)
                {
                    brainComponent = component;
                    break;
                }
            }

            if (brainComponent == null)
            {
                foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component is IAgentBrain)
                    {
                        brainComponent = component;
                        break;
                    }
                }
            }
        }

        if (motorComponent == null)
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                if (component is IMovementMotor)
                {
                    motorComponent = component;
                    break;
                }
            }

            if (motorComponent == null)
            {
                foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component is IMovementMotor)
                    {
                        motorComponent = component;
                        break;
                    }
                }
            }
        }

        if (animatorDriver == null)
        {
            animatorDriver = GetComponent<AgentAnimatorDriver>();
        }

        brain = brainComponent as IAgentBrain;
        motor = motorComponent as IMovementMotor;

        if (brain == null || motor == null)
        {
            StringBuilder message = new StringBuilder();
            message.Append($"{name}: AgentController setup is incomplete.");

            if (brain == null)
            {
                message.Append(" Missing IAgentBrain.");
            }

            if (motor == null)
            {
                message.Append(" Missing IMovementMotor.");
            }

            message.Append(" Add NpcBrain or EnemyBrain + NavMeshAgentMotor (or equivalent) on this object or its children.");
            Debug.LogError(message.ToString(), this);
        }
    }
}
