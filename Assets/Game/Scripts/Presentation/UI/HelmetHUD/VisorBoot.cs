using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The visor's power-on: a short brightness rise the first time the layer appears.
    ///
    /// <para>
    /// Purely visual. It never gates input and never delays a readout being legible
    /// (<c>GDC-L1-ANIM-0002</c> — animation must not block the player): the gauges are readable
    /// from the first frame because the sweep starts at <see cref="startAlpha"/> rather than at
    /// zero, and simply rides over them on the way up.
    /// </para>
    /// <para>
    /// Runs on unscaled time, like every other UI animation in the project, so it still plays
    /// while a solo session has time frozen behind a menu.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class VisorBoot : MonoBehaviour
    {
        [Tooltip("Seconds the rise takes.")]
        [SerializeField, Min(0f)] private float seconds = VisorStyle.BootSeconds;

        [Tooltip("Alpha the layer starts at. Deliberately not zero — the readouts must be legible " +
                 "from the first frame, so this is a rise, not a fade-in.")]
        [SerializeField, Range(0f, 1f)] private float startAlpha = 0.4f;

        private CanvasGroup group;
        private float elapsed;
        private bool running;

        private void Awake() => group = GetComponent<CanvasGroup>();

        private void OnEnable()
        {
            elapsed = 0f;
            running = !GameSettings.ReduceVisorMotion;
            if (group != null) group.alpha = running ? startAlpha : 1f;
        }

        private void OnDisable()
        {
            // A layer switched off mid-rise must not come back half-lit if motion is turned off
            // in the meantime.
            running = false;
            if (group != null) group.alpha = 1f;
        }

        private void Update()
        {
            if (!running || group == null) return;

            elapsed += Time.unscaledDeltaTime;
            float t = seconds <= 0f ? 1f : Mathf.Clamp01(elapsed / seconds);
            group.alpha = Mathf.Lerp(startAlpha, 1f, t);

            if (t >= 1f) running = false;
        }
    }
}
