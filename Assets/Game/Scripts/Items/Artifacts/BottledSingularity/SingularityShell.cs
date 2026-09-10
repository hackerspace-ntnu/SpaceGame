// Everything about a bottled singularity that is only LOOKED at.
//
// It is a separate component from SingularityWell for the reason the foam gun's nozzle and the
// cryo sprayer's plume are separate from their guns: the well answers "what is happening", and this
// answers "what does that look like", and the two have different owners. The well's half is split
// across three machines by authority; this half runs identically on all of them, off the same
// replicated clock, and never decides anything.
//
// THE SPHERE IS THE WHOLE READOUT. A player has no HUD for this thing and no reason to count
// seconds, so every fact they need is a property of one shape: how big it is says how far the pull
// reaches, growing says it is still winding up, white says it is open, black says it is closing,
// gone says everything it took is gone with it, and white again says here it comes
// (GDC-L1-ANIM-0003 — a readable-but-plain animation beats a gorgeous illegible one, and the
// animation IS the fair warning). The ring is there to give a featureless sphere an orientation and
// a sense of scale; a smooth white ball has neither.
//
// THE IMPORTED-MODEL TRAP. The collar and the core are nodes of an FBX, and _exportlib.export bakes
// no transforms — so each of them carries whatever rotation and scale the .blend gave it, plus
// whatever correction a hand edit put there. Assigning localRotation or localScale outright throws
// that away and flattens the part to identity on the first animated frame, which is what happened
// to the item scanner's dial. Every value driven on THOSE is therefore rest * offset, off a rest
// pose captured before anything moves. The sphere and the ring are the exception and are assigned
// outright: they are generated primitives with no authored transform to lose (SingularityBuilder).
using SpaceGame.Audio;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The bottle's moving parts and its effects: the event horizon and its ring, the collar's
    /// iris, the core's swell, the inhaled dust and the burst at the release.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SingularityShell : MonoBehaviour
    {
        [Header("Horizon")]
        [Tooltip("The half-transparent ball that IS the singularity. Scaled to the well's own " +
                 "radius, so it is not a decoration standing near the effect — it is the effect's " +
                 "reach, drawn (GDC-L1-SYS-0006).")]
        [SerializeField] private Transform sphere;

        [Tooltip("The renderer whose colour is driven from white to black. Found on the sphere " +
                 "when unset.")]
        [SerializeField] private Renderer sphereRenderer;

        [Tooltip("The flat black ring around it. Saturn's, and for Saturn's reason: a smooth ball " +
                 "has no orientation and no readable size, and a ring gives it both.")]
        [SerializeField] private Transform ring;

        [Tooltip("How far the ring reaches as a multiple of the sphere's own radius. Above one so " +
                 "it stands clear of the surface rather than z-fighting against it.")]
        [SerializeField, Min(1f)] private float ringSpan = 1.9f;

        [Tooltip("How far the ring is tipped out of level, degrees. Not zero: a ring seen exactly " +
                 "edge-on from a standing player's eye is an invisible ring.")]
        [SerializeField] private float ringTilt = 24f;

        [Tooltip("Degrees a second the ring turns about its own axis. Slow — it is there to say " +
                 "the thing is alive, not to spin (GDC-L1-ANIM-0005).")]
        [SerializeField] private float ringSpin = 18f;

        [Header("Horizon colour")]
        [Tooltip("The open horizon: white, and half-transparent so the pile being dragged in is " +
                 "still visible through it. A solid ball would hide the joke.")]
        [SerializeField] private Color openColour = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("The closing horizon. Completely black and completely opaque — the moment it " +
                 "stops being see-through is the moment the player stops being able to help " +
                 "whoever is inside.")]
        [SerializeField] private Color shutColour = new Color(0f, 0f, 0f, 1f);

        [Tooltip("Share of the collapse spent turning black, before any of it is spent shrinking. " +
                 "Small: the colour change is the announcement and the shrink is the consequence.")]
        [SerializeField, Range(0.01f, 1f)] private float blackenShare = 0.18f;

        [Header("Collar")]
        [Tooltip("The machined ring around the mouth. Twisted open when the bottle lands and left " +
                 "open — the iris is what says the bottle is live, so it must not animate shut " +
                 "while the thing is still inhaling.")]
        [SerializeField] private Transform collar;

        [Tooltip("How far the collar twists when the iris opens, in degrees about its own up axis.")]
        [SerializeField] private float collarOpenDegrees = 75f;

        [Tooltip("Seconds the iris takes to open. Short: the opening is the cue that the pull has " +
                 "started, and a cue that arrives after the effect is a cue nobody reads.")]
        [SerializeField, Min(0.01f)] private float collarOpenSeconds = 0.18f;

        [Header("Core")]
        [Tooltip("The black core behind the glass — the little black hole the bottle becomes. " +
                 "Swells through the inhale and snaps flat at the collapse, so the bottle itself " +
                 "is a readout of how long is left (GDC-L1-SYS-0006).")]
        [SerializeField] private Transform core;

        [Tooltip("Multiplier on the core's authored size at the end of the inhale.")]
        [SerializeField, Min(1f)] private float coreSwell = 2.4f;

        [Tooltip("Multiplier on the core's authored size once the bottle is spent. Under one: the " +
                 "core collapses rather than merely stopping, which is the visible half of " +
                 "everything being let go at once.")]
        [SerializeField, Range(0f, 1f)] private float coreCollapse = 0.15f;

        [Header("Effects")]
        [Tooltip("Dust and grit falling inward. Played while the bottle inhales, stopped when it " +
                 "stops.")]
        [SerializeField] private ParticleSystem inhaleStream;

        [Tooltip("The outward burst at the release. One shot.")]
        [SerializeField] private ParticleSystem releaseBurst;

        [Header("Audio")]
        [Tooltip("The iris opening. Played once, on every machine, at the moment the bottle lands.")]
        [SerializeField] private SfxId openSound = SfxId.AmbAntigravity;

        [Tooltip("The knot letting go. Played once, on every machine.")]
        [SerializeField] private SfxId releaseSound = SfxId.ImpactExplosion;

        /// <summary>
        /// The authored pose of the FBX parts. Captured before anything is written to them — see
        /// the file header for why a bare assignment is not an option on those two.
        /// </summary>
        private Quaternion collarRest;
        private Vector3 coreRest;
        private bool restCaptured;

        /// <summary>
        /// What the last <see cref="Show"/> was told, so the one-shots fire on the transition
        /// rather than on every frame of the phase they belong to.
        ///
        /// Starts at <see cref="SingularityPhase.Flying"/> because that is where every bottle
        /// starts, including one a machine first sees mid-flight.
        /// </summary>
        private SingularityPhase drawn = SingularityPhase.Flying;
        private bool drawnValid;

        /// <summary>
        /// Written through a block rather than onto the material, so every bottle on screen shares
        /// one material and none of them leaks an instance. <c>_BaseColor</c> is URP's name; the
        /// built-in <c>_Color</c> is written too so the same component survives a shader swap.
        /// </summary>
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColourId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock colourBlock;

        /// <summary>How far the ring has turned. Local and cosmetic; nothing reads it back.</summary>
        private float ringAngle;

        private void Awake()
        {
            CaptureRest();

            if (sphereRenderer == null && sphere != null)
                sphereRenderer = sphere.GetComponentInChildren<Renderer>();
        }

        /// <summary>
        /// Draw the bottle as it is right now.
        ///
        /// <para>
        /// Called every frame on every machine, from the well's own <c>Update</c>, and therefore
        /// idempotent: a repeated phase re-draws the same pose and fires nothing. A machine that
        /// joined late and meets the bottle already inhaling gets the open horizon on its first
        /// call without the sound, because the sound belongs to a moment it missed.
        /// </para>
        /// </summary>
        /// <param name="progress">How far through the current phase, 0 to 1.</param>
        /// <param name="radius">
        /// The well's own reach in metres. Passed in rather than serialized twice: the sphere IS
        /// the radius, and two numbers that have to agree are one number that will not.
        /// </param>
        public void Show(SingularityPhase phase, float progress, float radius)
        {
            CaptureRest();

            // A machine seeing this bottle for the first time has missed whatever moment brought it
            // to this phase, so it takes the pose and none of the one-shots.
            bool arrived = drawnValid && phase != drawn;

            drawn = phase;
            drawnValid = true;

            float shut = Mathf.Clamp01(progress);

            switch (phase)
            {
                case SingularityPhase.Flying:
                    SetHorizon(0f, openColour);
                    SetCollar(0f);
                    SetCore(1f);
                    SetStream(false);
                    break;

                case SingularityPhase.Inhaling:
                    // Clamped rather than driven off the inhale's own progress, so the iris opens
                    // at its own pace whatever the inhale is tuned to.
                    SetCollar(Mathf.Clamp01(progress * InhaleToCollar));
                    SetCore(Mathf.Lerp(1f, coreSwell, shut));
                    SetStream(true);

                    // Eased out, so the horizon arrives at its full reach rather than slamming into
                    // it — the reach is the thing the player is judging, and a linear grow reads as
                    // still growing right up to the last frame.
                    SetHorizon(radius * EaseOut(shut), openColour);

                    if (arrived) Play(openSound);
                    break;

                case SingularityPhase.Flaring:
                    SetCollar(1f);
                    SetCore(coreSwell);
                    SetStream(true);
                    SetHorizon(radius * Mathf.Lerp(1f, 2f, EaseOut(shut)), openColour);
                    break;

                case SingularityPhase.Collapsing:
                    SetCollar(1f);
                    SetCore(Mathf.Lerp(coreSwell, coreCollapse, shut));
                    SetStream(false);

                    // Black first, then small. Both run off the same progress, and the colour is
                    // finished inside the first fifth of it — see blackenShare.
                    SetHorizon(radius * 2f * (1f - EaseIn(shut)),
                               Color.Lerp(openColour, shutColour,
                                          Mathf.Clamp01(shut / Mathf.Max(blackenShare, 0.01f))));
                    break;

                case SingularityPhase.Held:
                    SetCollar(1f);
                    SetCore(coreCollapse);
                    SetStream(false);
                    SetHorizon(0f, shutColour);
                    break;

                case SingularityPhase.Spitting:
                    SetCollar(1f);
                    SetCore(coreCollapse);
                    SetStream(false);

                    // Out and back inside the split second: the ball is a flash, not a phase.
                    SetHorizon(radius * Mathf.Sin(shut * Mathf.PI), openColour);

                    if (!arrived) break;

                    Play(releaseSound);
                    if (releaseBurst != null) releaseBurst.Play();
                    break;

                case SingularityPhase.Spent:
                    SetCollar(1f);
                    SetCore(coreCollapse);
                    SetStream(false);
                    SetHorizon(0f, shutColour);
                    break;
            }
        }

        /// <summary>
        /// Put the horizon at <paramref name="horizonRadius"/> metres and <paramref name="colour"/>.
        ///
        /// <para>
        /// The mesh is a unit sphere, so a radius is twice the scale — the one conversion this file
        /// makes, in one place, because getting it wrong is a singularity drawn at half its reach
        /// and nothing in the console. A radius of zero switches the objects off outright rather
        /// than drawing a degenerate speck: a zero-scaled transform still costs a draw call and
        /// still confuses anything that reads bounds.
        /// </para>
        /// </summary>
        private void SetHorizon(float horizonRadius, Color colour)
        {
            bool visible = horizonRadius > 1e-3f;

            if (sphere != null)
            {
                if (sphere.gameObject.activeSelf != visible) sphere.gameObject.SetActive(visible);
                if (visible) sphere.localScale = Vector3.one * (horizonRadius * 2f);
            }

            if (ring != null)
            {
                if (ring.gameObject.activeSelf != visible) ring.gameObject.SetActive(visible);

                if (visible)
                {
                    ring.localScale = Vector3.one * (horizonRadius * 2f * ringSpan);
                    ring.localRotation = Quaternion.Euler(ringTilt, ringAngle, 0f);
                }
            }

            if (!visible || sphereRenderer == null) return;

            colourBlock ??= new MaterialPropertyBlock();
            sphereRenderer.GetPropertyBlock(colourBlock);
            colourBlock.SetColor(BaseColourId, colour);
            colourBlock.SetColor(ColourId, colour);
            sphereRenderer.SetPropertyBlock(colourBlock);
        }

        private void Update() => ringAngle = Mathf.Repeat(ringAngle + ringSpin * Time.deltaTime, 360f);

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private static float EaseIn(float t) => t * t;

        /// <summary>
        /// How much of the inhale one iris opening is worth, so <see cref="Show"/> can turn the
        /// inhale's progress into the collar's without being told the inhale's length.
        ///
        /// Guarded against a zero open time by the field's own <c>Min</c>; the inhale is always
        /// longer than the opening in any sane tuning, and if it is not the iris simply snaps.
        /// </summary>
        private float InhaleToCollar => 1f / Mathf.Max(collarOpenSeconds, 0.01f);

        private void CaptureRest()
        {
            if (restCaptured) return;

            collarRest = collar != null ? collar.localRotation : Quaternion.identity;
            coreRest = core != null ? core.localScale : Vector3.one;
            restCaptured = true;
        }

        /// <summary>Iris twist, 0 shut to 1 open, applied on top of the authored rotation.</summary>
        private void SetCollar(float open)
        {
            if (collar == null) return;

            // About local Z, not Y. Every node in an _exportlib FBX carries the Blender turn
            // (Rx -90), so this bottle's own axis is mesh-local +Z: a Y twist rotates about
            // prefab-space (0, 0, -1) and tumbles the collar ACROSS the bottle rather than
            // turning it about the neck. Measured on the built prefab -- Euler(0, 75, 0) gives
            // an axis dotting 0.000 with the bottle's axis, Euler(0, 0, 75) dots 1.000.
            collar.localRotation = collarRest *
                                   Quaternion.Euler(0f, 0f, collarOpenDegrees * Mathf.Clamp01(open));
        }

        /// <summary>Core size as a multiple of its authored scale.</summary>
        private void SetCore(float multiple)
        {
            if (core == null) return;

            core.localScale = coreRest * multiple;
        }

        private void SetStream(bool running)
        {
            if (inhaleStream == null) return;

            if (running && !inhaleStream.isPlaying) inhaleStream.Play();
            else if (!running && inhaleStream.isPlaying) inhaleStream.Stop();
        }

        /// <summary>
        /// One layer of the moment, at the bottle. Keyed on this component so the two one-shots
        /// are not deduped against each other by the catalog's per-source rate limit.
        /// </summary>
        private void Play(SfxId id)
        {
            if (id == SfxId.None) return;

            Sfx.Play(id, transform.position, GetInstanceID());
        }
    }
}
