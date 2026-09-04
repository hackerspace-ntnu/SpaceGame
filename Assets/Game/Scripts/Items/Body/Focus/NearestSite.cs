using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Which of the body screen's sites a click belongs to when more than one of their boxes
    /// contains the cursor: <b>the site that can take what the cursor is carrying</b>, then the one
    /// nearest the lens, then — among sites the same distance away — the one the cursor is aimed at.
    ///
    /// <para>
    /// <b>What is being carried outranks the geometry.</b> Carrying a gauntlet, the two arms win
    /// and the nearer of them takes the click; carrying a torso item, the torso does (user,
    /// 2026-09-04). It is the one thing the screen knows for certain about the player's intent, and
    /// no measurement of overlapping boxes is a better guess than the item already in their hand.
    /// A site that cannot take the carried item is not thereby unclickable — it is only outranked,
    /// so clicking one still gets the red shake that says why not.
    /// </para>
    /// <para>
    /// Depth is the rule underneath, and nearest centre alone was the whole rule once — it shipped
    /// wrong. A worn ornithopter's wings hang down both flanks, so the torso's projected box
    /// swallowed both arms and a click aimed squarely at a gauntlet lit the torso and beeped. That
    /// particular box is gone (<see cref="BodySite"/> clicks the back at the lash rail's two ends
    /// now), but the rule stands for everything else: when two boxes both contain the cursor, the
    /// one in front is what the player is pointing at.
    /// </para>
    /// <para>
    /// Aim still decides <i>within</i> <see cref="TieMetres"/>, because two sites that close
    /// together are two things in the same place — an arm folded across the chest — and there the
    /// box the cursor is nearest the middle of is the better answer. It is also what picks the
    /// nearer of the two arms, which sit at the same depth as each other on a front-on lens.
    /// </para>
    /// <para>
    /// A pure accumulator rather than a sort: the caller has the distances already and there are
    /// three sites, so there is nothing to allocate and nothing to order. <c>default</c> means
    /// nothing has been offered — see <see cref="Any"/> — so a picker never has to be initialised.
    /// </para>
    /// </summary>
    public struct NearestSite
    {
        /// <summary>
        /// How much nearer the lens one site has to be than another before depth decides between
        /// them, metres. A hand's width: the two things it separates are gear on an arm and gear
        /// on the trunk, which are never this close together unless the arm is across the chest.
        /// </summary>
        public const float TieMetres = 0.1f;

        private int chosenPlusOne;
        private bool bestAccepts;
        private float bestDepth;
        private float bestCentre;

        /// <summary>Whether anything has won yet.</summary>
        public bool Any => chosenPlusOne > 0;

        /// <summary>The winning index, or -1 while nothing has been offered.</summary>
        public int Index => chosenPlusOne - 1;

        /// <param name="index">The caller's own index for this site.</param>
        /// <param name="accepts">Whether this site can take what the cursor is carrying. False for
        /// every site when nothing is carried, which leaves the geometry to decide as it always did.</param>
        /// <param name="depthMetres">How far the site is from the lens.</param>
        /// <param name="cursorToCentreSqr">Squared canvas distance from the cursor to the middle of
        /// the site's box — squared because the caller has it that way and the comparison is the
        /// same either way.</param>
        public void Offer(int index, bool accepts, float depthMetres, float cursorToCentreSqr)
        {
            // Rank one, and it is absolute: a site that can take the carried item beats one that
            // cannot, however the boxes fall. The winner's own depth becomes the bar — not the
            // nearest offered so far — because everything measured against the losing rank is now
            // beside the point.
            if (Any && accepts != bestAccepts)
            {
                if (accepts) Take(index, true, depthMetres, cursorToCentreSqr);
                return;
            }

            bool wins = !Any
                        || depthMetres < bestDepth - TieMetres
                        || (depthMetres < bestDepth + TieMetres && cursorToCentreSqr < bestCentre);
            if (!wins) return;

            // The nearest depth offered so far, not the winner's own: the bar a later site has to
            // beat is the front of the stack, so a site that won on aim from just behind cannot
            // walk it backwards and let something further away in again.
            Take(index, accepts, Any ? Mathf.Min(depthMetres, bestDepth) : depthMetres, cursorToCentreSqr);
        }

        private void Take(int index, bool accepts, float depthMetres, float cursorToCentreSqr)
        {
            bestAccepts = accepts;
            bestDepth = depthMetres;
            bestCentre = cursorToCentreSqr;
            chosenPlusOne = index + 1;
        }
    }
}
