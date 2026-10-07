using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Lags the whole visor a few pixels behind the player's head turn, then eases it back.
    ///
    /// <para>
    /// This is the one thing that makes the layer read as light projected on glass in front of the
    /// eye rather than as a flat overlay drawn on the monitor. It is deliberately tiny: motion is a
    /// signal, not a texture, and idle movement that rises above the threshold of noticing starts
    /// competing with the movement that means something — see <c>GDC-L1-FEEL-0004</c>'s recorded
    /// disagreement, which is the reason this layer has one ambient effect rather than five.
    /// </para>
    /// <para>
    /// Honours <see cref="GameSettings.ReduceVisorMotion"/>, which is a vestibular-accessibility
    /// control in the same family as camera shake, not a polish dial.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VisorSway : MonoBehaviour
    {
        [Tooltip("Peak offset, in reference pixels, reached at a fast head turn.")]
        [SerializeField, Min(0f)] private float pixels = VisorStyle.SwayPixels;

        [Tooltip("How quickly the layer eases back to centre, per second.")]
        [SerializeField, Min(0.1f)] private float recovery = VisorStyle.SwayRecovery;

        [Tooltip("Degrees per second of head turn that produces the peak offset. Higher means the " +
                 "visor only lags on a fast whip round, lower means it drifts on any movement.")]
        [SerializeField, Min(1f)] private float degreesForFullSway = 220f;

        private RectTransform rect;
        private Quaternion lastRotation;
        private Vector2 offset;
        private bool hasLast;

        private void Awake() => rect = (RectTransform)transform;

        private void OnDisable()
        {
            // Left where it was, a disabled layer comes back offset. Also drops the stale rotation
            // so the first frame after re-enabling does not read as an enormous turn.
            offset = Vector2.zero;
            hasLast = false;
            if (rect != null) rect.anchoredPosition = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (rect == null) return;

            if (GameSettings.ReduceVisorMotion)
            {
                offset = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                hasLast = false;
                return;
            }

            // Camera.main is re-read every frame rather than cached, which is what lets the visor
            // follow the player onto a mount and back off again.
            Camera view = Camera.main;
            if (view == null) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Quaternion now = view.transform.rotation;

            if (!hasLast)
            {
                lastRotation = now;
                hasLast = true;
                return;
            }

            // Yaw and pitch separately: a head turn drags the layer sideways, a look up or down
            // drags it vertically. Signed, so the lag trails the movement rather than leading it.
            Vector3 delta = (Quaternion.Inverse(lastRotation) * now).eulerAngles;
            float yawRate = Mathf.DeltaAngle(0f, delta.y) / dt;
            float pitchRate = Mathf.DeltaAngle(0f, delta.x) / dt;
            lastRotation = now;

            Vector2 target = new(
                Mathf.Clamp(-yawRate / degreesForFullSway, -1f, 1f) * pixels,
                Mathf.Clamp(pitchRate / degreesForFullSway, -1f, 1f) * pixels);

            // Exponential rather than a fixed lerp factor, so the ease is frame-rate independent.
            offset = Vector2.Lerp(offset, target, 1f - Mathf.Exp(-recovery * dt));
            rect.anchoredPosition = offset;
        }
    }
}
