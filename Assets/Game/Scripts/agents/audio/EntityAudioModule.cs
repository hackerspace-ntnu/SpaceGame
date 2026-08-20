// Plays FMOD sounds and emits noise events based on entity movement and state.
// Footstep noise is emitted at a configurable interval while moving, alerting nearby listeners.
// Attach alongside AgentController and NoiseEmitter.
//
// Each sound is chosen twice over: an SfxId picks which catalog slot this entity draws from — a
// robot and a trader want different voices from the same component — and the EventReference beside
// it overrides that outright for a one-off. Leaving both at their defaults still makes noise, which
// is the point; before the catalog existed an unassigned field was simply silent.
using UnityEngine;
using FMODUnity;
using SpaceGame.Audio;

namespace SpaceGame.Agents
{
    public class EntityAudioModule : MonoBehaviour
    {
        [Header("Footsteps")]
        [SerializeField] private SfxId footstepId = SfxId.EntityFootstep;
        [SerializeField] private EventReference footstepSound;
        [SerializeField] private float footstepInterval = 0.45f;
        [Tooltip("Speed threshold below which footsteps don't play.")]
        [SerializeField] private float movementThreshold = 0.3f;
        [SerializeField] private float footstepNoiseRadius = 8f;
        [SerializeField] private bool emitFootstepNoise = true;

        [Header("Aggro")]
        [SerializeField] private SfxId aggroId = SfxId.EntityAggro;
        [SerializeField] private EventReference aggroSound;
        [Tooltip("Noise radius broadcast when this entity becomes aggressive.")]
        [SerializeField] private float aggroNoiseRadius = 14f;

        [Header("Ambient")]
        [Tooltip("The idle vocalisation this entity makes — mumbling for people, chirps for machines. " +
                 "Set to None to keep an entity silent between events.")]
        [SerializeField] private SfxId ambientId = SfxId.NpcMumbleNeutral;
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

            Sfx.Play(footstepId, transform.position, footstepSound, GetInstanceID());

            if (emitFootstepNoise && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Footstep, footstepNoiseRadius);
        }

        private void HandleAmbient()
        {
            if (ambientId == SfxId.None && ambientSound.IsNull)
                return;

            ambientTimer -= Time.deltaTime;
            if (ambientTimer > 0f)
                return;

            // Rescheduled whether or not the sound actually reached FMOD — the catalog culls distant
            // entities, and an entity that never reschedules would go permanently quiet after
            // wandering out of earshot once.
            Sfx.Play(ambientId, transform.position, ambientSound, GetInstanceID());
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
                Sfx.Play(aggroId, transform.position, aggroSound, GetInstanceID());

                if (noiseEmitter)
                    noiseEmitter.Emit(NoiseType.Alert, aggroNoiseRadius);
            }
            else if (!isChasing)
            {
                wasChasing = false;
            }
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
}
