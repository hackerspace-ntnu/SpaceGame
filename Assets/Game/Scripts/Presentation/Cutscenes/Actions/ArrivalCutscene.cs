using System.Collections;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// What the arrival looks like from inside the cabin.
    ///
    /// <para>
    /// Presentation ONLY. It moves no ship and seats no player — the hull is flown by
    /// <c>ArrivalDirector</c> on the server and replicated, and the bodies are put in their chairs
    /// by <c>SeatedRider</c>. This runs on each machine for its own player, and the game would still
    /// be correct if it were deleted mid-flight. That property is exactly what keeps a cutscene out
    /// of networked state, and it is the pattern any other cutscene that has to agree across
    /// machines should copy.
    /// </para>
    ///
    /// <para>
    /// The descent length is passed in by the director rather than authored here, so retuning the
    /// descent cannot silently desynchronise the beats from the hull they describe.
    /// </para>
    /// </summary>
    public class ArrivalCutscene : Cutscene
    {
        [Tooltip("How long the descent takes. Overwritten by ArrivalDirector via Configure — the " +
                 "director is the authority. The value here is only what a lone play-test sees.")]
        [SerializeField] private float descentDuration = 26f;

        [Tooltip("Fade up from black over this long at the start, as the player comes round.")]
        [SerializeField] private float wakeFade = 1.6f;

        [Tooltip("Shake through the descent, sampled by normalised time. Starts near zero, builds " +
                 "through the entry buffet, and is at its peak for the ground rush. Capped and " +
                 "scaled by the player's own shake setting inside ShakeMath.")]
        [SerializeField] private AnimationCurve shakeOverDescent = new(
            new Keyframe(0f, 0.05f),
            new Keyframe(0.35f, 0.25f),
            new Keyframe(0.75f, 0.55f),
            new Keyframe(1f, 1f));

        [Tooltip("How long the wreck spends hitting the ground and toppling flat. Overwritten by " +
                 "ArrivalDirector via Configure, like the descent, because the crash is a beat of " +
                 "the hull's own and the screen must not go black in the middle of it.")]
        [SerializeField] private float settleWindow = 1.6f;

        [Tooltip("How long the screen holds at full shake AFTER the wreck has stopped moving, " +
                 "before it goes black.")]
        [SerializeField] private float impactHold = 0.35f;

        [Tooltip("How long the black holds after impact, before the player comes to in the wreck.")]
        [SerializeField] private float blackout = 1.4f;

        /// <summary>
        /// Told by the director how long its descent and its crash actually are, so the beats and
        /// the hull agree even though they are timed on different objects.
        ///
        /// <para>
        /// Both, not just the descent: the hull now keeps moving after it first touches the ground
        /// — it comes in nose-down and topples onto its belly — and a screen that faded on the old
        /// cue would black out halfway through the crash it exists to show.
        /// </para>
        /// </summary>
        public void Configure(float duration, float settle)
        {
            descentDuration = Mathf.Max(0.1f, duration);
            settleWindow = Mathf.Max(0f, settle);
        }

        public override IEnumerator Play(CutsceneContext ctx)
        {
            Camera cam = ctx.PlayerCamera;
            if (cam == null)
            {
                Debug.LogError("[ArrivalCutscene] No player camera; the arrival cannot be shown.");
                yield break;
            }

            var rig = cam.gameObject.AddComponent<ArrivalCameraRig>();

            // Black FIRST, then fade up. The player is meant to be coming round, and opening on a
            // clear frame shows them a cabin before telling them they are in one.
            LetterboxOverlay.Instance.FadeToBlackAsync(0f);
            yield return LetterboxOverlay.Instance.FadeFromBlackAsync(wakeFade);

            float startTime = Time.time;

            while (Time.time - startTime < descentDuration)
            {
                float t = Mathf.Clamp01((Time.time - startTime) / descentDuration);
                rig.ShakeIntensity = shakeOverDescent.Evaluate(t);
                yield return null;
            }

            // Impact, and then the crash: the hull is still moving through the settle window, so
            // the shake stays at its peak for the whole of it rather than only for the frame of
            // first contact.
            rig.ShakeIntensity = 1f;
            yield return new WaitForSeconds(settleWindow + impactHold);

            yield return LetterboxOverlay.Instance.FadeToBlackAsync(0.12f);

            rig.ShakeIntensity = 0f;
            yield return new WaitForSeconds(blackout);

            // Removed while the screen is still black, so the camera is back under the player's own
            // control by the first frame they can see. Destroyed rather than disabled: its OnDisable
            // is what returns the camera to a neutral local pose, and a disabled component left
            // attached would be found and re-enabled by a second arrival.
            Destroy(rig);

            yield return LetterboxOverlay.Instance.FadeFromBlackAsync(1f);
        }
    }
}
