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
        [SerializeField] private float descentDuration = 18.2f;

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

        /// <summary>
        /// Raised on this machine when its own player has come round in the landed wreck — the
        /// recovery fade has finished and the controls are coming back. The reference instant for
        /// anything that times itself from "the cutscene is over", such as the seat-exit hint.
        /// </summary>
        public static event System.Action LocalPlayerRecovered;

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

            // A rig left over from an earlier arrival would be a second writer on the camera's
            // one LateUpdate — the rig now outlives its cutscene (it is the seated look), so a
            // replay has to clear the old one first.
            var stale = cam.gameObject.GetComponent<ArrivalCameraRig>();
            if (stale != null) Destroy(stale);

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

            // TEMPORARY DIAGNOSTIC (2026-09-02) — remove with the others once a clean playtest
            // confirms the blackout. One line per beat, so a missing black can be read straight
            // off the timestamps instead of reasoned about.
            Debug.Log($"[Arrival:DIAG] beats begin t={Time.time:F2} late={launchedSecondsAgo:F2} " +
                      $"contact=+{beats.Contact:F2} fadeStart=+{beats.FadeStart:F2}", this);

            // A late joiner is already past the waking-up beat, so it is collapsed rather than
            // played out of order — they come round at once, in a ship that is already falling.
            // Somebody seated after the impact never comes round at all until the black lifts at
            // the end: showing them the cabin for a frame and blacking it out again would be worse
            // than never showing it.
            //
            // Every fade in this cutscene is FIRE AND FORGET, waited out on time rather than on
            // the overlay's coroutine. Awaiting the fade itself froze this whole sequence when
            // anything else touched the overlay mid-fade (see LetterboxOverlay's generation note);
            // the beats are timings, and time is the only thing they may depend on.
            if (launchedSecondsAgo < beats.Contact)
            {
                float wake = Mathf.Max(0f, wakeFade - launchedSecondsAgo);
                LetterboxOverlay.Instance.FadeFromBlackAsync(wake);
                yield return new WaitForSeconds(wake);
            }

            yield return Descend(rig, beats, startTime);

            // Contact. The screen is black on this exact frame, and the hull spends the next second
            // and a half toppling off its nose onto its belly behind it. The shake stops with the
            // picture: there is nothing left to shake, and leaving it running would keep moving a
            // camera the player cannot see for the sake of nothing.
            if (rig != null) rig.ShakeIntensity = 0f;
            yield return new WaitForSeconds(beats.BlackHold);

            // The rig is NOT destroyed here any more: it is the seated look. PlayerLook spends its
            // yaw rotating the player's body — the wrong thing entirely for someone strapped into
            // a chair — where the rig feeds the look into PlayerHeadLook's clamped neck, which is
            // the whole in-chair look model (HeadAimTests). It hands itself back when the rider
            // stands, which is the moment the body is the player's own again.
            if (rig != null) rig.ReleaseWithSeat();

            // Started while the screen is still black, so the first frame the player can see is
            // already blurred: they are coming to in a wreck, not watching a filter switch on.
            // The component outlives this coroutine on purpose — the cutscene ends with the fade
            // so the player gets their look back, and the blur clears over their first free
            // seconds in the seat.
            ArrivalConcussion.Begin(cam);

            LetterboxOverlay.Instance.FadeFromBlackAsync(recoveryFade);
            yield return new WaitForSeconds(recoveryFade);

            Debug.Log($"[Arrival:DIAG] recovered t={Time.time:F2}", this);

            // The black has lifted and the controls are about to come back: this is the instant
            // "after the cutscene" means to anything timing itself from it — the seat-exit hint
            // waits its own delay from here.
            LocalPlayerRecovered?.Invoke();
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

                // The rig can be destroyed under this coroutine — anything that rebuilds or
                // replaces the player camera mid-descent takes it along. Losing the shake is
                // cosmetic; throwing here used to abort the whole cutscene through the director's
                // catch, which handed the controls back mid-dive and skipped the fade entirely.
                if (rig != null)
                    rig.ShakeIntensity = shakeOverDescent.Evaluate(beats.DescentProgress(elapsed));

                if (!fading && elapsed >= beats.FadeStart)
                {
                    fading = true;
                    LetterboxOverlay.Instance.FadeToBlackAsync(beats.Contact - elapsed);
                    Debug.Log($"[Arrival:DIAG] impact fade opened t={Time.time:F2} " +
                              $"remaining={beats.Contact - elapsed:F2}", this);
                }

                yield return null;
            }

            // TEMPORARY DIAGNOSTIC (2026-09-02) — the alpha BEFORE the snap says whether the
            // scheduled fade actually got there on its own.
            Debug.Log($"[Arrival:DIAG] contact t={Time.time:F2} " +
                      $"alphaBeforeSnap={LetterboxOverlay.Instance.FadeAlpha:F2}", this);

            // Black at contact is the CONTRACT, so it is enforced rather than assumed: the overlay
            // runs one fade at a time and any other system that faded during the descent silently
            // cancelled ours. Snapping a screen that is already black is invisible; snapping one
            // that is not is the whole point.
            LetterboxOverlay.Instance.FadeToBlackAsync(0f);
        }
    }
}
