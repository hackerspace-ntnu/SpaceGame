using SpaceGame.Core.Lobbies;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The join page's geometry, in one place because three classes draw against it and the
    /// layout tests reason about it.
    ///
    /// <para>
    /// Two columns, because everything clickable has to sit below <see cref="MenuEntry.Horizon"/>
    /// to read against ground, and a title, a code field, its action, a session list and a footer
    /// do not fit in half a screen stacked on top of each other. Which column is which is the
    /// whole point: the sessions are the wide left column, read first, because the list of games
    /// you can actually join is the subject of a page called "Join a game"; the code entry is the
    /// compact aside it always was.
    /// </para>
    /// </summary>
    public static class LobbyJoinLayout
    {
        // ─────────────────────────────────────────────────────────── the two columns

        public const float ListX = MenuEntry.ColumnX;
        public const float ListWidth = 1120f;

        public const float CodeX = 1264f;
        public const float CodeWidth = 560f;
        public const float FieldWidth = 520f;

        /// <summary>The heading over the list and the caption over the code field share this height.</summary>
        public const float HeadingHeight = 44f;

        // The list's own band, measured from the rows already pinned above and below it. Both insets
        // were 48 when the list was a side column; there is no reason for that much air around the
        // thing the page is for, and 12 is worth most of an extra row.
        public const float ListTopDrop = 46f;
        public const float ListBottomGap = 12f;

        public const float RowHeight = 72f;
        public const float RowSpacing = 6f;

        // The code column's rows, measured down from ContentTop: the caption at 0, the field from
        // FieldDrop, Join from JoinDrop.
        public const float FieldDrop = 46f;
        public const float JoinDrop = 152f;
        public const float JoinWidth = 420f;

        // Where the two busy rules sit, measured down from ContentTop. Both land in gaps that
        // already exist between rows rather than pushing anything around, so the page does not
        // reflow when a wait starts.
        public const float CodeRuleDrop = 144f;
        public const float ListRuleDrop = 44f;

        // ───────────────────────────────────────────────── a row's right-hand furniture
        //
        // Laid out from the right edge inwards. Each slot is sized to its longest content, because
        // UIBuilder labels truncate rather than overflow. The state slot is wide enough for the
        // animated "Joining…" caption that replaces the occupancy during a join, not merely for
        // "4/4" — a slot sized to the resting content would silently clip the one message that
        // matters.

        public const float StateWidth = 200f;
        public const float PipsGap = 16f;
        public const float PlayingWidth = 150f;

        /// <summary>One pip per player slot. Four of them, 22 wide with 8 between: 112 across.</summary>
        public const float PipWidth = 22f;
        public const float PipHeight = 10f;
        public const float PipGap = 8f;

        public const float PipsWidth = LobbySession.MaxPlayers * PipWidth + (LobbySession.MaxPlayers - 1) * PipGap;

        /// <summary>Air between the last piece of furniture and where the name is allowed to run to.</summary>
        public const float NameInset = 24f;

        // ────────────────────────────────────────────────────────────────── the footer

        public const float FooterSpacing = 64f;
        public const float RefreshWidth = 340f;
        public const float BackWidth = 260f;
        public const float CancelWidth = 300f;
    }
}
