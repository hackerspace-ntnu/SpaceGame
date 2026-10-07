using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SpaceGame.Core.Lobbies;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// One session in the browser: the name, then its state, occupancy pips and whether it has
    /// started, laid out from the right edge inwards.
    ///
    /// <para>
    /// Kept and rewritten in place rather than rebuilt, because the list refreshes every second.
    /// Destroying and recreating every row on that cadence would restart the hover animation
    /// under the player's pointer, drop the scroll position, and hand a click to a button being
    /// destroyed in the same frame.
    /// </para>
    ///
    /// <para>
    /// Everything writable here is on its own object. The row's own label is not: the button
    /// prefab's animator rewrites its colour on every state change, which is why the name is set
    /// once at build time and every changing value lives beside it.
    /// </para>
    /// </summary>
    internal sealed class LobbyBrowserRow
    {
        /// <summary>
        /// A session's name, one step up from <see cref="MenuEntry.RowSize"/>. Local rather than a
        /// change to the shared constant: the world list is read at leisure, where this is the
        /// one thing on the page the player came to find.
        /// </summary>
        private const int NameSize = 58;

        /// <summary>What an unfilled player slot is drawn in — the same navy, barely there.</summary>
        private static readonly Color PipEmpty =
            new(MenuEntry.Caption.r, MenuEntry.Caption.g, MenuEntry.Caption.b, 0.22f);

        private readonly RectTransform root;
        private readonly CanvasGroup group;
        private readonly TextMeshProUGUI state;
        private readonly TextMeshProUGUI playing;
        private readonly Image[] pips;

        /// <summary>What <see cref="StateLabel"/> says when nothing is being joined.</summary>
        private string occupancy;

        private LobbyBrowserRow(RectTransform root, CanvasGroup group, TextMeshProUGUI state,
            TextMeshProUGUI playing, Image[] pips)
        {
            this.root = root;
            this.group = group;
            this.state = state;
            this.playing = playing;
            this.pips = pips;
        }

        public RectTransform Root => root;

        /// <summary>Right-hand slot: the occupancy at rest, the "Joining…" caption during a join.</summary>
        public TextMeshProUGUI StateLabel => state;

        public static LobbyBrowserRow Build(GameObject entryPrefab, RectTransform parent, Lobby lobby,
            UnityAction onClick)
        {
            Button button = MenuEntry.Create(entryPrefab, parent, "SessionRow", lobby.Name, NameSize,
                                             LobbyJoinLayout.RowHeight, onClick, out TextMeshProUGUI label);

            var root = (RectTransform)button.transform;

            // Its own group, so this one row can stay lit while the rest of the list recedes
            // behind the join it started.
            var group = button.gameObject.AddComponent<CanvasGroup>();

            TextMeshProUGUI state = TrailingLabel(root, "State", LobbyJoinLayout.StateWidth, 0f,
                                                  MenuEntry.Caption);

            float pipsRight = LobbyJoinLayout.StateWidth + LobbyJoinLayout.PipsGap;
            Image[] pips = BuildPips(root, pipsRight);

            float playingRight = pipsRight + LobbyJoinLayout.PipsWidth + LobbyJoinLayout.PipsGap;
            TextMeshProUGUI playing = TrailingLabel(root, "Playing", LobbyJoinLayout.PlayingWidth,
                                                    playingRight, MenuEntry.Idle);

            // The prefab's label fills the whole row, so it has to be told to stop before the
            // furniture starts or the name runs underneath all of it.
            MenuEntry.InsetLabel(label, 0f,
                                 playingRight + LobbyJoinLayout.PlayingWidth + LobbyJoinLayout.NameInset);

            return new LobbyBrowserRow(root, group, state, playing, pips);
        }

        /// <summary>
        /// Writes the parts that change: how full it is, and whether it has started.
        ///
        /// The name is not rewritten. A lobby cannot be renamed in this game, and the row's own
        /// label is the one thing on it the button prefab's animator also touches. The state slot
        /// is left alone while it is carrying the "Joining…" caption, which is animated and owns
        /// that label until the attempt settles.
        /// </summary>
        public void Update(Lobby lobby, bool captioned)
        {
            int taken = lobby.MaxPlayers - lobby.AvailableSlots;

            occupancy = LobbyRoster.DescribeOccupancy(lobby.MaxPlayers, lobby.AvailableSlots);
            if (!captioned) RestoreOccupancy();

            // Sessions already in progress are labelled rather than hidden: joining one works —
            // Netcode synchronises a late arrival into the running world — and a session that
            // vanishes from the list the moment friends start playing is the opposite of useful.
            if (playing != null)
                playing.text = LobbyRoster.IsPlaying(lobby) ? "PLAYING" : string.Empty;

            for (int i = 0; i < pips.Length; i++)
                if (pips[i] != null)
                    pips[i].color = i < taken ? MenuEntry.Idle : PipEmpty;
        }

        /// <summary>Puts the occupancy back after a "Joining…" caption.</summary>
        public void RestoreOccupancy()
        {
            if (state != null) state.text = occupancy;
        }

        public void Lock(bool locked, bool dim) => MenuLock.Set(group, locked, dim);

        /// <summary>
        /// Takes the row out of the list now, not at the end of the frame.
        ///
        /// Unparented as well as destroyed: Destroy does not take effect until the end of the
        /// frame, so a departing row would still be measured by the layout group until then and
        /// the list would visibly settle a frame later.
        /// </summary>
        public void Remove()
        {
            if (root == null) return;

            root.SetParent(null, false);
            Object.Destroy(root.gameObject);
        }

        /// <summary>
        /// One small bar per player slot, filled from the left.
        ///
        /// Bars rather than glyphs. The obvious characters for this — filled and hollow circles —
        /// are not in LiberationSans, the same gap that shipped the suit cycler's chevrons as
        /// nothing; and a rule is what the rest of this menu is made of anyway.
        /// </summary>
        private static Image[] BuildPips(RectTransform row, float fromRight)
        {
            RectTransform strip = UIBuilder.Rect("Pips", row);
            strip.anchorMin = new Vector2(1f, 0.5f);
            strip.anchorMax = new Vector2(1f, 0.5f);
            strip.pivot = new Vector2(1f, 0.5f);
            strip.anchoredPosition = new Vector2(-fromRight, 0f);
            strip.sizeDelta = new Vector2(LobbyJoinLayout.PipsWidth, LobbyJoinLayout.PipHeight);

            var pips = new Image[LobbySession.MaxPlayers];

            for (int i = 0; i < pips.Length; i++)
            {
                RectTransform pip = UIBuilder.Rect($"Pip{i}", strip);
                pip.anchorMin = new Vector2(0f, 0.5f);
                pip.anchorMax = new Vector2(0f, 0.5f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.anchoredPosition = new Vector2(i * (LobbyJoinLayout.PipWidth + LobbyJoinLayout.PipGap), 0f);
                pip.sizeDelta = new Vector2(LobbyJoinLayout.PipWidth, LobbyJoinLayout.PipHeight);

                pips[i] = UIBuilder.Solid(pip, PipEmpty);
            }

            return pips;
        }

        /// <summary>
        /// A right-aligned label inside a row, <paramref name="fromRight"/> in from its right edge.
        /// Its own object every time, never a tint or a write on the row's own label — see the
        /// class doc.
        /// </summary>
        private static TextMeshProUGUI TrailingLabel(RectTransform row, string name, float width,
            float fromRight, Color color)
        {
            RectTransform rect = UIBuilder.Rect(name, row);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-(fromRight + width), 0f);
            rect.offsetMax = new Vector2(-fromRight, 0f);

            return UIBuilder.Label(rect, string.Empty, MenuEntry.CaptionSize, color,
                                   TextAlignmentOptions.Right, FontStyles.Bold);
        }
    }
}
