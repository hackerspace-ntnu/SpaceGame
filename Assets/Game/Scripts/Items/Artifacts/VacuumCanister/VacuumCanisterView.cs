// Everything the canister itself visibly and audibly does.
//
// Split off the artifact so that the item is about what goes in the bottle and this is about what
// the bottle looks like taking it. It holds no gameplay state and decides nothing: it is told
// whether the draught is running and eases the shutter, the latch and the loop toward that.
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// The canister's moving parts: the shutter that irises open over the mouth, the latch that
    /// throws when something goes in, the captive moving behind the glass, and the draught's loop.
    ///
    /// <para>
    /// Driven by <see cref="VacuumCanisterArtifact"/> on every machine, from a flag every machine
    /// already has — the hold stream reaches all of them. Nothing here is sent, and nothing here
    /// asks who owns the canister: a peer watching somebody else draw sees the same shutter open
    /// and hears the same loop, which is the ordinary <c>Present</c> half of an artifact.
    /// </para>
    /// <para>
    /// <b>The glass is a prefab difference, not a runtime one.</b> A full canister is a different
    /// item with a different model, so the captive behind the window exists on the full prefab and
    /// is simply absent from the empty one. This only gives whatever is in there a small, restless
    /// motion — the joke does not land if it stands still (<c>GDC-L1-UX-0003</c>: the interface,
    /// here diegetic, has to answer "what have I got?" at a glance).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VacuumCanisterView : MonoBehaviour
    {
        [Header("Mouth")]
        [Tooltip("The shutter over the mouth. Turned and widened as the draught opens. Optional.")]
        [SerializeField] private Transform shutter;

        [Tooltip("How far the shutter turns about its own axes when fully open, in degrees. An " +
                 "iris reads as a twist plus a widening, so this is normally a spin about the " +
                 "mouth's own axis.")]
        [SerializeField] private Vector3 shutterOpenDegrees = new Vector3(0f, 0f, 75f);

        [Tooltip("How much wider the shutter opening gets, as a multiple of its authored size.")]
        [SerializeField, Range(1f, 3f)] private float shutterFlare = 1.35f;

        [Tooltip("The intake swirl at the mouth. Played while the draught is running; whatever is " +
                 "already in the air is left to finish. Optional.")]
        [SerializeField] private ParticleSystem intake;

        [Header("Latch")]
        [Tooltip("The heavy latch on top. Thrown shut when something goes in and thrown open on " +
                 "an uncork. Optional.")]
        [SerializeField] private Transform latch;

        [Tooltip("How far the latch swings between open and shut, in degrees about its own axes.")]
        [SerializeField] private Vector3 latchThrowDegrees = new Vector3(-52f, 0f, 0f);

        [Tooltip("Tick this on the FULL canister's prefab. It is what the latch starts thrown for, " +
                 "so a canister that arrives in the hand already holding something is not seen " +
                 "slamming itself shut over a capture that happened a minute ago.")]
        [SerializeField] private bool startsLatched;

        [Header("Captive")]
        [Tooltip("Whatever is drawn behind the glass on the FULL canister. Absent on the empty " +
                 "one. Nudged about so it reads as alive rather than as a decal.")]
        [SerializeField] private Transform captiveFigure;

        [Tooltip("How far the captive drifts inside the glass, in metres.")]
        [SerializeField, Min(0f)] private float captiveDrift = 0.012f;

        [Tooltip("How quickly it moves. Low is a creature sulking, high is one still fighting.")]
        [SerializeField, Min(0f)] private float captiveRate = 1.6f;

        [Header("Feel")]
        [Tooltip("Seconds for the shutter to iris open once the trigger goes down.")]
        [SerializeField, Min(0.001f)] private float openSeconds = 0.09f;

        [Tooltip("Seconds for it to close again. Slower than opening, so the draught trails off " +
                 "rather than being cut.")]
        [SerializeField, Min(0.001f)] private float shutSeconds = 0.2f;

        [Tooltip("Seconds the latch takes to swing. Fast: it is a mechanism throwing, not a lid " +
                 "settling.")]
        [SerializeField, Min(0.001f)] private float latchSeconds = 0.12f;

        [Header("Audio")]
        [Tooltip("The draught, looped while the canister is drawing. There is no vacuum event in " +
                 "the catalog and no way to author one, so this borrows the wing pack's wind loop.")]
        [SerializeField] private SfxId draughtLoop = SfxId.WingsWindLoop;

        [Tooltip("The thump of something going in and the latch throwing. Borrowed from the " +
                 "oxygen plant's fill-complete chime.")]
        [SerializeField] private SfxId foldSound = SfxId.InteractOxygenFilled;

        [Tooltip("The latch being thrown back. Borrowed from the lever.")]
        [SerializeField] private SfxId uncorkSound = SfxId.InteractLever;

        private readonly LoopingEmitter draught = new LoopingEmitter();

        /// <summary>0 shut, 1 fully open. Everything the mouth does is a function of this number.</summary>
        private float open;

        /// <summary>0 open, 1 thrown. The latch's own eased position.</summary>
        private float thrown;

        /// <summary>What the latch is easing towards.</summary>
        private bool latched;

        private bool sucking;

        /// <summary>
        /// Per-instance offset into the captive's noise, so two full canisters on a shelf do not
        /// twitch in lockstep.
        /// </summary>
        private float captiveSeed;

        /// <summary>
        /// The authored pose of each moving part, captured before anything drives it.
        ///
        /// <para>
        /// All three are applied as an offset FROM these rather than as a bare assignment. An FBX
        /// node carries whatever rotation, scale and position the source file gave it — the export
        /// bakes nothing — so writing one straight onto an imported part destroys how the model was
        /// seated, and the part snaps out of place on the first frame the canister is used.
        /// </para>
        /// </summary>
        private Quaternion shutterRest = Quaternion.identity;
        private Vector3 shutterRestScale = Vector3.one;
        private Quaternion latchRest = Quaternion.identity;
        private Vector3 captiveRest;

        private void Awake()
        {
            if (shutter != null)
            {
                shutterRest = shutter.localRotation;
                shutterRestScale = shutter.localScale;
            }

            if (latch != null) latchRest = latch.localRotation;
            if (captiveFigure != null) captiveRest = captiveFigure.localPosition;

            latched = startsLatched;
            thrown = latched ? 1f : 0f;
            captiveSeed = Random.value * 1000f;

            Apply();
        }

        /// <summary>
        /// Is the canister drawing right now?
        ///
        /// Pushed once a frame by the artifact rather than raised as an event, so a machine that
        /// missed the tick which started or ended a draw is put right by the next frame instead of
        /// roaring for ever. Only a CHANGE acts, because the swirl and the loop are handles rather
        /// than values, and re-stopping a stopped one sixty times a second is work that says
        /// nothing.
        /// </summary>
        public void SetSucking(bool drawing)
        {
            if (drawing == sucking) return;

            sucking = drawing;

            if (drawing) StartDraught();
            else StopDraught(immediate: false);
        }

        /// <summary>Something went in: throw the latch and thump.</summary>
        public void PlayFold()
        {
            latched = true;
            Sfx.Play(foldSound, transform.position, GetInstanceID());
        }

        /// <summary>Something came out: throw the latch back.</summary>
        public void PlayUncork()
        {
            latched = false;
            Sfx.Play(uncorkSound, transform.position, GetInstanceID());
        }

        private void OnDisable()
        {
            // A canister put away mid-draw — a hotbar change is the ordinary way — must not leave a
            // voice running or a swirl emitting on an object that is about to be destroyed.
            sucking = false;
            open = 0f;
            StopDraught(immediate: true);
            Apply();
        }

        // Reachable from OnDisable and OnDestroy both: a loop cleaned up in only one of them leaks
        // whenever the object goes away through the other.
        private void OnDestroy() => draught.Stop(false);

        private void Update()
        {
            // The captive keeps moving whatever the canister is doing — that is the whole of it
            // being alive in there.
            if (captiveFigure != null) DriftCaptive();

            float target = latched ? 1f : 0f;

            // A canister nobody is using writes nothing to the mechanism. Not an optimisation: an
            // idle component that keeps assigning a rest pose every frame is one that quietly wins
            // any argument with whatever else might come to drive the same parts.
            if (!sucking && open <= 0f && Mathf.Approximately(thrown, target)) return;

            open = Mathf.MoveTowards(open, sucking ? 1f : 0f,
                                     Time.deltaTime / (sucking ? openSeconds : shutSeconds));

            thrown = Mathf.MoveTowards(thrown, target, Time.deltaTime / latchSeconds);

            Apply();
        }

        /// <summary>Push the two eased numbers onto the mouth and the latch.</summary>
        private void Apply()
        {
            if (shutter != null)
            {
                shutter.localRotation = shutterRest * Quaternion.Euler(shutterOpenDegrees * open);
                shutter.localScale = shutterRestScale * Mathf.Lerp(1f, shutterFlare, open);
            }

            if (latch != null)
                latch.localRotation = latchRest * Quaternion.Euler(latchThrowDegrees * thrown);
        }

        /// <summary>
        /// Nudge whatever is behind the glass.
        ///
        /// Three incommensurable rates rather than one, so the drift never settles into a rhythm
        /// the eye can predict and start reading as a loop.
        /// </summary>
        private void DriftCaptive()
        {
            float t = Time.time * captiveRate;

            captiveFigure.localPosition = captiveRest + new Vector3(
                Mathf.PerlinNoise(captiveSeed, t) * 2f - 1f,
                Mathf.PerlinNoise(captiveSeed + 31.7f, t * 1.37f) * 2f - 1f,
                Mathf.PerlinNoise(captiveSeed + 71.3f, t * 0.83f) * 2f - 1f) * captiveDrift;
        }

        private void StartDraught()
        {
            if (intake != null && !intake.isEmitting) intake.Play();

            draught.Play(draughtLoop, gameObject);
        }

        private void StopDraught(bool immediate)
        {
            if (intake != null)
            {
                // Stop EMITTING, not stop dead: the dust already in the air belongs to a draught
                // that really happened and should be allowed to land.
                intake.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (immediate) intake.Clear(true);
            }

            draught.Stop(!immediate);
        }
    }
}
