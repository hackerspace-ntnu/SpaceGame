// Everything about a bottled singularity that is only LOOKED at.
//
// It is a separate component from SingularityWell for the reason the foam gun's nozzle and the
// slick can's fan are separate from their guns: the well answers "what is happening", and this
// answers "what does that look like", and the two have different owners. The well's half is split
// across three machines by authority; this half runs identically on all of them, off the same
// replicated clock, and never decides anything.
//
// THE IMPORTED-MODEL TRAP. The collar and the core are nodes of an FBX, and _exportlib.export bakes
// no transforms — so each of them carries whatever rotation and scale the .blend gave it, plus
// whatever correction a hand edit put there. Assigning localRotation or localScale outright throws
// that away and flattens the part to identity on the first animated frame, which is what happened
// to the item scanner's dial. Every value here is therefore driven as rest * offset, off a rest
// pose captured before anything moves.
using SpaceGame.Audio;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The bottle's moving parts and its effects: the collar's iris, the core's swell, the inhaled
    /// dust and the burst at the release.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SingularityShell : MonoBehaviour
    {
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
        [Tooltip("The black core behind the glass. Swells through the inhale and snaps flat at the " +
                 "release, so the bottle itself is a readout of how long is left (GDC-L1-SYS-0006).")]
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
        /// The authored pose of the moving parts. Captured before anything is written to them —
        /// see the file header for why a bare assignment is not an option.
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

        private void Awake() => CaptureRest();

        /// <summary>
        /// Draw the bottle as it is right now.
        ///
        /// <para>
        /// Called every frame on every machine, from the well's own <c>Update</c>, and therefore
        /// idempotent: a repeated phase re-draws the same pose and fires nothing. A machine that
        /// joined late and meets the bottle already inhaling gets the open collar on its first call
        /// without the sound, because the sound belongs to a moment it missed.
        /// </para>
        /// </summary>
        /// <param name="progress">
        /// How far through the current phase, 0 to 1. Only the inhale uses it; the other two phases
        /// are poses rather than animations.
        /// </param>
        public void Show(SingularityPhase phase, float progress)
        {
            CaptureRest();

            // A machine seeing this bottle for the first time has missed whatever moment brought it
            // to this phase, so it takes the pose and none of the one-shots.
            bool arrived = drawnValid && phase != drawn;

            drawn = phase;
            drawnValid = true;

            switch (phase)
            {
                case SingularityPhase.Flying:
                    SetCollar(0f);
                    SetCore(1f);
                    SetStream(false);
                    break;

                case SingularityPhase.Inhaling:
                    // Clamped rather than driven off the inhale's own progress, so the iris opens
                    // at its own pace whatever the inhale is tuned to.
                    SetCollar(Mathf.Clamp01(progress * InhaleToCollar));
                    SetCore(Mathf.Lerp(1f, coreSwell, Mathf.Clamp01(progress)));
                    SetStream(true);
                    if (arrived) Play(openSound);
                    break;

                case SingularityPhase.Spent:
                    SetCollar(1f);
                    SetCore(coreCollapse);
                    SetStream(false);
                    if (!arrived) break;

                    Play(releaseSound);
                    if (releaseBurst != null) releaseBurst.Play();
                    break;
            }
        }

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
