using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The lander's computer speaking: each objective step's briefing, one line per dialog popup,
    /// the moment the crew reach it — and, where the step asks, the camera turned to what it is
    /// about.
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
        [Tooltip("Who is speaking. Shown in bold before every line.")]
        [SerializeField] private string speaker = "LANDER";

        [Tooltip("Shortest time a line stays up, seconds. Longer lines stay up as long as they type.")]
        [SerializeField, Min(0.5f)] private float secondsPerLine = 4f;

        [Tooltip("Pause between one line going and the next arriving, seconds.")]
        [SerializeField, Min(0f)] private float gapSeconds = 0.4f;

        [Tooltip("How long the player must have been present — spawned, out of every cutscene — " +
                 "before a briefing starts, seconds. Covers the frame between a body spawning at " +
                 "the hull and being seated in it.")]
        [SerializeField, Min(0f)] private float settleSeconds = 1.5f;

        private readonly Queue<string> pending = new();

        private ObjectiveDirector director;
        private LookAtCutscene lookAt;
        private Transform focusPoint;

        private int briefedStep = -1;
        private bool focusPending;
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
            if (director.Progress.Begun) briefedStep = director.Progress.Step;
        }

        private void OnDisable()
        {
            if (director != null) director.Changed -= OnChanged;
            pending.Clear();
        }

        private void OnChanged(bool live)
        {
            ObjectiveProgress progress = director.Progress;
            if (!progress.Begun || progress.Step == briefedStep) return;

            briefedStep = progress.Step;
            pending.Clear();
            focusPending = false;

            ObjectiveStep step = director.Current;
            if (!live || step == null) return;

            foreach (string line in step.Briefing)
                if (!string.IsNullOrWhiteSpace(line)) pending.Enqueue(line);

            focusPending = step.LookAtFocus;
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

            if (focusPending)
            {
                focusPending = false;
                LookAtFocus(player);
            }

            popup.Show($"<b>{speaker}</b>  {pending.Dequeue()}", secondsPerLine);
            lastVisible = now;
        }

        private static bool IsPresent(PlayerController player) =>
            player != null && (CutsceneDirector.Instance == null || !CutsceneDirector.Instance.IsPlaying);

        /// <summary>
        /// Turns this player's camera to the step's focus and back. The first line of the briefing
        /// shows while it turns; the rest wait, because the look is a cutscene and the player is not
        /// "present" until it ends.
        /// </summary>
        private void LookAtFocus(PlayerController player)
        {
            ObjectiveStep step = director.Current;
            if (step == null || CutsceneDirector.Instance == null) return;
            if (!step.TryGetFocus(director.World, out Vector3 focus)) return;

            focusPoint.position = focus;
            CutsceneDirector.Instance.Play(lookAt, player.gameObject);
        }
    }
}
