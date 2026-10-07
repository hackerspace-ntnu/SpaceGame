using System;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The session list: the wide left column of the join page, with a live count in its heading
    /// and occupancy drawn rather than spelled.
    ///
    /// <para>
    /// It is fed a fresh list every second, which is what forces the rows to be reconciled rather
    /// than rebuilt — see <see cref="Apply"/>. Rows are matched by lobby id so the one being joined
    /// can be found again after the click and made to say so.
    /// </para>
    /// </summary>
    public sealed class LobbyBrowser
    {
        /// <summary>The heading over the list, set larger than a caption because it leads the page.</summary>
        private const int HeadingSize = 36;

        private const string HeadingText = "OPEN SESSIONS";
        private const string EmptyText = "Nothing open right now.\nHost a game, or join with a code.";
        private const float ScrollSensitivity = 32f;

        private readonly GameObject entryPrefab;
        private readonly Action<string, string> onJoinRow;

        private readonly TextMeshProUGUI heading;
        private readonly CanvasGroup frameGroup;
        private readonly RectTransform content;
        private readonly GameObject emptyState;

        private readonly Dictionary<string, LobbyBrowserRow> rows = new();

        /// <summary>Which row is currently saying "Joining…" instead of its occupancy, or null.</summary>
        private string captionedRow;

        private MenuBusy rowDots;

        /// <param name="onJoinRow">Called with a lobby's id and name when its row is clicked.</param>
        public LobbyBrowser(RectTransform root, GameObject entryPrefab, Action<string, string> onJoinRow)
        {
            this.entryPrefab = entryPrefab;
            this.onJoinRow = onJoinRow;

            float top = MenuEntry.ContentTop;

            heading = UIBuilder.Label(
                UIBuilder.PinnedTop(root, "Heading", LobbyJoinLayout.ListX, top,
                                    LobbyJoinLayout.ListWidth, LobbyJoinLayout.HeadingHeight),
                HeadingText, HeadingSize, MenuEntry.Caption, TextAlignmentOptions.Left, FontStyles.Bold);

            RuleSlot = UIBuilder.PinnedTop(root, "ListRule", LobbyJoinLayout.ListX,
                                           top - LobbyJoinLayout.ListRuleDrop,
                                           LobbyJoinLayout.ListWidth, MenuBusy.RuleThickness);

            RectTransform frame = BuildFrame(root, out frameGroup, out content);
            emptyState = BuildEmptyState(frame);
        }

        /// <summary>The empty row under the heading a sweeping busy rule is built into.</summary>
        public RectTransform RuleSlot { get; }

        /// <summary>
        /// Brings the list into line with a freshly-queried set of sessions.
        ///
        /// Rows are matched by lobby id and updated where they already exist, so a session that was
        /// on screen a second ago keeps the same object — and with it its hover state, its place in
        /// the scroll, and the click the player is halfway through making. Only arrivals cost an
        /// Instantiate and only departures a Destroy.
        /// </summary>
        /// <param name="hasLanded">
        /// Whether any query has landed yet, so the empty state is held back until the page has
        /// actually looked rather than announcing there is nothing there.
        /// </param>
        public void Apply(List<Lobby> lobbies, bool hasLanded)
        {
            if (content == null) return;

            RemoveDeparted(lobbies);

            for (int i = 0; i < lobbies.Count; i++)
            {
                Lobby lobby = lobbies[i];

                if (!rows.TryGetValue(lobby.Id, out LobbyBrowserRow row))
                {
                    string id = lobby.Id;
                    string name = lobby.Name;
                    row = LobbyBrowserRow.Build(entryPrefab, content, lobby, () => onJoinRow(id, name));
                    rows[id] = row;
                }

                row.Update(lobby, captioned: captionedRow == lobby.Id);

                // Newest first is the query's order, and a list that reorders itself under the
                // pointer is worse than one that is slightly stale — but sessions filling up and
                // emptying is exactly what the player is watching for, so the order is honoured.
                if (row.Root != null) row.Root.SetSiblingIndex(i);
            }

            if (heading != null)
                heading.text = lobbies.Count > 0 ? $"{HeadingText} · {lobbies.Count}" : HeadingText;

            if (emptyState != null) emptyState.SetActive(hasLanded && lobbies.Count == 0);
        }

        /// <summary>
        /// Locks every row, dimming all but <paramref name="activeRowId"/>.
        ///
        /// The frame is locked but never dimmed — its rows carry their own alpha, and dimming
        /// here would multiply with theirs and take the active one down with the rest.
        /// </summary>
        public void Lock(bool locked, string activeRowId)
        {
            MenuLock.Set(frameGroup, locked, dim: false);

            foreach (KeyValuePair<string, LobbyBrowserRow> entry in rows)
                entry.Value.Lock(locked, dim: locked && entry.Key != activeRowId);
        }

        /// <summary>
        /// Moves the "Joining…" caption onto a row, and puts the previous one's occupancy back.
        ///
        /// This is the cue that answers the original complaint: the status line along the bottom
        /// says the same thing, but a player who has pressed a session name is looking at the
        /// session name. Written into the trailing slot the occupancy already uses, so the row does
        /// not change shape.
        /// </summary>
        public void Caption(string rowId)
        {
            if (captionedRow == rowId) return;

            StopRowDots();

            if (captionedRow != null && rows.TryGetValue(captionedRow, out LobbyBrowserRow previous))
                previous.RestoreOccupancy();

            captionedRow = rowId;

            if (rowId != null && rows.TryGetValue(rowId, out LobbyBrowserRow row))
                rowDots = MenuBusy.Dots(row.StateLabel, "Joining");
        }

        /// <summary>
        /// Drops every reference to the rows ahead of the page going away.
        ///
        /// The caption is stopped rather than restored: the label it was writing to is either gone
        /// already or about to be, and putting an occupancy back on a destroyed row is how a clean
        /// teardown becomes a MissingReferenceException.
        /// </summary>
        public void Dispose()
        {
            StopRowDots();
            captionedRow = null;
            rows.Clear();
        }

        /// <summary>
        /// Gone first, so the survivors' sibling indices are set against the final list. Collected
        /// before anything is destroyed: mutating the dictionary inside its own enumeration throws.
        /// </summary>
        private void RemoveDeparted(List<Lobby> lobbies)
        {
            var seen = new HashSet<string>();
            foreach (Lobby lobby in lobbies) seen.Add(lobby.Id);

            var departed = new List<string>();
            foreach (string id in rows.Keys)
                if (!seen.Contains(id))
                    departed.Add(id);

            foreach (string id in departed) RemoveRow(id);
        }

        private void RemoveRow(string id)
        {
            if (!rows.TryGetValue(id, out LobbyBrowserRow row)) return;

            rows.Remove(id);

            // A row cannot vanish from under a join in flight — the auto refresh stands down while
            // one is running — but the caption reference would outlive the label if it did.
            if (captionedRow == id)
            {
                StopRowDots();
                captionedRow = null;
            }

            row.Remove();
        }

        private void StopRowDots()
        {
            if (rowDots == null) return;

            rowDots.Stop();
            rowDots = null;
        }

        /// <summary>
        /// The scrolling frame, anchored to both edges vertically so the list uses whatever the
        /// band actually is rather than a height computed here and left to drift.
        /// </summary>
        private static RectTransform BuildFrame(RectTransform root, out CanvasGroup group, out RectTransform content)
        {
            RectTransform frame = UIBuilder.Rect("Browser", root);
            group = frame.gameObject.AddComponent<CanvasGroup>();
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            frame.offsetMin = new Vector2(LobbyJoinLayout.ListX, MenuEntry.MessageBottom + LobbyJoinLayout.ListBottomGap);
            frame.offsetMax = new Vector2(LobbyJoinLayout.ListX + LobbyJoinLayout.ListWidth,
                                          MenuEntry.ContentTop - LobbyJoinLayout.ListTopDrop);

            RectTransform viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", frame));
            viewport.gameObject.AddComponent<RectMask2D>();

            content = UIBuilder.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            UIBuilder.Column(content, LobbyJoinLayout.RowSpacing);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ScrollSensitivity;

            return frame;
        }

        /// <summary>
        /// In the list's own area, where the player is already looking, rather than as a caption
        /// in the far corner of the screen. A sibling of the viewport so the scroll content can
        /// stay empty — a placeholder inside the layout group would be measured as a row.
        /// </summary>
        private static GameObject BuildEmptyState(RectTransform frame)
        {
            RectTransform empty = UIBuilder.Fill(UIBuilder.Rect("Empty", frame));
            UIBuilder.Label(empty, EmptyText, MenuEntry.CaptionSize, MenuEntry.Caption,
                            TextAlignmentOptions.TopLeft).textWrappingMode = TextWrappingModes.Normal;

            empty.gameObject.SetActive(false);
            return empty.gameObject;
        }
    }
}
