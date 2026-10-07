using NUnit.Framework;
using SpaceGame.Presentation;
using SpaceGame.Presentation.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Guards the join page's geometry.
    ///
    /// <para>
    /// These exist because the page is built from hand-computed offsets and there is no way to look
    /// at it from here: the MCP bridge refuses to enter play mode, so the layout cannot be rendered
    /// and screenshotted from outside the editor. Every constant below was worked out on paper, and
    /// paper is exactly where a column overlapping its neighbour or a rule landing on top of a
    /// button goes unnoticed.
    /// </para>
    ///
    /// <para>
    /// They assert relationships, not values. That a session name has room to be read matters; that
    /// the list happens to be 1120 wide does not, and a test asserting the latter would only ever
    /// fail as a chore the next time someone moved anything.
    /// </para>
    /// </summary>
    public class LobbyLayoutTests
    {
        /// <summary>The canvas both menu screens scale against — CanvasScaler's reference width.</summary>
        private const float ReferenceWidth = 1920f;

        // ────────────────────────────────────────────────────── the two columns

        [Test]
        public void TheColumnsDoNotOverlap()
        {
            float listRight = LobbyJoinLayout.ListX + LobbyJoinLayout.ListWidth;

            Assert.Less(listRight, LobbyJoinLayout.CodeX,
                        "the session list runs into the code column");
        }

        [Test]
        public void BothColumnsFitOnTheCanvas()
        {
            Assert.LessOrEqual(LobbyJoinLayout.CodeX + LobbyJoinLayout.CodeWidth, ReferenceWidth,
                               "the code column runs off the right-hand edge");
        }

        /// <summary>
        /// The field's rule is drawn at exactly its given width, so a field wider than its column
        /// would underline straight through whatever sits beside it.
        /// </summary>
        [Test]
        public void TheCodeFieldFitsItsColumn()
        {
            Assert.LessOrEqual(LobbyJoinLayout.FieldWidth, LobbyJoinLayout.CodeWidth);
        }

        /// <summary>
        /// The list is the page's subject, so it gets the larger share. This is the invariant the
        /// whole overhaul was for — if a later change quietly shrinks the list below the aside
        /// beside it, the page is back to what it was.
        /// </summary>
        [Test]
        public void TheSessionListIsTheWiderColumn()
        {
            Assert.Greater(LobbyJoinLayout.ListWidth, LobbyJoinLayout.CodeWidth);
        }

        // ─────────────────────────────────────────────────── the vertical bands

        /// <summary>
        /// The list's band, in pixels, between the heading above it and the status line below.
        /// Both ends are measured the way the builder measures them.
        /// </summary>
        private static float ListBandHeight()
        {
            // ContentTop is negative, measured down from the top of the canvas. Asked for the
            // reference height explicitly rather than read off the live one: MenuEntry.ContentTop
            // now resolves against whatever canvas the game is running on, and a test that took it
            // would be asserting against the editor's game-view size.
            float reference = UIScale.ReferenceResolution.y;
            float topFromBottom = reference - (-MenuEntry.ContentTopFor(reference) - LobbyJoinLayout.ListTopDrop);
            float bottomFromBottom = MenuEntry.MessageBottom + LobbyJoinLayout.ListBottomGap;

            return topFromBottom - bottomFromBottom;
        }

        [Test]
        public void TheListHasRoomForSeveralSessions()
        {
            float rows = ListBandHeight() / (LobbyJoinLayout.RowHeight + 6f);

            // Three is the floor, not the target: below that the list stops reading as a list and
            // the scroll becomes the only way to see anything.
            Assert.GreaterOrEqual(rows, 3f, $"only {rows:0.0} session rows fit in the list");
        }

        /// <summary>
        /// The list must not reach down into the status line. It has no background, so an overlap
        /// does not clip — it interleaves, and a session row is drawn straight through whatever the
        /// page is trying to tell you.
        /// </summary>
        [Test]
        public void TheListClearsTheStatusLine()
        {
            Assert.Greater(LobbyJoinLayout.ListBottomGap, 0f);
            Assert.Greater(ListBandHeight(), 0f, "the list band has collapsed");
        }

        // ──────────────────────────────────────────────────── the busy rules

        /// <summary>
        /// The list's busy rule sits in the gap between the heading and the top of the list. The
        /// heading row is 44 tall, and the list starts at ListTopDrop.
        /// </summary>
        [Test]
        public void TheListRuleLandsBetweenTheHeadingAndTheList()
        {
            const float headingHeight = 44f;

            Assert.GreaterOrEqual(LobbyJoinLayout.ListRuleDrop, headingHeight,
                                  "the busy rule is drawn over the heading");

            Assert.LessOrEqual(LobbyJoinLayout.ListRuleDrop + MenuBusy.RuleThickness,
                               LobbyJoinLayout.ListTopDrop + 1f,
                               "the busy rule is drawn over the first session row");
        }

        /// <summary>
        /// The code column's busy rule sits between the field above it and the Join button below.
        /// The field runs from 46 to 46 + MenuField.Height; Join starts at 152.
        /// </summary>
        [Test]
        public void TheCodeRuleLandsBetweenTheFieldAndTheButton()
        {
            const float fieldTop = 46f;
            const float joinTop = 152f;

            float fieldBottom = fieldTop + MenuField.Height;

            Assert.GreaterOrEqual(LobbyJoinLayout.CodeRuleDrop, fieldBottom,
                                  "the busy rule is drawn over the code field");

            Assert.LessOrEqual(LobbyJoinLayout.CodeRuleDrop + MenuBusy.RuleThickness, joinTop,
                               "the busy rule is drawn over the Join button");
        }

        // ─────────────────────────────────────────────── a row's right-hand furniture

        /// <summary>Total width the state, pips and PLAYING marker claim from a row's right edge.</summary>
        private static float FurnitureWidth()
        {
            float pips = LobbySessionPips();

            return LobbyJoinLayout.StateWidth
                   + LobbyJoinLayout.PipsGap + pips
                   + LobbyJoinLayout.PipsGap + LobbyJoinLayout.PlayingWidth;
        }

        private static float LobbySessionPips() =>
            SpaceGame.Core.Lobbies.LobbySession.MaxPlayers * LobbyJoinLayout.PipWidth
            + (SpaceGame.Core.Lobbies.LobbySession.MaxPlayers - 1) * LobbyJoinLayout.PipGap;

        /// <summary>
        /// The name is what the player is scanning for, so it has to keep the majority of the row.
        /// UIBuilder labels truncate rather than wrap, so furniture that grows past this point does
        /// not push the name — it silently eats it.
        /// </summary>
        [Test]
        public void TheSessionNameKeepsMostOfTheRow()
        {
            float nameWidth = LobbyJoinLayout.ListWidth - FurnitureWidth() - 24f;

            Assert.Greater(nameWidth, LobbyJoinLayout.ListWidth * 0.5f,
                           $"only {nameWidth:0} of {LobbyJoinLayout.ListWidth:0} is left for the name");
        }

        /// <summary>
        /// The state slot has to hold the animated "Joining…" caption, not merely "4/4". A slot
        /// sized to the resting content would clip the one message on the row that matters.
        /// </summary>
        [Test]
        public void TheStateSlotFitsTheJoiningCaption()
        {
            // "Joining..." at CaptionSize, bold, measured generously at 0.62em per glyph.
            float caption = "Joining...".Length * MenuEntry.CaptionSize * 0.62f;

            Assert.GreaterOrEqual(LobbyJoinLayout.StateWidth, caption,
                                  "the Joining caption will truncate in its slot");
        }

        [Test]
        public void ThePipsFitBesideTheState()
        {
            Assert.Greater(LobbySessionPips(), 0f);
            Assert.Less(FurnitureWidth(), LobbyJoinLayout.ListWidth,
                        "a row's furniture is wider than the row");
        }
    }
}
