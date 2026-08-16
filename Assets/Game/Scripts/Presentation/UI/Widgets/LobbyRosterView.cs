using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The in-lobby page: who is here, the code to let others in, and the two decisions left.
    ///
    /// Split out of <see cref="LobbyUI"/> because it is the only page in the multiplayer flow that
    /// is <b>live</b> — <see cref="LobbySession"/> polls twice a second and this redraws from
    /// whatever came back. The join and password pages are static forms that are built once and
    /// then only read. Keeping the redrawing page apart from the ones that never redraw is what
    /// stops "rebuild the page" and "update the page" being the same code path.
    ///
    /// It performs no service calls of its own. Everything it can do arrives as a callback, which
    /// is what lets it be built and rendered without a lobby, a network, or Unity Gaming Services.
    /// </summary>
    public class LobbyRosterView
    {
        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;
        private const float ActionHeight = 78f;
        private const float RowHeight = 62f;

        /// <summary>Right-hand column of a roster row, holding "host".</summary>
        private const float RoleWidth = 260f;

        // Two columns, for the same reason the join page has them: everything here has to sit below
        // MenuEntry.Horizon to be legible over ground, and a code, a four-player roster, a privacy
        // toggle, a password rule and a footer are about 630px of content stacked — more than the
        // 540px the bottom half of the screen actually is. Side by side they fit, with the roster
        // getting the room it needs to show a full lobby without scrolling.
        private const float LeftWidth = 620f;
        private const float RightX = 820f;
        private const float RightWidth = 1000f;

        /// <summary>What the host can do from this page. Supplied by the screen that owns it.</summary>
        public readonly struct Actions
        {
            public readonly System.Action Start;
            public readonly System.Action Leave;
            public readonly System.Action CopyCode;

            /// <summary>Called with the privacy the host just asked for.</summary>
            public readonly System.Action<bool> SetPrivacy;

            public Actions(System.Action start, System.Action leave, System.Action copyCode,
                System.Action<bool> setPrivacy)
            {
                Start = start;
                Leave = leave;
                CopyCode = copyCode;
                SetPrivacy = setPrivacy;
            }
        }

        private readonly GameObject entryPrefab;
        private readonly Actions actions;

        private TextMeshProUGUI title;
        private TextMeshProUGUI code;
        private TextMeshProUGUI status;
        private RectTransform roster;
        private GameObject copyAction;

        private GameObject privacyRow;
        private TextMeshProUGUI privacyState;
        private GameObject startAction;

        private readonly List<GameObject> rosterRows = new();

        /// <summary>Mirrors the lobby's own flag so the toggle knows which way to flip.</summary>
        private bool isPrivate;

        /// <summary>Set while the status line is holding a failure the poll must not overwrite.</summary>
        private bool statusIsSticky;

        public LobbyRosterView(RectTransform page, GameObject entryPrefab, Actions actions)
        {
            this.entryPrefab = entryPrefab;
            this.actions = actions;

            Build(page);
        }

        // ────────────────────────────────────────────────────────────────────── build

        private void Build(RectTransform page)
        {
            RectTransform titleRow = Pinned(page, "Title", MenuEntry.TitleTop, ColumnWidth,
                                            MenuEntry.TitleHeight);
            title = UIBuilder.Label(titleRow, string.Empty, MenuEntry.TitleSize, MenuEntry.Title,
                                    TextAlignmentOptions.Left, FontStyles.Bold);

            BuildCodeRow(page);

            RectTransform statusRow = Pinned(page, "Status", MenuEntry.ContentTop, RightWidth, 44f,
                                             fromLeft: RightX);
            status = UIBuilder.Label(statusRow, string.Empty, MenuEntry.CaptionSize, MenuEntry.Caption);

            BuildRoster(page);
            BuildControls(page);
        }

        private void BuildCodeRow(RectTransform page)
        {
            RectTransform row = Pinned(page, "Code", MenuEntry.ContentTop, LeftWidth, RowHeight);

            RectTransform caption = UIBuilder.Rect("Caption", row);
            caption.anchorMin = new Vector2(0f, 0f);
            caption.anchorMax = new Vector2(0f, 1f);
            caption.pivot = new Vector2(0f, 0.5f);
            caption.offsetMin = Vector2.zero;
            caption.offsetMax = new Vector2(120f, 0f);
            UIBuilder.Label(caption, "Code", MenuEntry.CaptionSize, MenuEntry.Caption);

            RectTransform value = UIBuilder.Rect("Value", row);
            value.anchorMin = new Vector2(0f, 0f);
            value.anchorMax = new Vector2(0f, 1f);
            value.pivot = new Vector2(0f, 0.5f);
            value.offsetMin = new Vector2(130f, 0f);
            value.offsetMax = new Vector2(430f, 0f);
            code = UIBuilder.Label(value, "—", MenuEntry.RowSize, MenuEntry.Idle,
                                   TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform copyRow = UIBuilder.Rect("CopyRow", row);
            copyRow.anchorMin = new Vector2(0f, 0f);
            copyRow.anchorMax = new Vector2(0f, 1f);
            copyRow.pivot = new Vector2(0f, 0.5f);
            copyRow.offsetMin = new Vector2(450f, 0f);
            copyRow.offsetMax = new Vector2(LeftWidth, 0f);

            // Wrapped rather than passed straight through: MenuEntry takes a UnityAction and these
            // are System.Actions, which are a different delegate type and do not convert.
            copyAction = MenuEntry.Create(entryPrefab, copyRow, "CopyButton", "Copy",
                                          MenuEntry.CaptionSize, RowHeight,
                                          () => actions.CopyCode?.Invoke(), out _).gameObject;
        }

        private void BuildRoster(RectTransform page)
        {
            RectTransform frame = UIBuilder.Rect("Roster", page);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            // The left column, from under the code row down to just above the footer. Sized to hold
            // a full lobby without scrolling: LobbySession.MaxPlayers is 4, so four rows at
            // RowHeight is the most this ever has to show.
            frame.offsetMin = new Vector2(ColumnX, MenuEntry.FooterBottom + ActionHeight + 40f);
            frame.offsetMax = new Vector2(ColumnX + LeftWidth, MenuEntry.ContentTop - 80f);

            RectTransform viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", frame));
            viewport.gameObject.AddComponent<RectMask2D>();

            roster = UIBuilder.Rect("Content", viewport);
            roster.anchorMin = new Vector2(0f, 1f);
            roster.anchorMax = new Vector2(1f, 1f);
            roster.pivot = new Vector2(0.5f, 1f);
            roster.offsetMin = Vector2.zero;
            roster.offsetMax = Vector2.zero;

            UIBuilder.Column(roster, 4f);
            var fitter = roster.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = roster;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
        }

        /// <summary>
        /// The host's privacy controls on the right, and the two actions along the bottom.
        ///
        /// They are separate because they answer to different edges. The footer belongs at the
        /// bottom of the page whatever else is on it; privacy sits under the status line in the
        /// right-hand column, where the password rule can appear and disappear beneath it without
        /// pushing anything around — nothing is stacked below it to push.
        /// </summary>
        private void BuildControls(RectTransform page)
        {
            BuildPrivacy(page);

            RectTransform footer = Pinned(page, "Footer", 0f, ColumnWidth, ActionHeight,
                                          fromBottom: MenuEntry.FooterBottom);

            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 64f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            startAction = MenuEntry.Create(entryPrefab, footer, "StartButton", "Start game",
                                           MenuEntry.ActionSize, ActionHeight,
                                           () => actions.Start?.Invoke(), out _).gameObject;
            MenuEntry.Width(startAction.GetComponent<Button>(), 420f);

            MenuEntry.Width(MenuEntry.Create(entryPrefab, footer, "LeaveButton", "Leave",
                                             MenuEntry.ActionSize, ActionHeight,
                                             () => actions.Leave?.Invoke(), out _), 260f);
        }

        private void BuildPrivacy(RectTransform page)
        {
            RectTransform row = Pinned(page, "Privacy", MenuEntry.ContentTop - 60f, RightWidth,
                                       ActionHeight, fromLeft: RightX);
            privacyRow = row.gameObject;

            Button toggle = MenuEntry.Create(entryPrefab, row, "PrivacyButton", "Private session",
                                             MenuEntry.ActionSize, ActionHeight, TogglePrivacy,
                                             out TextMeshProUGUI label);

            privacyState = MenuField.Trailing(toggle, label, "off", 200f, MenuEntry.Caption);
        }

        // ───────────────────────────────────────────────────────────────────── render

        /// <summary>
        /// Redraws from the session's current lobby. Called on every change, so it has to be cheap
        /// enough to run twice a second — and it is the only thing that writes the privacy label,
        /// so what the toggle reads is always the lobby's own flag rather than the last thing the
        /// host clicked.
        /// </summary>
        public void Render(Lobby lobby, bool isHost, string hostTitle)
        {
            if (lobby == null)
            {
                title.text = (hostTitle ?? "Session").ToUpperInvariant();
                code.text = "—";
                if (copyAction != null) copyAction.SetActive(false);
                if (privacyRow != null) privacyRow.SetActive(false);
                if (startAction != null) startAction.SetActive(false);
                ClearRoster();
                return;
            }

            title.text = (isHost && !string.IsNullOrEmpty(hostTitle) ? hostTitle : lobby.Name)
                .ToUpperInvariant();

            code.text = string.IsNullOrEmpty(lobby.LobbyCode) ? "—" : lobby.LobbyCode;
            if (copyAction != null) copyAction.SetActive(!string.IsNullOrEmpty(lobby.LobbyCode));

            // Only the host can start, and only the host can change privacy. A client shown either
            // gets a control that reports "Only the host can do that" — a button whose whole
            // behaviour is to refuse.
            if (startAction != null) startAction.SetActive(isHost);
            if (privacyRow != null) privacyRow.SetActive(isHost);

            isPrivate = lobby.IsPrivate;

            if (privacyState != null)
            {
                privacyState.text = lobby.IsPrivate ? "on" : "off";
                privacyState.color = lobby.IsPrivate ? MenuEntry.Idle : MenuEntry.Caption;
            }

            SetPolledStatus(isHost
                ? "Share the code. Start when everyone is in."
                : LobbySession.IsPlaying(lobby)
                    ? "The host is already playing. Joining the world…"
                    : "Waiting for the host to start.");

            RenderRoster(LobbySession.PlayerNames(lobby), lobby.HostId, lobby.Players);
        }

        /// <summary>A transient line — "Creating session…", "Copied ABC123". The next poll replaces it.</summary>
        public void SetStatus(string message)
        {
            status.text = message ?? string.Empty;
            statusIsSticky = false;
        }

        /// <summary>
        /// A failure the player has to actually read.
        ///
        /// Sticky, because this page redraws twice a second: written through
        /// <see cref="SetStatus"/> it would survive for as long as it took the next poll to land,
        /// which is not long enough to read "Could not change the session's privacy" — and the
        /// polled line that replaced it would say everything was fine.
        /// </summary>
        public void SetWarning(string message)
        {
            status.text = message ?? string.Empty;
            statusIsSticky = true;
        }

        private void SetPolledStatus(string message)
        {
            if (statusIsSticky) return;
            status.text = message ?? string.Empty;
        }

        /// <summary>
        /// Rebuilds the roster rows.
        ///
        /// Destroy does not take effect until the end of the frame, so the outgoing rows are
        /// unparented as well: without that, a poll landing twice in one frame stacks two rosters
        /// in the layout group and the list visibly doubles before settling.
        /// </summary>
        private void RenderRoster(string[] names, string hostId, List<Player> players)
        {
            ClearRoster();

            for (int i = 0; i < names.Length; i++)
            {
                RectTransform row = UIBuilder.Rect("PlayerRow", roster);
                UIBuilder.FixedHeight(row, RowHeight);

                UIBuilder.Label(row, names[i], MenuEntry.RowSize, MenuEntry.Idle,
                                TextAlignmentOptions.Left, FontStyles.Bold);

                bool isHostRow = players != null && i < players.Count && players[i] != null
                                 && players[i].Id == hostId;

                if (isHostRow)
                {
                    RectTransform roleRect = UIBuilder.Rect("Role", row);
                    roleRect.anchorMin = new Vector2(1f, 0f);
                    roleRect.anchorMax = new Vector2(1f, 1f);
                    roleRect.pivot = new Vector2(1f, 0.5f);
                    roleRect.offsetMin = new Vector2(-RoleWidth, 0f);
                    roleRect.offsetMax = Vector2.zero;

                    UIBuilder.Label(roleRect, "host", MenuEntry.CaptionSize, MenuEntry.Caption,
                                    TextAlignmentOptions.Right);
                }

                rosterRows.Add(row.gameObject);
            }
        }

        private void ClearRoster()
        {
            foreach (GameObject row in rosterRows)
            {
                if (row == null) continue;

                row.transform.SetParent(null, false);
                Object.Destroy(row);
            }

            rosterRows.Clear();
        }

        // ───────────────────────────────────────────────────────────────────── actions

        /// <summary>
        /// Reports the privacy the host asked for.
        ///
        /// The label is not written here. It is rendered from the lobby the service hands back, so
        /// what the toggle says is what is actually in force rather than what was last asked for —
        /// which matters precisely when the update fails.
        /// </summary>
        private void TogglePrivacy()
        {
            // Pressing the toggle again is how a host retries after a failure, so the failure that
            // is being retried stops being the thing pinned to the status line.
            SetStatus(string.Empty);

            actions.SetPrivacy?.Invoke(!isPrivate);
        }

        /// <summary>
        /// A row placed against the page's top-left, or its bottom-left when
        /// <paramref name="fromBottom"/> is given. <paramref name="fromLeft"/> overrides the shared
        /// column inset, which is how the right-hand column is placed beside the left one.
        /// </summary>
        private static RectTransform Pinned(RectTransform parent, string name, float fromTop,
            float width, float height, float fromBottom = float.NaN, float fromLeft = ColumnX)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            bool bottom = !float.IsNaN(fromBottom);

            rect.anchorMin = rect.anchorMax = new Vector2(0f, bottom ? 0f : 1f);
            rect.pivot = new Vector2(0f, bottom ? 0f : 1f);
            rect.anchoredPosition = new Vector2(fromLeft, bottom ? fromBottom : fromTop);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }
}
