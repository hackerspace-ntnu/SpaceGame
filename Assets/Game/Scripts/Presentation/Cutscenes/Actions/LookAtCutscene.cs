using System.Collections;
using UnityEngine;

namespace SpaceGame.Presentation
{
    // Smoothly rotates the player camera to face a target, holds for duration, restores.
    // Sample cutscene — proves the Director pipeline end-to-end with no other dependencies.
    public class LookAtCutscene : Cutscene
    {
        [SerializeField] private Transform target;
        [SerializeField] private float blendInDuration = 0.6f;
        [SerializeField] private float holdDuration = 1.5f;
        [SerializeField] private float blendOutDuration = 0.6f;

        /// <summary>
        /// What to look at. Settable for callers whose target only exists at runtime — the
        /// objective briefing aims at a hull socket on a ship that was spawned, not authored.
        /// </summary>
        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public override IEnumerator Play(CutsceneContext ctx)
        {
            if (target == null || ctx.PlayerCamera == null)
                yield break;

            Transform cam = ctx.PlayerCamera.transform;
            Quaternion startRot = cam.rotation;
            Quaternion lookRot = Quaternion.LookRotation(target.position - cam.position);

            yield return BlendRotation(cam, startRot, lookRot, blendInDuration);
            yield return new WaitForSecondsRealtime(holdDuration);
            yield return BlendRotation(cam, lookRot, startRot, blendOutDuration);
        }

        private static IEnumerator BlendRotation(Transform t, Quaternion from, Quaternion to, float duration)
        {
            if (duration <= 0f)
            {
                t.rotation = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                t.rotation = Quaternion.Slerp(from, to, k);
                yield return null;
            }
            t.rotation = to;
        }
    }
}
