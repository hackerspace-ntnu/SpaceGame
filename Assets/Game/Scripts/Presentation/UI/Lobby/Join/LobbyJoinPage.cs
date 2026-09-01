using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The "Join a game" page: the session list, the code field beside it, and the footer.
    ///
    /// <para>
    /// Widgets only. What happens when something is pressed is <see cref="LobbyJoinFlow"/>'s
    /// business; this page knows how to lock itself region by region while the flow waits on a
    /// service, and nothing else. Locked region by region rather than through one group over the
    /// whole page, for two reasons: the footer's Cancel has to stay live while everything around
    /// it is dead, and the row being joined has to stay lit while its neighbours recede — and a
    /// child CanvasGroup cannot undo a parent's alpha, so "dim everything, then brighten one" is
    /// not available.
    /// </para>
    /// </summary>
    public sealed class LobbyJoinPage
    {
        /// <summary>What the page's controls do. Supplied by the flow that owns the page.</summary>
        public readonly struct Actions
        {
            public readonly Action JoinByCode;
            public readonly Action<string, string> JoinRow;
            public readonly Action Refresh;
            public readonly Action Back;
            public readonly Action Cancel;

            public Actions(Action joinByCode, Action<string, string> joinRow, Action refresh,
                Action back, Action cancel)
            {
                JoinByCode = joinByCode;
                JoinRow = joinRow;
                Refresh = refresh;
                Back = back;
                Cancel = cancel;
            }
        }

        private const int CodeCharacterLimit = 12;

        private readonly TMP_InputField codeField;
        private readonly CanvasGroup codeGroup;
        private readonly RectTransform codeRuleSlot;

        private readonly CanvasGroup refreshGroup;
        private readonly CanvasGroup cancelGroup;
        private readonly GameObject backAction;
        private readonly GameObject cancelAction;

        private MenuBusy rule;

        public LobbyBrowser Browser { get; }

        public MenuStatusLine Status { get; }

        /// <summary>Whatever is in the code field right now.</summary>
        public string TypedCode => codeField != null ? codeField.text : null;

        public LobbyJoinPage(RectTransform root, GameObject entryPrefab, Actions actions)
        {
            Browser = new LobbyBrowser(root, entryPrefab, actions.JoinRow);

            float top = MenuEntry.ContentTop;

            // The code side gets a rect of its own so one CanvasGroup can lock and dim its heading,
            // its field and its button together. It fills the page rather than boxing the column,
            // so every row keeps measuring from the same top-left corner; nothing is drawn on it,
            // so it catches no clicks and the browser beside it is unaffected.
            RectTransform code = UIBuilder.Fill(UIBuilder.Rect("CodeColumn", root));
            codeGroup = code.gameObject.AddComponent<CanvasGroup>();

            UIBuilder.Label(
                UIBuilder.PinnedTop(code, "Caption", LobbyJoinLayout.CodeX, top,
                                    LobbyJoinLayout.CodeWidth, LobbyJoinLayout.HeadingHeight),
                "Have a code?", MenuEntry.CaptionSize, MenuEntry.Caption);

            RectTransform codeRow = UIBuilder.PinnedTop(code, "Field", LobbyJoinLayout.CodeX,
                                                        top - LobbyJoinLayout.FieldDrop,
                                                        LobbyJoinLayout.CodeWidth, MenuField.Height);
            codeField = MenuField.Rule(codeRow, "CodeField", "Lobby code…", LobbyJoinLayout.FieldWidth,
                                       _ => actions.JoinByCode(), characterLimit: CodeCharacterLimit);

            // On the page, not in the column it sits under. The column is what gets dimmed while a
            // code join runs, and a busy rule faded to a third of its alpha along with the controls
            // it is explaining is the one thing on the page that must not recede.
            codeRuleSlot = UIBuilder.PinnedTop(root, "CodeRule", LobbyJoinLayout.CodeX,
                                               top - LobbyJoinLayout.CodeRuleDrop,
                                               LobbyJoinLayout.FieldWidth, MenuBusy.RuleThickness);

            RectTransform joinRow = UIBuilder.PinnedTop(code, "Join", LobbyJoinLayout.CodeX,
                                                        top - LobbyJoinLayout.JoinDrop,
                                                        LobbyJoinLayout.JoinWidth, MenuEntry.ActionHeight);
            MenuEntry.Create(entryPrefab, joinRow, "JoinButton", "Join", MenuEntry.ActionSize,
                             MenuEntry.ActionHeight, () => actions.JoinByCode(), out _);

            Status = new MenuStatusLine(UIBuilder.Label(
                UIBuilder.PinnedBottom(root, "Message", MenuEntry.ColumnX, MenuEntry.MessageBottom,
                                       MenuEntry.ColumnWidth, LobbyJoinLayout.HeadingHeight),
                string.Empty, MenuEntry.CaptionSize, MenuEntry.Caption));

            RectTransform footer = UIBuilder.PinnedBottom(root, "Footer", MenuEntry.ColumnX,
                                                          MenuEntry.FooterBottom, MenuEntry.ColumnWidth,
                                                          MenuEntry.ActionHeight);
            UIBuilder.Row(footer, LobbyJoinLayout.FooterSpacing);

            refreshGroup = FooterAction(entryPrefab, footer, "RefreshButton", "Refresh",
                                        LobbyJoinLayout.RefreshWidth, () => actions.Refresh());

            backAction = FooterAction(entryPrefab, footer, "BackButton", "Back",
                                      LobbyJoinLayout.BackWidth, () => actions.Back()).gameObject;

            // Takes Back's place for the duration of a join and is the only live control on the page
            // while one is running. Built here rather than swapped in later so the footer's layout
            // has always measured it.
            cancelGroup = FooterAction(entryPrefab, footer, "CancelButton", "Cancel",
                                       LobbyJoinLayout.CancelWidth, () => actions.Cancel());
            cancelAction = cancelGroup.gameObject;
            cancelAction.SetActive(false);
        }

        /// <summary>
        /// Locks the page for a wait, or gives it back for <see cref="LobbyBusyScope.None"/>.
        /// <paramref name="activeRowId"/> is the row being joined, which stays lit and carries the
        /// "Joining…" caption.
        /// </summary>
        public void SetBusy(LobbyBusyScope scope, string activeRowId)
        {
            LobbyBusyState state = LobbyBusyState.For(scope);

            MenuLock.Set(codeGroup, state.LockCodeColumn);
            MenuLock.Set(refreshGroup, state.LockRefresh);
            Browser.Lock(state.LockBrowser, activeRowId);
            Browser.Caption(activeRowId);

            // Back and Cancel share a slot: only one of them is ever a sensible thing to press. The
            // cancel group is unlocked again here because LockCancel leaves it dead.
            MenuLock.Set(cancelGroup, locked: false);
            if (backAction != null) backAction.SetActive(!state.OfferCancel);
            if (cancelAction != null) cancelAction.SetActive(state.OfferCancel);

            SetRule(scope);
        }

        /// <summary>Cancel can be pressed once and no more. Everything else on the page is already locked.</summary>
        public void LockCancel() => MenuLock.Set(cancelGroup, locked: true);

        /// <summary>
        /// Stops the animations ahead of the page being destroyed. Both are inside the page and
        /// would go with it, but a reference to a destroyed one is what a later Stop() trips over.
        /// </summary>
        public void Dispose()
        {
            Status.Stop();
            Browser.Dispose();

            if (rule != null) { rule.Stop(); rule = null; }
        }

        /// <summary>
        /// Puts the sweeping rule under whichever column the wait belongs to — the code field for a
        /// code join, the session list for everything else.
        /// </summary>
        private void SetRule(LobbyBusyScope scope)
        {
            if (rule != null) { rule.Stop(); rule = null; }

            RectTransform slot = scope switch
            {
                LobbyBusyScope.JoiningByCode => codeRuleSlot,
                LobbyBusyScope.SigningIn or LobbyBusyScope.Querying or LobbyBusyScope.JoiningRow => Browser.RuleSlot,
                _ => null
            };

            if (slot != null) rule = MenuBusy.Rule(slot);
        }

        /// <summary>
        /// A footer button that can be locked on its own. Each carries its own group because the
        /// three of them are not switched off together — Refresh goes quiet during a query that
        /// leaves Back alone, and Cancel stays live through a join that kills everything else.
        /// </summary>
        private static CanvasGroup FooterAction(GameObject entryPrefab, RectTransform footer, string name,
            string label, float width, UnityAction onClick)
        {
            Button button = MenuEntry.Create(entryPrefab, footer, name, label, MenuEntry.ActionSize,
                                             MenuEntry.ActionHeight, onClick, out _);
            MenuEntry.Width(button, width);
            return button.gameObject.AddComponent<CanvasGroup>();
        }
    }
}
