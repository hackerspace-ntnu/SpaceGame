using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Each objective step's briefing, one line per dialog popup, the moment the crew reach it —
    /// spoken by its chain's <see cref="ObjectiveChain.Speaker"/> (the lander's computer for the
    /// main chain) — and, where the step asks, the camera turned to what it is about.
    ///
    /// <para>
    /// <b>Per machine, nothing on the wire.</b> Every machine hears the same step change through
    /// <see cref="ObjectiveDirector.Changed"/> and plays the briefing to its own player. A step
    /// reached by a restore or learned on joining is NOT briefed (<c>live</c> is false): the words
    /// belong to the moment, and a late joiner reads the objective off the visor instead.
    /// </para>
    /// <para>
    /// <b>Only to a player who is there to hear it</b> — spawned, out of every cutscene, for
    /// <see cref="settleSeconds"/> on end. The first briefing is due the instant the hull comes to
    /// rest, under the blackout, and would otherwise type itself out to a black screen.
    /// Interruptible by design (<c>GDC-L1-NARR-0005</c>): lines wait for the player, never the
    /// other way round, and nothing here takes their controls except the short look at the focus.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LookAtCutscene))]
    public class ObjectiveBriefing : MonoBehaviour
    {
        [Tooltip("Shortest time a line stays up, seconds. Longer lines stay up as long as they type.")]
        [SerializeField, Min(0.5f)] private float secondsPerLine = 4f;

        [Tooltip("Pause between one line going and the next arriving, seconds.")]
        [SerializeField, Min(0f)] private float gapSeconds = 0.4f;

        [Tooltip("How long the player must have been present — spawned, out of every cutscene — " +
                 "before a briefing starts, seconds. Covers the frame between a body spawning at " +
                 "the hull and being seated in it.")]
        [SerializeField, Min(0f)] private float settleSeconds = 1.5f;

        private readonly Queue<string> pending = new();

        /// <summary>Per run, the step last briefed (or found already begun); -1 for none.</summary>
        private int[] briefedStep = System.Array.Empty<int>();

        private ObjectiveDirector director;
        private LookAtCutscene lookAt;
        private Transform focusPoint;

        private ObjectiveRun focusRun;
        private float presentSince = -1f;
        private float lastVisible = float.NegativeInfinity;
        private bool reportedMissingPopup;

        private void Awake()
        {
            lookAt = GetComponent<LookAtCutscene>();

            // What the look turns to is somewhere on a ship that was spawned, not authored, so the
            // cutscene is pointed at a marker this component moves there.
            focusPoint = new GameObject("BriefingFocus").transform;
            focusPoint.SetParent(transform, false);
            lookAt.Target = focusPoint;
        }

        private void OnEnable()
        {
            director = ObjectiveDirector.Instance;
            if (director == null) return;

            director.Changed += OnChanged;

            // Read the state as it is now: a step already begun when this wakes was reached before
            // anybody here could have heard it.
            briefedStep = new int[director.Runs.Count];
            foreach (ObjectiveRun run in director.Runs)
                briefedStep[run.Index] = run.Progress.Begun ? run.Progress.Step : -1;
        }

        private void OnDisable()
        {
            if (director != null) director.Changed -= OnChanged;
            pending.Clear();
            focusRun = null;
        }

        /// <summary>
        /// A newer step's briefing replaces whatever is still queued, from any chain: the crew have
        /// moved on, and lines about the step they left would only mislead.
        /// </summary>
        private void OnChanged(ObjectiveRun run, bool live)
        {
            ObjectiveProgress progress = run.Progress;
            if (!progress.Begun || progress.Step == briefedStep[run.Index]) return;

            briefedStep[run.Index] = progress.Step;

            ObjectiveStep step = run.Current;
            if (!live || step == null) return;

            pending.Clear();
            focusRun = null;

            string speaker = run.Chain.Speaker;
            foreach (string line in step.Briefing)
                if (!string.IsNullOrWhiteSpace(line))
                    pending.Enqueue(string.IsNullOrEmpty(speaker) ? line : $"<b>{speaker}</b>  {line}");

            if (step.LookAtFocus) focusRun = run;
        }

        private void Update()
        {
            if (pending.Count == 0) return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (!IsPresent(player))
            {
                presentSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (presentSince < 0f) presentSince = now;
            if (now - presentSince < settleSeconds) return;

            NpcDialogPopupUI popup = NpcDialogPopupUI.Instance;
            if (popup == null)
            {
                if (!reportedMissingPopup)
                {
                    Debug.LogError("[Objectives] No NpcDialogPopupUI in the scene, so the lander's " +
                                   "briefings cannot be shown. The objectives still work.", this);
                    reportedMissingPopup = true;
                }

                pending.Clear();
                return;
            }

            if (popup.IsVisible)
            {
                lastVisible = now;
                return;
            }

            if (now < lastVisible + gapSeconds) return;

            if (focusRun != null)
            {
                LookAtFocus(player, focusRun);
                focusRun = null;
            }

            popup.Show(pending.Dequeue(), secondsPerLine);
            lastVisible = now;
        }

        private static bool IsPresent(PlayerController player) =>
            player != null && (CutsceneDirector.Instance == null || !CutsceneDirector.Instance.IsPlaying);

        /// <summary>
        /// Turns this player's camera to the step's focus and back. The first line of the briefing
        /// shows while it turns; the rest wait, because the look is a cutscene and the player is not
        /// "present" until it ends.
        /// </summary>
        private void LookAtFocus(PlayerController player, ObjectiveRun run)
        {
            ObjectiveStep step = run.Current;
            if (step == null || CutsceneDirector.Instance == null) return;
            if (!step.TryGetFocus(run.World, out Vector3 focus)) return;

            focusPoint.position = focus;
            CutsceneDirector.Instance.Play(lookAt, player.gameObject);
        }
    }
}
