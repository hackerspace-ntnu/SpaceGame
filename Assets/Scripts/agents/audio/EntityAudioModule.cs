// Plays FMOD sounds and emits noise events based on entity movement and state.
// Footstep noise is emitted at a configurable interval while moving, alerting nearby listeners.
// Attach alongside AgentController and NoiseEmitter.
using UnityEngine;
using FMODUnity;

public class EntityAudioModule : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private EventReference footstepSound;
    [SerializeField] private float footstepInterval = 0.45f;
    [Tooltip("Speed threshold below which footsteps don't play.")]
    [SerializeField] private float movementThreshold = 0.3f;
    [SerializeField] private float footstepNoiseRadius = 8f;
    [SerializeField] private bool emitFootstepNoise = true;

    [Header("Aggro")]
    [SerializeField] private EventReference aggroSound;
    [Tooltip("Noise radius broadcast when this entity becomes aggressive.")]
    [SerializeField] private float aggroNoiseRadius = 14f;

    [Header("Ambient")]
    [SerializeField] private EventReference ambientSound;
    [SerializeField] private float ambientMinInterval = 5f;
    [SerializeField] private float ambientMaxInterval = 12f;

    private IMovementMotor motor;
    private NoiseEmitter noiseEmitter;
    private ChaseModule chaseModule;

    private float footstepTimer;
    private float ambientTimer;
    private bool wasChasing;

    private void Awake()
    {
        motor = GetComponent<IMovementMotor>();
        if (motor == null)
            motor = GetComponentInChildren<IMovementMotor>();

        noiseEmitter = GetComponent<NoiseEmitter>();
        chaseModule = GetComponent<ChaseModule>();
    }

    private void OnEnable()
    {
        footstepTimer = 0f;
        ScheduleNextAmbient();
        wasChasing = false;

        if (chaseModule != null)
        {
            chaseModule.OnEnterEngageRange += OnBecameAggressive;
        }
    }

    private void OnDisable()
    {
        if (chaseModule != null)
            chaseModule.OnEnterEngageRange -= OnBecameAggressive;
    }

    private void Update()
    {
        HandleFootsteps();
        HandleAmbient();
        HandleAggroTransition();
    }

    private void HandleFootsteps()
    {
        if (motor == null)
            return;

        float speed = motor.Velocity.magnitude;
        if (speed < movementThreshold)
            return;

        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f)
            return;

        footstepTimer = footstepInterval / Mathf.Max(0.1f, speed * 0.5f);

        if (!footstepSound.IsNull)
            RuntimeManager.PlayOneShot(footstepSound, transform.position);

        if (emitFootstepNoise && noiseEmitter)
            noiseEmitter.Emit(NoiseType.Footstep, footstepNoiseRadius);
    }

    private void HandleAmbient()
    {
        if (ambientSound.IsNull)
            return;

        ambientTimer -= Time.deltaTime;
        if (ambientTimer > 0f)
            return;

        RuntimeManager.PlayOneShot(ambientSound, transform.position);
        ScheduleNextAmbient();
    }

    private void HandleAggroTransition()
    {
        if (chaseModule == null)
            return;

        bool isChasing = chaseModule.HasTarget;
        if (isChasing && !wasChasing)
        {
            wasChasing = true;
            if (!aggroSound.IsNull)
                RuntimeManager.PlayOneShot(aggroSound, transform.position);

            if (noiseEmitter)
                noiseEmitter.Emit(NoiseType.Alert, aggroNoiseRadius);
        }
        else if (!isChasing)
        {
            wasChasing = false;
        }
    }

    private void OnBecameAggressive()
    {
        // Additional hook for entering attack range specifically.
    }

    private void ScheduleNextAmbient()
    {
        ambientTimer = Random.Range(ambientMinInterval, ambientMaxInterval);
    }

    private void OnValidate()
    {
        footstepInterval = Mathf.Max(0.05f, footstepInterval);
        movementThreshold = Mathf.Max(0f, movementThreshold);
        footstepNoiseRadius = Mathf.Max(0f, footstepNoiseRadius);
        aggroNoiseRadius = Mathf.Max(0f, aggroNoiseRadius);
        ambientMinInterval = Mathf.Max(0.1f, ambientMinInterval);
        ambientMaxInterval = Mathf.Max(ambientMinInterval, ambientMaxInterval);
    }
}
