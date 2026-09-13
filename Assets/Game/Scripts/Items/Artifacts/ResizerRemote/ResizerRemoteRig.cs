// Everything the resizer remote visibly and audibly does.
//
// Split off the artifact so that the item is about where the signal goes and this is about what the
// handset looks like sending it. It holds no gameplay state and decides nothing: it is told whether
// the channel is delivering, which way the dial is set and what the target's size reads, and eases
// the whip, the knob, the lamp, the beam and the hum toward that.
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// The handset's moving parts: the whip telescoping out while the channel is open, the polarity
    /// knob turning between its two detents, the lamp taking the colour of whichever one it is on,
    /// the beam out to the target and the carrier hum.
    ///
    /// <para>
    /// Driven by <see cref="ResizerRemoteArtifact"/> on every machine, from facts every machine
    /// already has — the hold stream reaches all of them and the target's size is replicated.
    /// Nothing here is sent and nothing here asks who owns the handset: a peer watching somebody
    /// else point this thing sees the same whip go up, hears the same hum and follows the same beam.
    /// That is the ordinary <c>Present</c> half of an artifact, and here it is also the only warning
    /// the person on the other end gets, which is why none of it is owner-only
    /// (<c>GDC-L1-MP-0002</c>).
    /// </para>
    /// <para>
    /// <b>The setting is carried by a position first and a colour second</b> (<c>GDC-L1-UX-0003</c>:
    /// never encode information in colour alone). What this component drives is the knob's ANGLE;
    /// the lamp and the beam only repeat it, so a player who cannot separate the two colours reads
    /// the pointer.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResizerRemoteRig : MonoBehaviour
    {
        [Header("Whip")]
        [Tooltip("The antenna — Mesh_ResizerRemote_Whip, whose origin is the collar it stands out " +
                 "of. Extended along its own axis while the channel is open. Optional.")]
        [SerializeField] private Transform whip;

        [Tooltip("The whip's own local axis to stretch along. Z, because the model's sections run " +
                 "up its local Z out of the collar.")]
        [SerializeField] private Vector3 whipAxis = Vector3.forward;

        [Tooltip("How much longer the whip gets at full extension, as a share of its authored " +
                 "length. Written as a multiple of the scale the model was imported with, never as " +
                 "an assignment — an FBX node carries whatever the source file gave it, and " +
                 "assigning over that flattens the part on the first driven frame.")]
        [SerializeField, Range(0f, 1.5f)] private float whipExtension = 0.5f;

        [Tooltip("Seconds for the whip to run all the way out, and the same back in. Slow enough " +
                 "to read as a mast rising rather than a pop.")]
        [SerializeField, Min(0.01f)] private float whipSeconds = 0.25f;

        [Header("Polarity knob")]
        [Tooltip("Mesh_ResizerRemote_Dial, whose origin is its own spindle. Optional.")]
        [SerializeField] private Transform knob;

        [Tooltip("The axis out of the knob's face, so the pointer sweeps across the chin rather " +
                 "than lifting off it.")]
        [SerializeField] private Vector3 knobAxis = Vector3.up;

        [Tooltip("How far the knob turns between the two detents, in degrees. Half of it each way " +
                 "from the pose the model was imported in, so neither setting is 'the authored one'.")]
        [SerializeField] private float knobThrowDegrees = 64f;

        [Tooltip("Seconds for the knob to cross between detents. A little lag, so it reads as a " +
                 "switch being thrown rather than a number changing.")]
        [SerializeField, Min(0.01f)] private float knobSeconds = 0.12f;

        [Header("Lamp")]
        [Tooltip("Mesh_ResizerRemote_Lamp. Tinted through a MaterialPropertyBlock, so two handsets " +
                 "set opposite ways do not share one lit material. Optional.")]
        [SerializeField] private Renderer lamp;

        [Tooltip("The lamp's colour when the dial is set to enlarge. Matches the target rim.")]
        [SerializeField] private Color growColour = new(0.45f, 0.85f, 1f, 1f);

        [Tooltip("And when it is set to shrink.")]
        [SerializeField] private Color shrinkColour = new(1f, 0.62f, 0.30f, 1f);

        [Tooltip("How much the lamp is dimmed while the channel is shut. Not off: the handset is " +
                 "powered whenever it is in a hand, and a dead lamp reads as a dead item.")]
        [SerializeField, Range(0f, 1f)] private float idleDim = 0.25f;

        [Header("Beam")]
        [Tooltip("The carrier, drawn from the whip's tip to whatever the handset is on. Two " +
                 "positions, so a LineRenderer with positionCount 2 and useWorldSpace on. Optional.")]
        [SerializeField] private LineRenderer beam;

        [Tooltip("Where the beam leaves the handset — Marker_Emitter, the whip's TIP. From the " +
                 "collar instead it comes out of the player's own knuckles.")]
        [SerializeField] private Transform emitter;

        [Tooltip("How fast the beam's far end chases the endpoint the hold stream reports, per " +
                 "second. The stream arrives 15 times a second and this component runs at the " +
                 "frame rate, so the end is chased rather than assigned or it steps across a " +
                 "moving target. Fast enough that the beam is never visibly behind the crosshair, " +
                 "slow enough to smooth the gap between ticks.")]
        [SerializeField, Min(1f)] private float beamChaseRate = 12f;

        [Header("Audio")]
        [Tooltip("The carrier hum, looped while the channel is delivering. There is no resizer " +
                 "event in the catalog and no way to author one — the FMOD project is lost — so " +
                 "this borrows the antigravity bed.")]
        [SerializeField] private SfxId carrierLoop = SfxId.AmbAntigravity;

        private readonly LoopingEmitter hum = new LoopingEmitter();

        /// <summary>0 shut, 1 fully open. The whip and the lamp are functions of it.</summary>
        private float open;

        /// <summary>Where the knob actually is, −1…+1. Chases <see cref="wantsGrow"/>.</summary>
        private float pointer;

        private bool wantsGrow = true;
        private bool delivering;

        /// <summary>
        /// What the beam is being asked to reach, where it has actually got to, and whether it has
        /// anything to reach at all.
        ///
        /// <para>
        /// The two are separate because the hold stream runs at 15 Hz and this component at the
        /// frame rate: a beam snapped straight onto every tick's endpoint steps four times a second
        /// across a moving target. <see cref="beamOn"/> is NOT cleared per frame for the same
        /// reason — three frames in four carry no tick, and a beam that went out on each of them
        /// would strobe. It goes out when the channel closes, which is a thing the artifact says
        /// explicitly.
        /// </para>
        /// </summary>
        private Vector3 beamEnd;
        private Vector3 beamShown;
        private bool beamOn;

        /// <summary>How far along this handset's range the target reads, 0…1. Pushed by the item.</summary>
        private float reading;

        /// <summary>
        /// The authored pose of each moving part, captured before anything drives it.
        ///
        /// <para>
        /// `_exportlib.export` bakes no transforms, so an FBX node carries whatever rotation and
        /// scale the source `.blend` gave it — and a hand edit to how the model is seated puts the
        /// correction there too. Driving a part as an ASSIGNMENT throws both away and flattens it on
        /// the first driven frame; driving it as <c>rest * delta</c> does not.
        /// </para>
        /// </summary>
        private Vector3 whipRest = Vector3.one;
        private Quaternion knobRest = Quaternion.identity;

        private MaterialPropertyBlock lampBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Is the channel delivering? Told by the artifact, on every machine.</summary>
        public void SetTransmitting(bool on)
        {
            if (on == delivering) return;

            delivering = on;

            if (on) hum.Play(carrierLoop, gameObject);
            else hum.Stop();
        }

        /// <summary>Which detent the knob is on.</summary>
        public void SetPolarity(bool grow) => wantsGrow = grow;

        /// <summary>
        /// What the TARGET reads, 0…1 — how far along this handset's range the body under the beam
        /// has been driven. Brightens the beam, and nothing else: the handset's own battery has its
        /// own bar on the case and the two must never be confused for each other.
        /// </summary>
        public void SetReading(float progress) => reading = Mathf.Clamp01(progress);

        /// <summary>
        /// Aim the beam at <paramref name="point"/>. It stays on until the channel closes; this
        /// only moves the far end.
        /// </summary>
        public void SetBeam(Vector3 point)
        {
            if (!beamOn) beamShown = point;      // A new beam starts where it is pointed, not
                                                 // where the last one happened to end.
            beamEnd = point;
            beamOn = true;
        }

        /// <summary>Stop drawing the beam. Pushed straight through — see the artifact's Close.</summary>
        public void ClearBeam()
        {
            beamOn = false;
            if (beam != null) beam.enabled = false;
        }

        private void Awake()
        {
            if (whip != null) whipRest = whip.localScale;
            if (knob != null) knobRest = knob.localRotation;
        }

        private void OnDisable()
        {
            hum.Stop(false);
            ClearBeam();

            // Put the driven parts back where the model had them. A handset disabled mid-signal
            // becomes an item lying in the sand, and one lying there with its whip half out is a
            // prop that looks broken.
            open = 0f;
            if (whip != null) whip.localScale = whipRest;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            open = Mathf.MoveTowards(open, delivering ? 1f : 0f, dt / whipSeconds);
            pointer = Mathf.MoveTowards(pointer, wantsGrow ? 1f : -1f, dt / knobSeconds);

            if (whip != null)
            {
                // Along one axis only: the whip telescopes, it does not inflate. Scaling all three
                // would fatten a 4 mm rod into a pole. Each component is the AUTHORED one times a
                // factor, so a model imported at a non-unit scale extends by the same proportion
                // rather than being snapped to this component's idea of one.
                Vector3 axis = whipAxis.normalized;
                float grow = whipExtension * open;
                whip.localScale = new Vector3(whipRest.x * (1f + axis.x * grow),
                                              whipRest.y * (1f + axis.y * grow),
                                              whipRest.z * (1f + axis.z * grow));
            }

            if (knob != null)
            {
                knob.localRotation = knobRest *
                    Quaternion.AngleAxis(knobThrowDegrees * 0.5f * pointer, knobAxis);
            }

            DriveLamp();
            DriveBeam(dt);
        }

        private void DriveLamp()
        {
            if (lamp == null) return;

            Color lit = Color.Lerp(shrinkColour, growColour, (pointer + 1f) * 0.5f);
            Color shown = lit * Mathf.Lerp(idleDim, 1f, open);

            lampBlock ??= new MaterialPropertyBlock();
            lamp.GetPropertyBlock(lampBlock);
            lampBlock.SetColor(BaseColorId, shown);
            lampBlock.SetColor(EmissionColorId, shown);
            lamp.SetPropertyBlock(lampBlock);
        }

        private void DriveBeam(float dt)
        {
            if (beam == null) return;

            if (!beamOn || emitter == null) { beam.enabled = false; return; }

            // Chased rather than assigned — see beamChaseRate. Framerate-independent, because a
            // plain per-frame Lerp factor smooths twice as hard at 120 fps as at 60.
            beamShown = Vector3.Lerp(beamShown, beamEnd, 1f - Mathf.Exp(-beamChaseRate * dt));

            beam.enabled = true;
            beam.positionCount = 2;
            beam.SetPosition(0, emitter.position);
            beam.SetPosition(1, beamShown);

            // The beam brightens with what it has ACHIEVED, not with how long it has been on, so a
            // player watching from across the sand can tell a signal that is working from one
            // wasted on a rock (GDC-L1-FEEL-0004: amplify the real event, do not decorate).
            Color lit = Color.Lerp(shrinkColour, growColour, (pointer + 1f) * 0.5f);
            lit.a = Mathf.Lerp(0.35f, 1f, reading);
            beam.startColor = lit;
            beam.endColor = lit;
        }
    }
}
