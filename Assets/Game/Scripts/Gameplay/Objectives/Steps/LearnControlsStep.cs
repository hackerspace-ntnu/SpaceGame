using System;
using System.Text;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The first thing after the crash: every player tries the basic controls once. The visor
    /// lists them under the objective and strikes each one through the moment it is used; the step
    /// is met when EVERY player in the session has used all of them.
    ///
    /// <para>
    /// Teaching by doing (<c>GDC-L1-UX-0001</c>): the list is short, it is read off the visor while
    /// playing rather than from a screen that stops play, and each line confirms itself as soon as
    /// the player does what it says (<c>GDC-L1-UX-0003</c>).
    /// </para>
    /// <para>
    /// <b>Input is local; the step is shared.</b> Each machine watches its own player's input in
    /// <see cref="TrackLocalPlayer"/>, and once all of it has been used the director reports that
    /// player to the server (<see cref="ObjectiveNetwork"/>). The server only ever counts reports.
    /// Nothing is saved: a reload mid-lesson asks for the controls again, which takes seconds.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Learn Controls Step")]
    public class LearnControlsStep : ObjectiveStep
    {
        [Flags]
        public enum Control
        {
            None   = 0,
            Move   = 1 << 0,
            Sprint = 1 << 1,
            Jump   = 1 << 2,
            Crouch = 1 << 3,
        }

        [Serializable]
        private struct Lesson
        {
            public Control control;

            [Tooltip("The key, drawn bold: \"WASD\".")]
            public string keys;

            [Tooltip("What it does: \"move\".")]
            public string action;
        }

        [SerializeField] private Lesson[] lessons =
        {
            new() { control = Control.Move,   keys = "WASD",  action = "move" },
            new() { control = Control.Sprint, keys = "Shift", action = "sprint while moving" },
            new() { control = Control.Jump,   keys = "Space", action = "jump" },
            new() { control = Control.Crouch, keys = "C",     action = "crouch" },
        };

        [Tooltip("Stick deflection that counts as moving, 0 to 1.")]
        [SerializeField, Range(0.1f, 1f)] private float moveThreshold = 0.5f;

        [Tooltip("Shown under the list once this player is done but somebody else is not.")]
        [SerializeField] private string waitingForCrew = "Waiting for the rest of the crew";

        [Tooltip("Opacity of a lesson already done, 0 to 1. It is also struck through.")]
        [SerializeField, Range(0f, 1f)] private float doneOpacity = 0.4f;

        public override bool IsMet(ObjectiveWorld world) => world.EveryoneFinished;

        public override bool TrackLocalPlayer(ObjectiveWorld world)
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player != null && player.Input != null && IsStanding())
                world.LocalControlsUsed |= Used(player.Input);

            return HasLearntAll(world.LocalControlsUsed);
        }

        public override string Status(ObjectiveWorld world)
        {
            Control used = world.LocalControlsUsed;
            string doneAlpha = Mathf.RoundToInt(doneOpacity * 255f).ToString("X2");
            var text = new StringBuilder();

            foreach (Lesson lesson in lessons)
            {
                if (text.Length > 0) text.Append('\n');

                bool done = (used & lesson.control) != 0;
                if (done) text.Append("<alpha=#").Append(doneAlpha).Append("><s>");
                text.Append("<b>").Append(lesson.keys).Append("</b>  ").Append(lesson.action);
                if (done) text.Append("</s><alpha=#FF>");
            }

            if (HasLearntAll(used)) text.Append("\n\n").Append(waitingForCrew);
            return text.ToString();
        }

        /// <summary>What the player is doing this frame, as controls.</summary>
        private Control Used(PlayerInputManager input)
        {
            bool moving = input.MoveInput.sqrMagnitude >= moveThreshold * moveThreshold;

            Control used = Control.None;
            if (moving) used |= Control.Move;
            if (moving && input.SprintHeld) used |= Control.Sprint;
            if (input.JumpHeld) used |= Control.Jump;
            if (input.CrouchHeld) used |= Control.Crouch;
            return used;
        }

        private bool HasLearntAll(Control used)
        {
            foreach (Lesson lesson in lessons)
                if ((used & lesson.control) == 0) return false;

            return true;
        }

        /// <summary>
        /// Out of the seat and out of every cutscene. A player strapped in cannot walk, so a key
        /// pressed there proves nothing; the way out of the seat is taught by its own prompt.
        /// </summary>
        private static bool IsStanding() =>
            !SeatedRider.LocalPlayerMayLeave
            && (CutsceneDirector.Instance == null || !CutsceneDirector.Instance.IsPlaying);
    }
}
