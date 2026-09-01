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
    ///
    /// <para>
    /// <b>It starts when the FORMATION launches, not when this machine's player sat down.</b>
    /// Seating happens whenever each client finishes streaming, up to twelve seconds apart, so a
    /// presentation timed from it ran on a different clock on every machine — the host's screen
    /// went black seconds before a client's, on a hull they were both sitting in. Sitting down
    /// still puts the screen to black immediately (the player is meant to be out cold); the timed
    /// half waits for <see cref="Launch"/>, which every machine receives from one server
    /// announcement.
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

        [Tooltip("How long the fade to black takes, and therefore how far BEFORE the impact it " +
                 "starts: it has to be finished at the frame the hull first touches the ground, " +
                 "not begun there. Longer reads as blacking out on the way in, shorter as a cut.")]
        [SerializeField] private float impactFade = 0.6f;

        [Tooltip("How long the wreck spends hitting the ground and toppling flat. Overwritten by " +
                 "ArrivalDirector via Configure, like the descent. The screen is already black by " +
                 "then — this is how long it has to STAY black for the crash to finish behind it.")]
        [SerializeField] private float settleWindow = 1.6f;

        [Tooltip("How long the black holds after the wreck has stopped moving, before the player " +
                 "comes to in it.")]
        [SerializeField] private float blackout = 1.4f;

        [Tooltip("How long the player comes round over, at the end.")]
        [SerializeField] private float recoveryFade = 1f;

        [Tooltip("How long to wait on a black screen for the server's launch announcement before " +
                 "playing anyway. Only ever reached when something upstream is broken — the " +
                 "announcement is sent the moment the crew is aboard — but a player left staring " +
                 "at black forever is a worse failure than beats that are a little out.")]
        [SerializeField] private float launchWait = 30f;

        /// <summary>Set by <see cref="Launch"/>; cleared at the top of every <see cref="Play"/>.</summary>
        private bool launched;

        /// <summary>How long ago the formation launched, at the moment we were told about it.</summary>
        private float launchedSecondsAgo;

        private ArrivalBeats Beats => new(descentDuration, impactFade, settleWindow, blackout);

        /// <summary>
        /// Told by the director how long its descent and its crash actually are, so the beats and
        /// the hull agree even though they are timed on different objects.
        ///
        /// <para>
        /// Both, not just the descent: the hull keeps moving after it first touches the ground — it
        /// comes in nose-down and topples onto its belly — and the black has to cover all of it.
        /// </para>
        /// </summary>
        public void Configure(float duration, float settle)
        {
            descentDuration = Mathf.Max(0.1f, duration);
            settleWindow = Mathf.Max(0f, settle);
        }

        /// <summary>
        /// The formation has launched. Every machine gets this from one server announcement, which
        /// is what makes the sequence start at the same instant everywhere.
        ///
        /// <para>
        /// <paramref name="secondsAgo"/> is zero for everybody who was here when it happened, and
        /// the age of the launch for a late joiner seated into a descent already under way — they
        /// pick the beats up where the hull actually is rather than replaying an entry they missed.
        /// </para>
        /// </summary>
        public void Launch(float secondsAgo)
        {
            launched = true;
            launchedSecondsAgo = Mathf.Max(0f, secondsAgo);
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

            // Black FIRST, and black for as long as it takes the rest of the crew to be strapped
            // in. The player is meant to be coming round, and opening on a clear frame shows them a
            // cabin before telling them they are in one.
            LetterboxOverlay.Instance.FadeToBlackAsync(0f);

            launched = false;
            launchedSecondsAgo = 0f;

            yield return WaitForLaunch();

            ArrivalBeats beats = Beats;
            float startTime = Time.time - launchedSecondsAgo;

            // A late joiner is already past the waking-up beat, so it is collapsed rather than
            // played out of order — they come round at once, in a ship that is already falling.
            // Somebody seated after the impact never comes round at all until the black lifts at
            // the end: showing them the cabin for a frame and blacking it out again would be worse
            // than never showing it.
            if (launchedSecondsAgo < beats.Contact)
            {
                float wake = Mathf.Max(0f, wakeFade - launchedSecondsAgo);
                yield return LetterboxOverlay.Instance.FadeFromBlackAsync(wake);
            }

            yield return Descend(rig, beats, startTime);

            // Contact. The screen is black on this exact frame, and the hull spends the next second
            // and a half toppling off its nose onto its belly behind it. The shake stops with the
            // picture: there is nothing left to shake, and leaving it running would keep moving a
            // camera the player cannot see for the sake of nothing.
            rig.ShakeIntensity = 0f;
            yield return new WaitForSeconds(beats.BlackHold);

            // Removed while the screen is still black, so the camera is back under the player's own
            // control by the first frame they can see. Destroyed rather than disabled: its OnDisable
            // is what returns the camera to a neutral local pose and hands the head back to the
            // body, and a disabled component left attached would be found and re-enabled by a
            // second arrival.
            Destroy(rig);

            yield return LetterboxOverlay.Instance.FadeFromBlackAsync(recoveryFade);
        }

        /// <summary>
        /// Hold on black until the server says the formation is away, or until the wait runs out.
        ///
        /// <para>
        /// Bounded for the same reason every wait in this flow is bounded: an announcement that
        /// never arrives must not leave a player looking at a black screen for the rest of the
        /// session. Giving up plays the beats from now, which is what every machine used to do.
        /// </para>
        /// </summary>
        private IEnumerator WaitForLaunch()
        {
            float waited = 0f;

            while (!launched && waited < launchWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!launched)
                Debug.LogWarning($"[ArrivalCutscene] No launch announcement after {launchWait}s. " +
                                 "Playing the arrival from here, which will not line up with the " +
                                 "hull. Something upstream did not send it — see " +
                                 "SeatedRider.AnnounceLaunch.", this);
        }

        /// <summary>
        /// The dive: shake rising off the curve, and a fade that is timed to FINISH at contact.
        ///
        /// <para>
        /// The fade is started with exactly the time remaining rather than with its authored
        /// length, so a frame spike inside the last second cannot leave the player watching the
        /// impact through a half-faded screen.
        /// </para>
        /// </summary>
        private IEnumerator Descend(ArrivalCameraRig rig, ArrivalBeats beats, float startTime)
        {
            bool fading = false;

            while (true)
            {
                float elapsed = Time.time - startTime;
                if (elapsed >= beats.Contact) break;

                rig.ShakeIntensity = shakeOverDescent.Evaluate(beats.DescentProgress(elapsed));

                if (!fading && elapsed >= beats.FadeStart)
                {
                    fading = true;
                    LetterboxOverlay.Instance.FadeToBlackAsync(beats.Contact - elapsed);
                }

                yield return null;
            }

            // A machine that joined inside the fade window never opened one; the screen still has
            // to be black at contact.
            if (!fading) LetterboxOverlay.Instance.FadeToBlackAsync(0f);
        }
    }
}
