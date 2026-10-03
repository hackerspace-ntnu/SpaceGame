using FirstGearGames.SmoothCameraShaker;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// What an airlock cycle looks and sounds like: mist jets blasting out of the vent nozzles while the chamber
    /// vents, the chamber's haze dragged out toward the outer hatch, jets filling it with swirling haze while it
    /// pressurises, a rotating amber beacon, flashing status lamps, a klaxon, a hiss and a valve thud you feel.
    ///
    /// <para>
    /// <b>Presentation only, on every machine.</b> It reads <see cref="AirlockChamber.State"/>, which on a client is
    /// the server's replicated word, and decides nothing. A cycle is the airlock's one slow, meaningful event — the
    /// moment the player waits on — so it gets every sense at once (<c>GDC-L1-FEEL-0004</c>), and the lamps and the
    /// haze also say at rest which air the chamber holds, so the state is readable before anyone clicks
    /// (<c>GDC-L1-SYS-0006</c>). The camera kick is small, only at the two valve thuds, only near the chamber, and
    /// scaled by the player's shake setting (<c>GDC-L1-FEEL-0006</c>).
    /// </para>
    /// <para>
    /// Every particle system is authored by hand: this only plays, stops and scales the emission each one was
    /// authored with, so the look is tuned on the systems and the timing here.
    /// </para>
    /// </summary>
    public sealed class AirlockPressureEffects : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private AirlockChamber chamber;

        [Header("Jets")]
        [Tooltip("Mist jets that blast while the chamber VENTS, at the vent nozzles. Each plays for the cycle at its " +
                 "authored emission rate, shaped by Jet Envelope.")]
        [SerializeField] private ParticleSystem[] ventJets = new ParticleSystem[0];

        [Tooltip("Jets that fire while the chamber PRESSURISES, filling it.")]
        [SerializeField] private ParticleSystem[] fillJets = new ParticleSystem[0];

        [Tooltip("Jet emission over a cycle, as a fraction of each jet's authored rate (Y) against cycle progress " +
                 "(X, 0 to 1): a hard blast at the valve opening that tails off as the pressure equalises.")]
        [SerializeField] private AnimationCurve jetEnvelope = new(
            new Keyframe(0f, 0f), new Keyframe(0.06f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f));

        [Header("Chamber haze")]
        [Tooltip("A looping haze filling the chamber (box shape, noise for swirl). Its authored rate is the rate at " +
                 "the end of pressurising.")]
        [SerializeField] private ParticleSystem haze;

        [Tooltip("Fraction of the haze's authored rate kept while the chamber sits pressurised. 0 is clear air.")]
        [SerializeField, Range(0f, 1f)] private float restingHaze = 0.15f;

        [Tooltip("Where venting drags the haze: the outer hatch, or the exhaust grille.")]
        [SerializeField] private Transform ventTarget;

        [Tooltip("Acceleration, in m/s2, pulling the haze toward Vent Target while the chamber vents.")]
        [SerializeField, Min(0f)] private float ventPull = 14f;

        [Header("Warning beacon")]
        [Tooltip("The beacon's lamp, lit only while a cycle runs. A spot light on the rotor reads as a sweep.")]
        [SerializeField] private Light beaconLight;

        [Tooltip("What spins while a cycle runs: the beacon's reflector, with the light under it.")]
        [SerializeField] private Transform beaconRotor;

        [Tooltip("Spin axis, in the rotor's local frame.")]
        [SerializeField] private Vector3 beaconAxis = Vector3.up;

        [SerializeField] private float beaconDegreesPerSecond = 300f;

        [Header("Status lamps")]
        [SerializeField] private Renderer[] lampRenderers = new Renderer[0];
        [SerializeField] private Light statusLight;
        [Tooltip("Both hatches shut, nothing waiting.")]
        [SerializeField] private Color sealedColour = new(0.2f, 1f, 0.45f);
        [Tooltip("Cycling, or a hatch shutting before a cycle. Flashes while the air moves.")]
        [SerializeField] private Color cyclingColour = new(1f, 0.6f, 0.1f);
        [Tooltip("A hatch stands open.")]
        [SerializeField] private Color openColour = new(1f, 0.2f, 0.15f);
        [Tooltip("Flashes per second while the air moves.")]
        [SerializeField, Min(0f)] private float flashHz = 2.5f;
        [Tooltip("Fraction of each flash the lamps are lit.")]
        [SerializeField, Range(0.05f, 0.95f)] private float flashDuty = 0.5f;
        [Tooltip("Lamp brightness in the dark part of a flash, 0 to 1.")]
        [SerializeField, Range(0f, 1f)] private float flashFloor = 0.15f;
        [Tooltip("Emission multiplier on the lamp colour, so the lamps bloom.")]
        [SerializeField, Min(0f)] private float lampEmission = 2f;

        [Header("Audio")]
        [Tooltip("The klaxon as a cycle starts.")]
        [SerializeField] private SfxId cycleStartSound = SfxId.ShipAlarm;
        [Tooltip("The hiss held for the whole cycle. The catalog has no gas event; this is its stand-in.")]
        [SerializeField] private SfxId cycleLoop = SfxId.InteractOxygenFillLoop;
        [Tooltip("The valves shutting as the chamber equalises.")]
        [SerializeField] private SfxId cycleEndSound = SfxId.InteractOxygenFilled;

        [Header("Camera kick")]
        [Tooltip("Struck at the two valve thuds, start and end of a cycle. Leave empty for no kick.")]
        [SerializeField] private ShakeData cycleShake;
        [Tooltip("Kick strength before the player's shake setting.")]
        [SerializeField, Range(0f, 2f)] private float shakeMagnitude = 0.35f;
        [Tooltip("Only a local player within this many metres of the chamber feels it.")]
        [SerializeField, Min(0f)] private float shakeRadius = 4f;

        private readonly LoopingEmitter hiss = new();
        private MaterialPropertyBlock block;
        private float[] ventRates;
        private float[] fillRates;
        private float hazeRate;
        private float statusIntensity;
        private AirlockPhase shownPhase;

        private void Awake()
        {
            ventRates = AuthoredRates(ventJets);
            fillRates = AuthoredRates(fillJets);
            if (haze != null) hazeRate = haze.emission.rateOverTimeMultiplier;
            if (statusLight != null) statusIntensity = statusLight.intensity;
            if (beaconLight != null) beaconLight.enabled = false;
            if (chamber == null)
                Debug.LogError($"[Airlock] '{name}' has no chamber to read, so no cycle will show.", this);
        }

        private void OnDisable() => hiss.Stop(false);

        private void OnDestroy() => hiss.Stop(false);

        private void Update()
        {
            if (chamber == null) return;

            AirlockState state = chamber.State;
            if (state.Phase != shownPhase) ChangePhase(state.Phase);

            float progress = chamber.CycleProgress ?? 0f;
            DriveJets(ventJets, ventRates, state.Phase == AirlockPhase.Venting, progress);
            DriveJets(fillJets, fillRates, state.Phase == AirlockPhase.Pressurising, progress);
            DriveHaze(state, progress);

            if (beaconRotor != null && state.Phase != AirlockPhase.Settled)
                beaconRotor.Rotate(beaconAxis, beaconDegreesPerSecond * Time.deltaTime, Space.Self);

            PaintLamps(state);
        }

        /// <summary>The air started or stopped moving: the one-shot layers.</summary>
        private void ChangePhase(AirlockPhase phase)
        {
            bool starting = shownPhase == AirlockPhase.Settled;
            shownPhase = phase;

            Sfx.Play(starting ? cycleStartSound : cycleEndSound, transform.position, GetInstanceID());
            if (phase == AirlockPhase.Settled) hiss.Stop();
            else hiss.Play(cycleLoop, gameObject);

            if (beaconLight != null) beaconLight.enabled = phase != AirlockPhase.Settled;
            PullHaze(phase == AirlockPhase.Venting);
            Kick();
        }

        private static float[] AuthoredRates(ParticleSystem[] systems)
        {
            var rates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) rates[i] = systems[i].emission.rateOverTimeMultiplier;
            return rates;
        }

        private void DriveJets(ParticleSystem[] systems, float[] rates, bool firing, float progress)
        {
            float k = firing ? Mathf.Max(0f, jetEnvelope.Evaluate(progress)) : 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem jet = systems[i];
                if (jet == null) continue;

                ParticleSystem.EmissionModule emission = jet.emission;
                emission.rateOverTimeMultiplier = rates[i] * k;

                if (firing && !jet.isPlaying) jet.Play(true);
                else if (!firing && jet.isEmitting) jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// The chamber's air made visible: thickening as it pressurises, a light haze at rest when it holds the room's
        /// air, none when it holds the outside's, and none fed while venting drags the rest out.
        /// </summary>
        private void DriveHaze(AirlockState state, float progress)
        {
            if (haze == null) return;

            float k = state.Phase switch
            {
                AirlockPhase.Pressurising => Mathf.Max(restingHaze, progress),
                AirlockPhase.Venting => 0f,
                _ => state.Vented ? 0f : restingHaze,
            };

            ParticleSystem.EmissionModule emission = haze.emission;
            emission.rateOverTimeMultiplier = hazeRate * k;
            if (k > 0f && !haze.isPlaying) haze.Play(true);
        }

        private void PullHaze(bool venting)
        {
            if (haze == null) return;

            ParticleSystem.ForceOverLifetimeModule force = haze.forceOverLifetime;
            force.enabled = venting && ventTarget != null;
            if (!force.enabled) return;

            Vector3 pull = (ventTarget.position - haze.transform.position).normalized * ventPull;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(pull.x);
            force.y = new ParticleSystem.MinMaxCurve(pull.y);
            force.z = new ParticleSystem.MinMaxCurve(pull.z);
        }

        private void PaintLamps(AirlockState state)
        {
            Color colour = state.Busy ? cyclingColour
                         : state.InnerOpen || state.OuterOpen ? openColour
                         : sealedColour;

            float brightness = 1f;
            if (state.Phase != AirlockPhase.Settled)
            {
                bool lit = Mathf.Repeat(Time.time * flashHz, 1f) < flashDuty;
                brightness = lit ? 1f : flashFloor;
            }

            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColor, colour * brightness);
            block.SetColor(EmissionColor, colour * (brightness * lampEmission));
            foreach (Renderer lamp in lampRenderers)
                if (lamp != null) lamp.SetPropertyBlock(block);

            if (statusLight != null)
            {
                statusLight.color = colour;
                statusLight.intensity = statusIntensity * brightness;
            }
        }

        /// <summary>The valve thud, for a local player standing at the airlock.</summary>
        private void Kick()
        {
            if (cycleShake == null) return;

            Transform player = GameplayMenuScope.LocalPlayerTransform;
            if (player == null || (player.position - transform.position).sqrMagnitude > shakeRadius * shakeRadius)
                return;

            float scaled = shakeMagnitude * GameSettings.CameraShakeIntensity;
            if (scaled <= 0f) return;

            // A null instance means no CameraShaker is live: nothing to scale.
            ShakerInstance instance = CameraShakerHandler.Shake(cycleShake);
            instance?.MultiplyMagnitude(scaled, 0f);
        }
    }
}
