using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything a jet of flame looks and sounds like, driven by one throttle.
    ///
    /// <para>
    /// Separate from <see cref="FlamethrowerArtifact"/> because the two answer different questions.
    /// The artifact decides <i>whether</i> the jet is on — from the hold stream, the tank and the
    /// authority split — and this decides what "on" looks like. Keeping them apart is also what
    /// lets the prefab be wired without touching the item's logic, and what keeps a machine that
    /// only ever presents (a peer) running the same code as the one that decides.
    /// </para>
    /// <para>
    /// The throttle is a ramp rather than a switch, and every channel reads it: emission rate, light
    /// intensity, loop volume. One meaningful event, several senses at once, none of them delaying
    /// the acknowledgement of the button (GDC-L1-FEEL-0004).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class FlameJet : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Parent of every emitter, moved to the muzzle and turned down the aim each frame. " +
                 "Must be a plain empty authored for this, never a node of the imported model: " +
                 "assigning a rotation to one of those destroys how the model was seated.")]
        [SerializeField] private Transform jetRoot;

        [Header("Emitters")]
        [Tooltip("The jet itself. Its authored emission rate is the full-throttle rate; the " +
                 "throttle scales it.")]
        [SerializeField] private ParticleSystem flame;

        [Tooltip("Sparks and burning specks thrown clear of the jet.")]
        [SerializeField] private ParticleSystem embers;

        [Tooltip("Smoke lifting off the tail of the jet.")]
        [SerializeField] private ParticleSystem smoke;

        [Tooltip("The pilot flame at the muzzle. Lit for as long as the item is held, whether or " +
                 "not the trigger is down — it is what says the weapon is live before it is fired.")]
        [SerializeField] private ParticleSystem pilot;

        [Header("Light")]
        [Tooltip("Optional light at the muzzle. Carries both the pilot flame and the jet.")]
        [SerializeField] private Light flameLight;

        [Tooltip("Light intensity at full throttle.")]
        [SerializeField] private float jetIntensity = 12f;

        [Tooltip("Light intensity of the pilot flame alone, with the trigger up.")]
        [SerializeField] private float pilotIntensity = 0.7f;

        [Tooltip("How far the light reaches at full throttle, in metres. A jet that lights the " +
                 "muzzle and nothing else is a torch; this is the range at which the flame lights " +
                 "what it is about to burn.")]
        [SerializeField] private float jetRange = 14f;

        [Tooltip("How far the pilot flame alone reaches, in metres.")]
        [SerializeField] private float pilotRange = 1.6f;

        [Tooltip("How far down the jet the light sits at full throttle, in metres. At the muzzle " +
                 "the plume lights the holder and nothing in front of them; out here it lights " +
                 "what the flame is actually touching.")]
        [SerializeField] private float lightReach = 2.6f;

        [Tooltip("How hard the light flickers, 0 for a steady lamp.")]
        [SerializeField, Range(0f, 1f)] private float flicker = 0.3f;

        [Header("Sound")]
        [Tooltip("Sustained loop while the jet is running. There is no flame event — the FMOD " +
                 "project is lost and no new ones are authorable — so this points at the nearest " +
                 "sustained hose loop the catalogue has.")]
        [SerializeField] private SfxId jetSound = SfxId.PortalSprayLoop;

        /// <summary>
        /// A loop, not a one-shot. Started through <c>Sfx.Play</c> a sustained event would run for
        /// the rest of the session with nothing holding a handle to stop it.
        /// </summary>
        private readonly LoopingEmitter jetLoop = new LoopingEmitter();

        /// <summary>0 with the trigger up, 1 at full jet. Ramped by the artifact, not by this.</summary>
        private float throttle;

        private bool piloted;
        private bool audible;

        /// <summary>
        /// Each emitter and everything hung under it. The jet is layered — a core, the billows
        /// rolling off it, the wisps between them — and those layers are CHILDREN of the system
        /// this component holds, so a layer added in the builder needs no new serialized field
        /// here. See <see cref="FlameLayers"/> for why the throttle cannot simply be written into
        /// the emission multiplier.
        /// </summary>
        private FlameLayers flameLayers;
        private FlameLayers emberLayers;
        private FlameLayers smokeLayers;
        private FlameLayers pilotLayers;

        /// <summary>
        /// Point the jet: it leaves <paramref name="origin"/> and travels along
        /// <paramref name="direction"/>.
        ///
        /// The direction is the holder's aim rather than the barrel's own forward, because the aim
        /// is what the authority sweeps its cone along. A jet drawn down the barrel while the fire
        /// is dealt down the aim is a weapon that visibly misses what it is burning.
        /// </summary>
        public void Aim(Vector3 origin, Vector3 direction)
        {
            if (jetRoot == null) return;

            jetRoot.SetPositionAndRotation(
                origin,
                direction.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(direction)
                    : jetRoot.rotation);
        }

        /// <summary>Light or douse the pilot flame. Called on equip and unequip.</summary>
        public void SetPilot(bool on)
        {
            if (piloted == on) return;

            piloted = on;
            pilotLayers?.SetEmitting(on);

            if (!on) Douse();
        }

        /// <summary>
        /// How hard the jet is running, 0..1. Called every frame; the edges do the expensive part.
        /// </summary>
        public void SetThrottle(float value)
        {
            float next = Mathf.Clamp01(value);
            bool wasRunning = throttle > 0.001f;
            bool running = next > 0.001f;

            throttle = next;

            if (running != wasRunning)
            {
                // Started and stopped on the edge, never per frame: calling Play on a system that is
                // already playing restarts it, which would clear everything already in the air sixty
                // times a second and leave a permanent stub of a jet.
                flameLayers?.SetEmitting(running);
                emberLayers?.SetEmitting(running);
                smokeLayers?.SetEmitting(running);

                if (running)
                {
                    if (jetSound != SfxId.None && !audible)
                    {
                        jetLoop.Play(jetSound, gameObject);
                        audible = true;
                    }
                }
                else
                {
                    jetLoop.Stop();
                    audible = false;
                }
            }

            if (running)
            {
                flameLayers?.SetRate(throttle);
                emberLayers?.SetRate(throttle);
                smokeLayers?.SetRate(throttle);
                jetLoop.SetVolume(throttle);
            }

            DrawLight();
        }

        /// <summary>
        /// Resolve the layers once. In Awake rather than lazily, because the first
        /// <see cref="SetThrottle"/> arrives on the frame the trigger goes down and a search there
        /// would be a hitch at exactly the moment the item is meant to feel immediate.
        /// </summary>
        private void Awake()
        {
            flameLayers = new FlameLayers(flame);
            emberLayers = new FlameLayers(embers);
            smokeLayers = new FlameLayers(smoke);
            pilotLayers = new FlameLayers(pilot);
        }

        private void DrawLight()
        {
            if (flameLight == null) return;

            float steady = Mathf.Lerp(piloted ? pilotIntensity : 0f, jetIntensity, throttle);
            bool lit = steady > 0.001f;

            if (flameLight.enabled != lit) flameLight.enabled = lit;
            if (!lit) return;

            flameLight.range = Mathf.Lerp(pilotRange, jetRange, throttle);

            // The light travels out along the plume as the jet comes up. Parked at the muzzle it
            // lights the holder's own arms and leaves what they are burning in the dark; six metres
            // of fire that illuminates nothing is the thing this fixes.
            Transform lightPoint = flameLight.transform;
            Vector3 local = lightPoint.localPosition;
            local.z = Mathf.Lerp(0.12f, lightReach, throttle);
            lightPoint.localPosition = local;

            // Two incommensurable frequencies rather than one, so the flicker never settles into a
            // rhythm the eye can predict and start reading as a pulse.
            float wobble = 1f
                + flicker * 0.6f * Mathf.Sin(Time.time * 31f)
                + flicker * 0.4f * Mathf.Sin(Time.time * 53.3f);

            flameLight.intensity = steady * wobble;
        }

        /// <summary>Everything off, from wherever it is being torn down.</summary>
        private void Douse()
        {
            throttle = 0f;

            flameLayers?.SetEmitting(false);
            emberLayers?.SetEmitting(false);
            smokeLayers?.SetEmitting(false);

            jetLoop.Stop();
            audible = false;

            DrawLight();
        }

        private void OnDisable()
        {
            piloted = false;
            pilotLayers?.SetEmitting(false);
            Douse();

            // Immediate rather than faded: a disable is a teardown, and a fade would hold a voice
            // attached to a GameObject that is about to stop existing.
            jetLoop.Stop(false);
        }

        private void OnDestroy() => jetLoop.Stop(false);
    }
}
