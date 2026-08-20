// The ornithopter's voice: wind that rises with airspeed, a beat on every wing stroke, and a
// warning when the wing lets go.
//
// Driven entirely from IOrnithopterFlightState, the same seam the wing animator uses. That keeps it
// on the physics side of the assembly boundary without needing the motor, and means a test double
// can exercise it with no Rigidbody at all.
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Vehicles.Ornithopter
{
    public class OrnithopterAudio : MonoBehaviour
    {
        [Header("Wind")]
        [SerializeField] private SfxId windLoopId = SfxId.WingsWindLoop;
        [SerializeField] private EventReference windLoopSound;
        [Tooltip("Airspeed in m/s at which wind noise reaches full volume.")]
        [SerializeField] private float windFullVolumeSpeed = 30f;
        [Tooltip("Quietest the wind gets when the craft is barely moving. Not zero — a gliding " +
                 "ornithopter with silence over the canopy reads as broken, not as calm.")]
        [SerializeField, Range(0f, 1f)] private float windMinVolume = 0.15f;
        [Tooltip("Optional FMOD parameter fed normalised airspeed, if the event has one.")]
        [SerializeField] private string windSpeedParameter = "";

        [Header("Flap")]
        [SerializeField] private SfxId flapId = SfxId.WingsFlap;
        [SerializeField] private EventReference flapSound;
        [Tooltip("Below this flap effort the stroke is too gentle to be worth a sound.")]
        [SerializeField, Range(0f, 1f)] private float flapEffortThreshold = 0.12f;

        [Header("Stall")]
        [SerializeField] private SfxId stallId = SfxId.WingsStall;
        [Tooltip("Seconds before the stall warning may sound again.")]
        [SerializeField] private float stallRepeatDelay = 2f;

        [Header("Deploy")]
        [SerializeField] private SfxId deployId = SfxId.WingsDeploy;
        [SerializeField] private SfxId foldId = SfxId.WingsFold;
        [Tooltip("WingSpread above this counts as deployed, below as folded.")]
        [SerializeField, Range(0f, 1f)] private float spreadThreshold = 0.5f;

        private readonly LoopingEmitter wind = new LoopingEmitter();

        private IOrnithopterFlightState flight;

        private float lastPhase;
        private bool wasStalled;
        private bool wasDeployed;
        private float stallCooldown;
        private bool initialised;

        // Self-binds from the motor on this GameObject, exactly as OrnithopterWingAnimator does, so
        // dropping the component on the craft prefab is the whole installation.
        private void Awake()
        {
            Initialise(GetComponent<IOrnithopterFlightState>());
        }

        /// <summary>
        /// Hands over the flight source. Mirrors OrnithopterWingAnimator.Initialise, and is callable
        /// directly so a test can drive the audio from a stub.
        /// </summary>
        public void Initialise(IOrnithopterFlightState flightSource)
        {
            flight = flightSource;
            initialised = flight != null;

            if (initialised)
            {
                lastPhase = flight.FlapPhase;
                wasStalled = flight.IsStalled;
                wasDeployed = flight.WingSpread >= spreadThreshold;
            }
        }

        private void OnEnable()
        {
            if (initialised) StartWind();
        }

        // Both paths, always. The craft is despawned on landing and disabled when the pilot bails,
        // and a wind loop that survives either one follows the player around for the rest of the
        // session with nothing left to stop it.
        private void OnDisable() => wind.Stop();

        private void OnDestroy() => wind.Stop(false);

        private void StartWind()
        {
            wind.Play(windLoopId, gameObject, windLoopSound);
        }

        private void Update()
        {
            if (!initialised) return;

            if (!wind.IsPlaying) StartWind();

            float airspeed = flight.Airspeed;
            float normalised = Mathf.Clamp01(airspeed / Mathf.Max(0.1f, windFullVolumeSpeed));

            wind.SetVolume(Mathf.Lerp(windMinVolume, 1f, normalised));
            if (!string.IsNullOrEmpty(windSpeedParameter))
                wind.SetParameter(windSpeedParameter, normalised);

            HandleFlap();
            HandleStall();
            HandleDeploy();
        }

        /// <summary>
        /// Fires once per wing beat, caught on the phase wrapping past 1 back to 0.
        /// <para>
        /// The phase must not be smoothed on the way in — the animator makes the same point. A
        /// filtered phase does not wrap cleanly, and the beat detection either double-fires around
        /// the seam or misses strokes entirely at high flap rates.
        /// </para>
        /// </summary>
        private void HandleFlap()
        {
            float phase = flight.FlapPhase;
            float effort = Mathf.Clamp01(flight.FlapEffort);

            bool wrapped = phase < lastPhase;
            lastPhase = phase;

            if (!wrapped) return;
            if (effort < flapEffortThreshold) return;
            if (flight.WingSpread < spreadThreshold) return;

            Sfx.Play(flapId, transform.position, flapSound, GetInstanceID());
        }

        private void HandleStall()
        {
            stallCooldown -= Time.deltaTime;

            bool stalled = flight.IsStalled;
            bool justStalled = stalled && !wasStalled;
            wasStalled = stalled;

            if (!justStalled || stallCooldown > 0f) return;

            stallCooldown = stallRepeatDelay;
            Sfx.Play(stallId, transform.position, default, GetInstanceID());
        }

        private void HandleDeploy()
        {
            bool deployed = flight.WingSpread >= spreadThreshold;
            if (deployed == wasDeployed) return;

            wasDeployed = deployed;
            Sfx.Play(deployed ? deployId : foldId, transform.position, default, GetInstanceID());
        }

        private void OnValidate()
        {
            windFullVolumeSpeed = Mathf.Max(0.1f, windFullVolumeSpeed);
            stallRepeatDelay = Mathf.Max(0f, stallRepeatDelay);
        }
    }
}
