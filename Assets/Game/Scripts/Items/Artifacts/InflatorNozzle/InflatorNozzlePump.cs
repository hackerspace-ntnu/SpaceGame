// Everything the inflator nozzle visibly and audibly does.
//
// Split off the artifact so that the item is about where the pressure goes and this is about what
// the pump looks like putting it there. It holds no gameplay state and decides nothing: it is told
// whether the valve is delivering and what the target's gauge reads, and eases the model, the jet,
// the needle and the loop toward that.
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// The pump's moving parts: the plunger stroking under the hand, the hose swelling with each
    /// stroke, the jet out of the collar, the hiss, the needle climbing the pressure dial, and the
    /// burst when a body goes over the top of it.
    ///
    /// <para>
    /// Driven by <see cref="InflatorNozzleArtifact"/> on every machine, from facts every machine
    /// already has — the hold stream reaches all of them and the target's pressure is replicated.
    /// Nothing here is sent and nothing here asks who owns the pump: a peer watching somebody else
    /// inflate a crate sees the same plunger, hears the same hiss and watches the same needle, which
    /// is the ordinary <c>Present</c> half of an artifact.
    /// </para>
    /// <para>
    /// The layering is <c>GDC-L1-FEEL-0004</c> taken at its word for the one event that matters
    /// here: a burst lands on sight (a puff at the body, the needle dropping off the peg) and on
    /// hearing (a report at the body's own position, not at the pump), so a player who was watching
    /// their target rather than their gauge still knows what happened. And it is amplification of a
    /// real event rather than decoration — the pump only draws a burst on a tick that really burst
    /// something.
    /// </para>
    /// <para>
    /// <b>The needle is the reading, and the red arc only confirms it</b> (<c>GDC-L1-UX-0003</c>:
    /// never encode information in colour alone). The arc is painted geometry on the dial face; what
    /// this component drives is the angle, so a player who cannot separate the arc from the plate
    /// still sees the needle at the top of its sweep.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InflatorNozzlePump : MonoBehaviour
    {
        [Header("Jet")]
        [Tooltip("The air leaving the collar. Parent it under Marker_Muzzle. Played while the pump " +
                 "is delivering and stopped when it is not; the particles already in the air are " +
                 "left to finish.")]
        [SerializeField] private ParticleSystem jet;

        [Header("Moving parts")]
        [Tooltip("The plunger under the hand, driven up and down its own axis while pumping. " +
                 "Optional.")]
        [SerializeField] private Transform plunger;

        [Tooltip("How far the plunger travels at the bottom of a stroke, in its parent's local " +
                 "space. Written as an offset from the pose the model was imported with, never as " +
                 "an assignment — an FBX node carries whatever the source file gave it.")]
        [SerializeField] private Vector3 plungerStroke = new(0f, -0.06f, 0f);

        [Tooltip("Strokes per second at full delivery. Fast enough to read as work being done, " +
                 "slow enough that the individual strokes are countable.")]
        [SerializeField, Min(0.1f)] private float strokesPerSecond = 3f;

        [Tooltip("The coiled hose, swelled in time with the plunger so the pressure visibly " +
                 "travels down it. Optional.")]
        [SerializeField] private Transform hose;

        [Tooltip("How much bigger the hose gets at the bottom of a stroke, as a share of its " +
                 "authored size. Small: this is secondary motion, not a balloon.")]
        [SerializeField, Range(0f, 0.5f)] private float hoseBulge = 0.06f;

        [Header("Dial")]
        [Tooltip("The needle on the pressure dial — Marker_Dial. It reads the TARGET's inflation, " +
                 "not this item's tank, which is why it is a needle rather than a second fill bar: " +
                 "two bars side by side that mean different things is the readout a player " +
                 "misreads. Optional.")]
        [SerializeField] private Transform needle;

        [Tooltip("Which way the needle turns, in its own local space. The axis out of the dial " +
                 "face, so the needle sweeps across the plate rather than lifting off it.")]
        [SerializeField] private Vector3 needleAxis = Vector3.forward;

        [Tooltip("How far the needle swings between an empty gauge and the top of it, in degrees. " +
                 "Negative to sweep the other way round the face.")]
        [SerializeField] private float needleSweepDegrees = -240f;

        [Tooltip("Seconds for the needle to cross its whole sweep. A little lag, so the gauge " +
                 "reads like an instrument with a spring behind it rather than a number.")]
        [SerializeField, Min(0.01f)] private float needleSeconds = 0.25f;

        [Header("Burst")]
        [Tooltip("The puff a body makes when it goes over the top. Author it PLAYING with its " +
                 "emission module disabled, simulating in WORLD space and culling set to Always " +
                 "Simulate: one system is moved to each burst and emits, rather than a new object " +
                 "being spawned per burst. Optional.")]
        [SerializeField] private ParticleSystem popBurst;

        [Tooltip("Particles in one burst.")]
        [SerializeField, Min(1)] private int popParticles = 40;

        [Header("Feel")]
        [Tooltip("Seconds for the valve to open once the trigger goes down.")]
        [SerializeField, Min(0.001f)] private float openSeconds = 0.07f;

        [Tooltip("Seconds for it to shut again. Slower than opening, so the jet trails off rather " +
                 "than being cut, which is what a pressurised line actually does.")]
        [SerializeField, Min(0.001f)] private float shutSeconds = 0.18f;

        [Header("Audio")]
        [Tooltip("The hiss, looped while the pump is delivering. There is no inflator event in the " +
                 "catalog and no way to author one, so this borrows the portal can's spray loop.")]
        [SerializeField] private SfxId pumpLoop = SfxId.PortalSprayLoop;

        [Tooltip("The burst, played at the BODY rather than at the pump — the player who was " +
                 "watching their target hears it where they are looking.")]
        [SerializeField] private SfxId popSound = SfxId.ImpactExplosion;

        private readonly LoopingEmitter hiss = new LoopingEmitter();

        /// <summary>0 shut, 1 fully open. The jet, the stroke and the hose are all functions of it.</summary>
        private float open;

        /// <summary>Where in the stroke the plunger is, 0…1 and wrapping.</summary>
        private float strokePhase;

        /// <summary>What the needle is being asked to read, 0…1. Pushed by the artifact.</summary>
        private float pressure;

        /// <summary>Where the needle actually is. Chases <see cref="pressure"/>.</summary>
        private float reading;

        private bool delivering;

        /// <summary>
        /// The authored pose of each moving part, captured before anything drives it.
        ///
        /// <para>
        /// All three are applied as an offset FROM these rather than as a bare assignment. An FBX
        /// node carries whatever position, rotation and scale the source file gave it — the export
        /// bakes nothing — so writing one straight onto an imported part destroys how the model was
        /// seated, and the part snaps out of place on the first frame the pump is used.
        /// </para>
        /// </summary>
        private Vector3 plungerRest;
        private Vector3 hoseRest = Vector3.one;
        private Quaternion needleRest = Quaternion.identity;

        private void Awake()
        {
            if (plunger != null) plungerRest = plunger.localPosition;
            if (hose != null) hoseRest = hose.localScale;
            if (needle != null) needleRest = needle.localRotation;
        }

        /// <summary>
        /// Is the pump actually delivering right now?
        ///
        /// Pushed once a frame by the artifact rather than raised as an event, so a machine that
        /// missed the tick which started or ended a pump is put right by the next frame instead of
        /// hissing for ever. Only a CHANGE acts, because the jet and the loop are handles rather
        /// than values, and re-stopping a stopped one sixty times a second is work that says nothing.
        /// </summary>
        public void SetPumping(bool pumping)
        {
            if (pumping == delivering) return;

            delivering = pumping;

            if (pumping) StartJet();
            else StopJet(immediate: false);
        }

        /// <summary>
        /// What the target's gauge reads, 0…1 along this nozzle's own range.
        ///
        /// A share rather than a scalar, so a ballast item sweeping the other way still drives the
        /// needle from empty to full — the dial says "how far have I got with this", which is the
        /// question a player pumping actually has (<c>GDC-L1-SYS-0006</c>).
        /// </summary>
        public void SetPressure(float share) => pressure = Mathf.Clamp01(share);

        /// <summary>
        /// A body just went over the top: puff and report, at the body rather than at the pump.
        ///
        /// <para>
        /// One shared system MOVED to the point rather than an object spawned per burst — the
        /// system simulates in world space, so the puff from the last burst stays where it was made
        /// while the pump swings away with the hand.
        /// </para>
        /// </summary>
        public void Burst(Vector3 point)
        {
            Sfx.Play(popSound, point, GetInstanceID());

            if (popBurst == null) return;

            popBurst.transform.position = point;
            popBurst.Emit(new ParticleSystem.EmitParams(), popParticles);
        }

        private void OnDisable()
        {
            // A pump put away mid-stroke — a hotbar change is the ordinary way — must not leave a
            // voice running or a jet emitting on an object that is about to be destroyed.
            delivering = false;
            open = 0f;
            pressure = 0f;
            reading = 0f;
            StopJet(immediate: true);
            Apply();
        }

        // Reachable from OnDisable and OnDestroy both: a loop cleaned up in only one of them leaks
        // whenever the object goes away through the other.
        private void OnDestroy() => hiss.Stop(false);

        private void Update()
        {
            // A pump nobody is using writes nothing. Not an optimisation: an idle component that
            // keeps assigning a rest pose every frame is one that quietly wins any argument with
            // whatever else might come to drive the same parts.
            if (!delivering && open <= 0f && reading <= 0f) return;

            float deltaTime = Time.deltaTime;

            open = Mathf.MoveTowards(open, delivering ? 1f : 0f,
                                     deltaTime / (delivering ? openSeconds : shutSeconds));

            // Advanced by how open the valve is, so the plunger eases into its rhythm and out of it
            // rather than starting and stopping mid-stroke.
            strokePhase = Mathf.Repeat(strokePhase + deltaTime * strokesPerSecond * open, 1f);

            reading = Mathf.MoveTowards(reading, pressure, deltaTime / needleSeconds);

            Apply();
        }

        /// <summary>Push the three driving numbers onto every part that reads them.</summary>
        private void Apply()
        {
            // A cosine rather than a sawtooth: a plunger is on the end of an arm and turns round at
            // each end of its travel instead of teleporting back to the top.
            float stroke = 0.5f - 0.5f * Mathf.Cos(strokePhase * 2f * Mathf.PI);

            if (plunger != null)
                plunger.localPosition = plungerRest + plungerStroke * stroke;

            if (hose != null)
                hose.localScale = hoseRest * (1f + hoseBulge * stroke);

            if (needle != null)
                needle.localRotation =
                    needleRest * Quaternion.AngleAxis(needleSweepDegrees * reading, needleAxis);
        }

        private void StartJet()
        {
            if (jet != null && !jet.isEmitting) jet.Play();

            hiss.Play(pumpLoop, gameObject);
        }

        private void StopJet(bool immediate)
        {
            if (jet != null)
            {
                // Stop EMITTING, not stop dead: the air already out of the collar belongs to a pump
                // that really happened and should be allowed to disperse.
                jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (immediate) jet.Clear(true);
            }

            hiss.Stop(!immediate);
        }
    }
}
