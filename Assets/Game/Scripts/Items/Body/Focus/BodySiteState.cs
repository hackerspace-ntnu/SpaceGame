namespace SpaceGame.Items
{
    /// <summary>What a site on the body is showing. See <see cref="BodySiteState.Resolve"/>.</summary>
    public enum SiteState
    {
        /// <summary>Nothing worn: the faint generic placeholder.</summary>
        Empty,

        /// <summary>The real worn item, as it is.</summary>
        Worn,

        /// <summary>Carrying something that fits an empty site: a translucent copy of it, seated.</summary>
        Preview,

        /// <summary>Carrying something that fits a filled site: an amber outline on what is worn — a swap.</summary>
        SwapOutline,

        /// <summary>Hovering with something that cannot go here.</summary>
        Refused,

        /// <summary>This site is where the carried item came from.</summary>
        Reserved,

        /// <summary>A legal click was sent and the server has not answered yet. Set by the site, never resolved.</summary>
        Committing,
    }

    /// <summary>
    /// The pure mapping from "what is worn here" and "what the cursor carries" to what the site
    /// shows. <see cref="GearMoves.Resolve"/> is its only source of legality — the same table the
    /// server decides with and the hotbar tiles predict with — so the three never disagree.
    ///
    /// <para>
    /// Separated from every scrap of display code so that it can be tested at all: this project's
    /// EditMode tests cannot raise <c>Awake</c>, so a decision left inside a MonoBehaviour is a
    /// decision nobody can pin. The sites themselves own only the look of each state.
    /// </para>
    /// </summary>
    public static class BodySiteState
    {
        /// <param name="slot">The site being drawn.</param>
        /// <param name="wornKind">Kind of the item worn here, or null when the site is bare.</param>
        /// <param name="carried">Where the cursor picked its item up from, or
        /// <see cref="GearRef.None"/> when the cursor is carrying nothing.</param>
        /// <param name="carriedKind">Kind of the carried item, or null when carrying nothing.</param>
        /// <param name="hovered">Is the cursor over this site right now?</param>
        public static SiteState Resolve(BodySlot slot, EquipKind? wornKind, GearRef carried, EquipKind? carriedKind, bool hovered)
        {
            GearRef here = GearRef.Body(slot);

            if (carried.IsNone) return wornKind == null ? SiteState.Empty : SiteState.Worn;

            // The site the carry came from keeps its own state rather than answering the move
            // question: GearMoves refuses a slot moving onto itself, and rendering that refusal as
            // red would blame the player for picking the item up.
            if (carried == here) return SiteState.Reserved;

            // Not mounted, always: the screen refuses to open from the saddle, so no site is ever
            // resolved in a mounted state. The server still asks the live value — this is the
            // prediction agreeing with the only case that can reach it, not a second opinion.
            MoveResult verdict = GearMoves.Resolve(carried, carriedKind, here, wornKind, mounted: false);
            // Swap-ness is read off the verdict rather than re-derived from "is the target
            // occupied". The two agree today, but only because occupied-and-legal is exactly what
            // GearMoves calls a swap; asking it directly means a future rule that made some
            // occupied move a plain move cannot leave this screen drawing the wrong thing.
            if (verdict.Allowed) return verdict.IsSwap ? SiteState.SwapOutline : SiteState.Preview;

            // Illegal targets stay quiet until the cursor asks: a screen full of red is a screen
            // that tells the player nothing about where the item CAN go.
            if (hovered) return SiteState.Refused;
            return wornKind == null ? SiteState.Empty : SiteState.Worn;
        }
    }
}
