using System;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One question, two or three answers, and Back.
    ///
    /// Generalises <c>MultiplayerChoiceUI</c>, which asked exactly one question — host or join —
    /// before the front menu grew a second front. The same shape is now needed three times over:
    /// Story asks singleplayer-or-multiplayer, Multiplayer asks host-or-join, VS asks host-or-join
    /// again. Three bespoke screens differing only in a title and two callbacks is the copy-paste
    /// CLAUDE.md forbids, so this is the one class all three route through.
    ///
    /// <para>
    /// It exists at all because of what MultiplayerChoiceUI's own doc explained: "Multiplayer" used
    /// to open the world list directly, so a player who only wanted to join a friend had to invent a
    /// world of their own first — and having done that, arrived in the lobby carrying a staged save
    /// that the host's world would then load over. There was no route into the lobby that did not go
    /// through picking a world, which is why joining looked impossible. Asking host-or-join before
    /// anything else, on a screen with no other job, is what fixed it — and every other fork this
    /// menu now offers needs the identical shape.
    /// </para>
    ///
    /// <para>
    /// Built from <see cref="MenuScreen"/>'s shared page skeleton — <c>Title</c>, <c>Column</c>,
    /// <c>Entry</c> — rather than its own copy of the anchors and constants. This screen sits between
    /// the main menu and whatever it routes to — the world list, <see cref="LobbyUI"/>, another
    /// instance of itself — all of which clone the menu's own button prefab and draw in its navy
    /// palette; a screen in the middle with its own colours and its own type scale reads as a seam in
    /// a flow that is supposed to be one.
    /// </para>
    /// </summary>
    public class MenuChoiceUI : MenuScreen
    {
        /// <summary>One answer: what it says, and what happens when it is picked.</summary>
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly Action Go;

            public Choice(string label, Action go)
            {
                Label = label;
                Go = go;
            }
        }

        private string title;
        private Choice[] choices;

        /// <summary>
        /// Opens the screen with a title and its answers. Back is added automatically and must not
        /// be passed in as one of <paramref name="choices"/>.
        /// </summary>
        public static MenuChoiceUI Open(MainMenuUI owner, string title, params Choice[] choices)
        {
            var existing = FindFirstObjectByType<MenuChoiceUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(MenuChoiceUI)).AddComponent<MenuChoiceUI>();
            ui.title = title;

            // A null params array can only arrive from a caller passing `null` explicitly — the
            // compiler turns an empty call into a zero-length array on its own — but a page that
            // silently NullReferenceExceptions on a bad call site is a worse failure than one that
            // opens with nothing to click but Back and says so in the console.
            ui.choices = choices ?? Array.Empty<Choice>();
            if (ui.choices.Length == 0)
                Debug.LogWarning($"[MenuChoiceUI] '{title}' opened with no choices — only Back will show.");

            ui.Present(owner);
            return ui;
        }

        // ---------------------------------------------------------------- actions

        /// <summary>
        /// Closes this screen, then runs the chosen route.
        ///
        /// Closing first is not optional. <see cref="MenuScreen.Close"/> puts the menu's canvases
        /// back on; whatever this hands off to — a world list, a lobby, another choice page —
        /// switches off whatever it finds enabled and restores it on its own way out. A screen left
        /// alive here would sit underneath whatever opens next forever, holding canvases off that
        /// only it remembers having hidden.
        ///
        /// <paramref name="go"/> arrives as a parameter, already captured by the button's closure
        /// before this runs — not read from a field on <c>this</c> after <see cref="MenuScreen.Close"/>
        /// has destroyed it. Reaching for a field on the object at that point would be reading a
        /// field on a corpse; a parameter captured ahead of time is not.
        /// </summary>
        private void Pick(Action go)
        {
            Close();
            go?.Invoke();
        }

        // ----------------------------------------------------------------- build

        protected override void Build()
        {
            Title(title);

            RectTransform column = Column();

            for (int i = 0; i < choices.Length; i++)
            {
                // A fresh local per iteration, not the loop variable — the closure below has to
                // capture this choice, not whatever i has become by the time the button is clicked.
                Choice choice = choices[i];
                Entry(column, $"Choice{i}", choice.Label, () => Pick(choice.Go));

                if (i < choices.Length - 1) UIBuilder.Spacer(column, 30f);
            }

            // Larger than the gap between choices, so Back does not read as a third answer.
            UIBuilder.Spacer(column, 44f);

            Entry(column, "BackButton", "Back", Close);
        }
    }
}
