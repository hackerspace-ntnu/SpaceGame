using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The first seconds after the crash: the world swims back into focus.
    ///
    /// <para>
    /// A full-screen blur that starts opaque under the cutscene's final blackout and clears over
    /// the player's first free moments in the seat, so coming to in the wreck FEELS like coming
    /// to rather than a fade revealing a perfectly crisp cabin (GDC-L1-FEEL-0005). It lives
    /// outside <see cref="ArrivalCutscene"/> on purpose: the cutscene has to end for the player
    /// to get their look back, and the whole point of this beat is looking blearily around the
    /// cabin while it clears.
    /// </para>
    ///
    /// <para>
    /// Built the way <c>PackFocusCamera</c> builds its focus blur — a runtime
    /// <see cref="VolumeProfile"/> with one <see cref="DepthOfField"/>, no asset behind it — but
    /// global on the default layer, because the camera being blurred IS the player's own view.
    /// Gaussian with the far edge pulled almost to the lens, so everything past arm's reach is
    /// fully soft; the weight is what animates, from one to zero, and the volume is destroyed
    /// with the component when it gets there.
    /// </para>
    /// </summary>
    public class ArrivalConcussion : MonoBehaviour
    {
        [Tooltip("Seconds the blur takes to clear, from fully soft to gone.")]
        [SerializeField, Min(0.1f)] private float clearDuration = 9f;

        [Tooltip("Fraction of the clearing spent at FULL blur before it starts to lift. The beat " +
                 "reads as dazed, then recovering; without the hold it reads as a lens wipe.")]
        [SerializeField, Range(0f, 0.9f)] private float holdFraction = 0.3f;

        private Volume volume;
        private VolumeProfile profile;
        private float elapsed;

        /// <summary>
        /// Puts the concussion on <paramref name="cam"/>'s object, restarting one already there —
        /// a second arrival on the same camera means a fresh crash, not a resumed daze.
        /// </summary>
        public static void Begin(Camera cam)
        {
            if (cam == null) return;

            var standing = cam.GetComponent<ArrivalConcussion>();
            if (standing != null)
            {
                standing.elapsed = 0f;
                return;
            }

            cam.gameObject.AddComponent<ArrivalConcussion>();
        }

        private void Awake()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "ArrivalConcussion";

            // Bokeh, not Gaussian: Gaussian's blur radius is capped low and reads as a mild soft
            // filter, where a wide-open bokeh lens focused at arm's length dissolves the cabin
            // into smears of light — which is what a concussion should look like. Focus glued to
            // the lens, longest focal length, widest aperture: everything past the player's own
            // nose is maximally out of focus, and the volume WEIGHT is the one thing that
            // animates it back to sharp.
            DepthOfField dof = profile.Add<DepthOfField>(overrides: true);
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.overrideState = true;
            dof.focusDistance.value = 0.1f;
            dof.focalLength.overrideState = true;
            dof.focalLength.value = 300f;
            dof.aperture.overrideState = true;
            dof.aperture.value = 1f;

            var volumeGo = new GameObject("ArrivalConcussionVolume");
            volumeGo.transform.SetParent(transform, false);

            volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            float hold = clearDuration * holdFraction;
            float clearing = Mathf.Max(0.1f, clearDuration - hold);
            float k = Mathf.Clamp01((elapsed - hold) / clearing);

            volume.weight = 1f - Mathf.SmoothStep(0f, 1f, k);

            if (k >= 1f) Destroy(this);
        }

        private void OnDestroy()
        {
            // The profile is an assetless ScriptableObject; Unity does not collect those with
            // their GameObject, so both are let go explicitly.
            if (volume != null) Destroy(volume.gameObject);
            if (profile != null) Destroy(profile);
        }
    }
}
