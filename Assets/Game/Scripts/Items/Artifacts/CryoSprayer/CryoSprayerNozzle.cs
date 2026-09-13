// Everything the cryo sprayer visibly and audibly does.
//
// Split off the artifact so that the item is about where the cold lands and this is about what the
// gun looks like putting it there. It holds no gameplay state and decides nothing: it is told
// whether the valve is open and what the plume is landing on, and eases the model, the jet and the
// loop toward that.
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Items
{
    /// <summary>
    /// The sprayer's moving parts: the trigger under the finger, the vapour plume, what happens
    /// where the plume lands, the rime creeping back along the barrel, and the hiss.
    ///
    /// <para>
    /// Driven by <see cref="CryoSprayerArtifact"/> on every machine, from flags every machine
    /// already has — the hold stream reaches all of them. Nothing here is sent and nothing here
    /// asks who owns the gun: a peer watching somebody else spray sees the same plume and hears
    /// the same hiss, which is the ordinary <c>Present</c> half of an artifact.
    /// </para>
    /// <para>
    /// <b>The landing is the readable half.</b> The cold takes hold of bodies and of nothing else,
    /// and a rule with no visible consequence is superstition rather than depth (GDC-L1-SYS-0006).
    /// So the plume ends in one of two bursts — frost biting into a body, or vapour blowing off
    /// the ground and leaving nothing behind — and the player learns what the gun is for by
    /// watching it rather than by being told.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CryoSprayerNozzle : MonoBehaviour
    {
        [Header("Plume")]
        [Tooltip("The vapour leaving the nozzle. Played while the valve is open and stopped when " +
                 "it is not; the particles already in the air are left to finish.")]
        [SerializeField] private ParticleSystem plume;

        [Tooltip("Cone half-angle the plume emits into with the valve shut, in degrees.")]
        [SerializeField, Range(0f, 90f)] private float shutConeDegrees = 3f;

        [Tooltip("Cone half-angle with the valve fully open, in degrees. This is the plume the " +
                 "player SEES, and it must match the cone that freezes — " +
                 "CryoSprayerArtifact.coneHalfAngle, which warns when the two drift apart. Wide " +
                 "enough that a target near the crosshair is inside the spray, because everything " +
                 "the plume covers now freezes at the same rate.")]
        [SerializeField, Range(0f, 90f)] private float openConeDegrees = 22f;

        /// <summary>
        /// The spread the plume is drawn at, for the artifact to check its own freeze cone against.
        /// Vapour that visibly washes over a creature and does nothing to it is the whole bug this
        /// exists to make loud.
        /// </summary>
        public float OpenConeDegrees => openConeDegrees;

        [Header("Landing")]
        [Tooltip("Frost forming where the plume lands. Played at the hit point while the cold is " +
                 "biting, which is on a body and nowhere else.\n\n" +
                 "Both landing systems are MOVED to the hit point every sweep, so both must " +
                 "simulate in WORLD space: in local space the particles already in the air are " +
                 "dragged along with the system and the frost smears across the ground.")]
        [SerializeField] private ParticleSystem bite;

        [Tooltip("Vapour blowing off a surface, which is every surface: ground, a wall, a deck " +
                 "plate. This is the refusal, shown rather than explained. World simulation " +
                 "space, for the reason above.")]
        [SerializeField] private ParticleSystem blowoff;

        [Header("Rime")]
        [Tooltip("Parts of the gun that frost over while it sprays: the fins and the last few " +
                 "centimetres of the barrel. Each must use a material with a _Freeze property — " +
                 "the frozen-statue shader — so that a rime shell authored over the barrel is " +
                 "clipped away entirely at 0 and covers it at 1, the same property and the same " +
                 "meaning as the ice on what the gun is pointed at.")]
        [SerializeField] private Renderer[] rimedParts;

        [Tooltip("Seconds of continuous spraying for the rime to reach full. Slower than the " +
                 "valve, because frost builds up over a burst rather than appearing with it.")]
        [SerializeField, Min(0.01f)] private float rimeSeconds = 1.6f;

        [Tooltip("How frosted the barrel stays when nobody is spraying. Deliberately not zero: " +
                 "the rimed muzzle is one of the three non-colour cues that tell this gun from " +
                 "the foam gun at a glance, and a clean barrel at rest throws it away for the " +
                 "one instance the player is actually holding. The material is authored at 1 so " +
                 "the prefab, the icon, the pack mat and the gun lying in the sand keep the cue " +
                 "with nothing running; this is what keeps the live instance honest too.")]
        [SerializeField, Range(0f, 1f)] private float restRime = 0.35f;

        [Tooltip("Seconds for the rime to clear once the gun is idle.")]
        [SerializeField, Min(0.01f)] private float clearSeconds = 3f;

        [Header("Moving parts")]
        [Tooltip("The trigger under the finger, pulled while the valve is open. Optional.")]
        [SerializeField] private Transform trigger;

        [Tooltip("How far the trigger swings when pulled, in degrees about its own axes.")]
        [SerializeField] private Vector3 triggerPullDegrees = new Vector3(-16f, 0f, 0f);

        [Header("Feel")]
        [Tooltip("Seconds for the valve to open once the trigger goes down.")]
        [SerializeField, Min(0.001f)] private float openSeconds = 0.06f;

        [Tooltip("Seconds for it to shut again. Slower than opening, so the plume trails off " +
                 "rather than being cut, which is what a pressurised bottle actually does.")]
        [SerializeField, Min(0.001f)] private float shutSeconds = 0.2f;

        [Header("Audio")]
        [Tooltip("The hiss, looped while the valve is open. There is no cryo event in the catalog " +
                 "and no way to author one — the Studio project is lost — so this borrows the " +
                 "portal can's spray loop.")]
        [SerializeField] private SfxId sprayLoop = SfxId.PortalSprayLoop;

        private readonly LoopingEmitter hiss = new LoopingEmitter();

        /// <summary>0 shut, 1 fully open. The plume and the trigger are functions of this.</summary>
        private float open;

        /// <summary>0 clean, 1 fully frosted. Its own, slower clock — see <see cref="rimeSeconds"/>.</summary>
        private float rime;

        private bool emitting;

        /// <summary>The last rime pushed onto the model, so an unchanged frame writes nothing.</summary>
        private float paintedRime = -1f;

        private MaterialPropertyBlock block;

        /// <summary>
        /// The authored pose of the trigger, captured before anything drives it.
        ///
        /// Applied as an offset FROM this rather than as a bare assignment. An FBX node carries
        /// whatever rotation the source file gave it — the export bakes nothing — so writing a
        /// rotation straight onto an imported part destroys how the model was seated, and the part
        /// snaps out of place on the first frame the gun is used.
        /// </summary>
        private Quaternion triggerRest = Quaternion.identity;

        private void Awake()
        {
            if (trigger != null) triggerRest = trigger.localRotation;
        }

        /// <summary>
        /// Is the gun putting vapour out right now?
        ///
        /// Pushed once a frame by the artifact rather than raised as an event, so a machine that
        /// missed the tick which started or ended a spray is put right by the next frame instead of
        /// hissing for ever. Only a CHANGE acts, because the plume and the loop are handles rather
        /// than values, and re-stopping a stopped one sixty times a second says nothing.
        /// </summary>
        public void SetSpraying(bool spraying)
        {
            if (spraying == emitting) return;

            emitting = spraying;

            if (spraying) StartPlume();
            else StopPlume(immediate: false);
        }

        /// <summary>
        /// Where the plume is landing and whether the cold is taking hold there.
        ///
        /// <para>
        /// Three states rather than two: nothing in reach at all, a body the cold bites into, and
        /// a surface that shrugs it off. The middle and the last are what tell the player that
        /// this gun is pointed at creatures, before they have committed a tank to finding out.
        /// </para>
        /// </summary>
        public void SetLanding(bool landed, Vector3 point, bool sticking)
        {
            Land(bite, landed && sticking, point);
            Land(blowoff, landed && !sticking, point);
        }

        private void OnDisable()
        {
            // A gun put away mid-spray — a hotbar change is the ordinary way — must not leave a
            // voice running or a jet emitting on an object that is about to be destroyed.
            emitting = false;
            open = 0f;
            rime = 0f;
            StopPlume(immediate: true);
            SetLanding(false, Vector3.zero, false);
            Apply();
        }

        // Reachable from OnDisable and OnDestroy both: a loop cleaned up in only one of them leaks
        // whenever the object goes away through the other.
        private void OnDestroy() => hiss.Stop(false);

        private void Update()
        {
            // A gun nobody is spraying and whose rime has cleared writes nothing. Not an
            // optimisation: a component that keeps assigning a rest pose every frame is one that
            // quietly wins any argument with whatever else might come to drive the same parts.
            // ... but it must have written the rest pose at least once, or a gun that has never
            // been fired keeps the material's authored _Freeze while one that has cooled shows
            // restRime, and the same gun reads two different ways for no reason the player can see.
            if (!emitting && open <= 0f && rime <= 0f && paintedRime >= 0f) return;

            float deltaTime = Time.deltaTime;

            open = Mathf.MoveTowards(open, emitting ? 1f : 0f,
                                     deltaTime / (emitting ? openSeconds : shutSeconds));

            rime = Mathf.MoveTowards(rime, emitting ? 1f : 0f,
                                     deltaTime / (emitting ? rimeSeconds : clearSeconds));

            Apply();
        }

        /// <summary>Push <see cref="open"/> and <see cref="rime"/> onto every part that reads them.</summary>
        private void Apply()
        {
            if (trigger != null)
                trigger.localRotation = triggerRest * Quaternion.Euler(triggerPullDegrees * open);

            if (plume != null)
            {
                ParticleSystem.ShapeModule shape = plume.shape;
                shape.angle = Mathf.Lerp(shutConeDegrees, openConeDegrees, open);
            }

            PaintRime();
        }

        private void PaintRime()
        {
            if (rimedParts == null || rimedParts.Length == 0) return;

            // The player reads frost on a barrel to nothing finer than a percent, and a property
            // block is a write per renderer, so a value that cannot change a pixel is skipped.
            if (Mathf.Abs(rime - paintedRime) < 0.005f) return;

            paintedRime = rime;

            if (block == null) block = new MaterialPropertyBlock();

            foreach (Renderer part in rimedParts)
            {
                if (part == null) continue;

                part.GetPropertyBlock(block);
                block.SetFloat(FrostShell.FreezeProperty, Mathf.Lerp(restRime, 1f, rime));
                part.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Put one landing effect at <paramref name="point"/> and run it, or stop it. Moved rather
        /// than re-instantiated: the plume lands somewhere new fifteen times a second.
        /// </summary>
        private static void Land(ParticleSystem effect, bool running, Vector3 point)
        {
            if (effect == null) return;

            if (running)
            {
                effect.transform.position = point;
                if (!effect.isEmitting) effect.Play();
                return;
            }

            if (effect.isEmitting)
                effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void StartPlume()
        {
            if (plume != null && !plume.isEmitting) plume.Play();

            hiss.Play(sprayLoop, gameObject);
        }

        private void StopPlume(bool immediate)
        {
            if (plume != null)
            {
                // Stop EMITTING, not stop dead: the vapour already in the air belongs to a spray
                // that really happened and should be allowed to disperse.
                plume.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (immediate) plume.Clear(true);
            }

            hiss.Stop(!immediate);
        }
    }
}
