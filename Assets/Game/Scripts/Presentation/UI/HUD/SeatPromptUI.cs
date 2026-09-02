// "Q to exit the ship" — shown while this machine's own player is sitting in a landed ship and
// may get up.
//
// It exists because the crash landing is the one seat in the game you are put into rather than
// choosing to sit in, so nothing has taught you how to leave it. Every other seat is entered by
// walking up to a chair and pressing a key, which is its own lesson; this one arrives with the
// player already in it, at the opening of the game, with no prior instruction to fall back on.
//
// POLLED, never event-driven, and that is a lesson paid for: this component lives on the player
// HUD, which is deactivated around exactly the moments the arrival announces things — so an
// event-based version subscribed after the announcements it needed had already fired, and the
// hint simply never appeared. Every frame this asks for the current truth instead: is the seat
// leavable (SeatedRider.LocalPlayerMayLeave), and is the cutscene over (CutsceneDirector). Timing
// it a beat after the blackout lifts (GDC-L1-UX-0001: the lesson at the moment it matters) lets
// the player take in the wreck first and read the way out second.
//
// Decides nothing and draws nothing itself — SeatedRider owns whether the key does anything, and
// PlayerHints owns the pixels. This component only owns WHEN the arrival's one hint shows.
using UnityEngine;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class SeatPromptUI : MonoBehaviour
    {
        private const string HintId = "arrival-seat-exit";

        [Tooltip("Seconds after the player has come round — the cutscene finishing — before the " +
                 "hint appears.")]
        [SerializeField, Min(0f)] private float delayAfterRecovery = 3f;

        [Tooltip("Backstop delay from the seat becoming leavable, for a session whose cutscene " +
                 "never ends or never played — the way out must not depend on a presentation. " +
                 "Longer than the healthy blackout-plus-delay path so it never wins when the " +
                 "cutscene is fine.")]
        [SerializeField, Min(0f)] private float delayWithoutRecovery = 10f;

        [SerializeField] private string hintText = "<color=#FFD980><b>Q</b></color>  exit the ship";

        private float mayLeaveSince = -1f;
        private float cutsceneOverSince = -1f;
        private bool shown;

        private void OnDisable() => TakeDown();

        private void Update()
        {
            if (!SeatedRider.LocalPlayerMayLeave)
            {
                TakeDown();
                return;
            }

            float now = Time.unscaledTime;
            if (mayLeaveSince < 0f) mayLeaveSince = now;

            // "The cutscene is over" is observed, not subscribed to, and only counts once the seat
            // is leavable — a cutscene that was never going to play reads as over from the start,
            // which is exactly right: the hint then simply waits its delay.
            bool cutsceneRunning = CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying;
            if (!cutsceneRunning && cutsceneOverSince < 0f) cutsceneOverSince = now;

            if (shown) return;

            bool due = cutsceneOverSince >= 0f
                ? now >= cutsceneOverSince + delayAfterRecovery
                : now >= mayLeaveSince + delayWithoutRecovery;

            if (!due) return;

            shown = true;
            PlayerHints.Show(HintId, hintText);
        }

        private void TakeDown()
        {
            if (shown) PlayerHints.Hide(HintId);

            shown = false;
            mayLeaveSince = -1f;
            cutsceneOverSince = -1f;
        }
    }
}
