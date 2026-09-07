// Everything the slick can visibly and audibly does while it is spraying.
//
// Split off the artifact so that the item is about where the film lands and this is about what the
// can looks like laying it. It holds no gameplay state and decides nothing: it is told whether the
// can is emitting and eases the model, the jet and the loop toward that.
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// The can's moving parts: the trigger under the finger, the nozzle that flares as the fan
    /// widens, the jet of film and the hiss it makes.
    ///
    /// <para>
    /// Driven by <see cref="SlickCanArtifact"/> on every machine, from a flag every machine already
    /// has — the hold stream reaches all of them. Nothing here is sent, and nothing here is asked
    /// about who owns the can: a peer watching somebody else spray sees the same nozzle open and
    /// hears the same hiss, which is the ordinary <c>Present</c> half of an artifact.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlickCanNozzle : MonoBehaviour
    {
        [Header("Jet")]
        [Tooltip("The film leaving the nozzle. Played while the can is emitting and stopped when " +
                 "it is not; the particles already in the air are left to finish.")]
        [SerializeField] private ParticleSystem jet;

        [Tooltip("Cone half-angle the jet emits into with the nozzle shut, in degrees.")]
        [SerializeField, Range(0f, 90f)] private float shutConeDegrees = 6f;

        [Tooltip("Cone half-angle with the nozzle fully open, in degrees. Match this to the " +
                 "artifact's own fan half-angle, or the film lands somewhere the jet is not.")]
        [SerializeField, Range(0f, 90f)] private float openConeDegrees = 30f;

        [Header("Moving parts")]
        [Tooltip("The flare at the front of the can, scaled up as the fan widens. Optional.")]
        [SerializeField] private Transform nozzle;

        [Tooltip("How much bigger the flare gets while spraying, as a multiple of its authored size.")]
        [SerializeField, Range(1f, 2f)] private float nozzleFlare = 1.3f;

        [Tooltip("The trigger under the finger, turned while the can is emitting. Optional.")]
        [SerializeField] private Transform trigger;

        [Tooltip("How far the trigger swings when pulled, in degrees about its own axes.")]
        [SerializeField] private Vector3 triggerPullDegrees = new Vector3(-16f, 0f, 0f);

        [Header("Feel")]
        [Tooltip("Seconds for the nozzle to open once the trigger goes down.")]
        [SerializeField] private float openSeconds = 0.07f;

        [Tooltip("Seconds for it to shut again. Slower than opening, so the jet trails off rather " +
                 "than being cut, which is what a pressurised can actually does.")]
        [SerializeField] private float shutSeconds = 0.18f;

        [Header("Audio")]
        [Tooltip("The hiss, looped while the can is emitting. There is no slick-can event in the " +
                 "catalog and no way to author one, so this borrows the portal can's spray loop.")]
        [SerializeField] private SfxId sprayLoop = SfxId.PortalSprayLoop;

        private readonly LoopingEmitter hiss = new LoopingEmitter();

        /// <summary>0 shut, 1 fully open. Everything below is a function of this one number.</summary>
        private float open;

        private bool emitting;

        /// <summary>
        /// The authored pose of each moving part, captured before anything drives it.
        ///
        /// <para>
        /// Both are applied as an offset FROM these rather than as a bare assignment. An FBX node
        /// carries whatever rotation and scale the source file gave it — the export bakes nothing —
        /// so writing a rotation or a scale straight onto an imported part destroys how the model
        /// was seated, and the part snaps out of place on the first frame the can is used.
        /// </para>
        /// </summary>
        private Quaternion triggerRest = Quaternion.identity;
        private Vector3 nozzleRest = Vector3.one;

        private void Awake()
        {
            if (trigger != null) triggerRest = trigger.localRotation;
            if (nozzle != null) nozzleRest = nozzle.localScale;
        }

        /// <summary>
        /// Is the can putting film out right now?
        ///
        /// Pushed once a frame by the artifact rather than raised as an event, so a machine that
        /// missed the tick which started or ended a spray is put right by the next frame instead of
        /// hissing for ever. Only a CHANGE acts, because the jet and the loop are both handles
        /// rather than values, and re-stopping a stopped one sixty times a second is work that says
        /// nothing.
        /// </summary>
        public void SetSpraying(bool spraying)
        {
            if (spraying == emitting) return;

            emitting = spraying;

            if (spraying) StartJet();
            else StopJet(immediate: false);
        }

        private void OnDisable()
        {
            // A can put away mid-spray — a hotbar change is the ordinary way — must not leave a
            // voice running or a jet emitting on an object that is about to be destroyed.
            emitting = false;
            open = 0f;
            StopJet(immediate: true);
            Apply();
        }

        // Reachable from OnDisable and OnDestroy both: a loop cleaned up in only one of them leaks
        // whenever the object goes away through the other.
        private void OnDestroy() => hiss.Stop(false);

        private void Update()
        {
            // A can nobody is spraying writes nothing. Not an optimisation: an idle component that
            // keeps assigning a rest pose every frame is one that quietly wins any argument with
            // whatever else might come to drive the same parts.
            if (!emitting && open <= 0f) return;

            float seconds = emitting ? openSeconds : shutSeconds;

            open = Mathf.MoveTowards(open, emitting ? 1f : 0f,
                                     Time.deltaTime / Mathf.Max(0.001f, seconds));

            Apply();
        }

        /// <summary>Push <see cref="open"/> onto every part that reads it.</summary>
        private void Apply()
        {
            if (nozzle != null)
                nozzle.localScale = nozzleRest * Mathf.Lerp(1f, nozzleFlare, open);

            if (trigger != null)
                trigger.localRotation = triggerRest * Quaternion.Euler(triggerPullDegrees * open);

            if (jet != null)
            {
                ParticleSystem.ShapeModule shape = jet.shape;
                shape.angle = Mathf.Lerp(shutConeDegrees, openConeDegrees, open);
            }
        }

        private void StartJet()
        {
            if (jet != null && !jet.isEmitting) jet.Play();

            hiss.Play(sprayLoop, gameObject);
        }

        private void StopJet(bool immediate)
        {
            if (jet != null)
            {
                // Stop EMITTING, not stop dead: the film already in the air belongs to a spray that
                // really happened and should be allowed to land.
                jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (immediate) jet.Clear(true);
            }

            hiss.Stop(!immediate);
        }
    }
}
