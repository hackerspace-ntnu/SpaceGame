using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Which of the body screen's sites a click belongs to when more than one of their boxes
    /// contains the cursor: <b>the one nearest the lens</b>, and among sites the same distance
    /// away, the one the cursor is aimed at.
    ///
    /// <para>
    /// Nearest centre alone was the whole rule once, and it shipped wrong. A worn ornithopter's
    /// wings hang down both flanks, so the torso's projected box swallows both arms — a click
    /// aimed squarely at a gauntlet lit the torso and beeped. Both boxes contain the cursor either
    /// way; the only thing that separates them is which of the two is in front, and in front is
    /// what the player is pointing at.
    /// </para>
    /// <para>
    /// Aim still decides <i>within</i> <see cref="TieMetres"/>, because two sites that close
    /// together are two things in the same place — an arm folded across the chest — and there the
    /// box the cursor is nearest the middle of is the better answer.
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
        private float bestDepth;
        private float bestCentre;

        /// <summary>Whether anything has won yet.</summary>
        public bool Any => chosenPlusOne > 0;

        /// <summary>The winning index, or -1 while nothing has been offered.</summary>
        public int Index => chosenPlusOne - 1;

        /// <param name="index">The caller's own index for this site.</param>
        /// <param name="depthMetres">How far the site is from the lens.</param>
        /// <param name="cursorToCentreSqr">Squared canvas distance from the cursor to the middle of
        /// the site's box — squared because the caller has it that way and the comparison is the
        /// same either way.</param>
        public void Offer(int index, float depthMetres, float cursorToCentreSqr)
        {
            bool wins = !Any
                        || depthMetres < bestDepth - TieMetres
                        || (depthMetres < bestDepth + TieMetres && cursorToCentreSqr < bestCentre);
            if (!wins) return;

            // The nearest depth offered so far, not the winner's own: the bar a later site has to
            // beat is the front of the stack, so a site that won on aim from just behind cannot
            // walk it backwards and let something further away in again.
            bestDepth = Any ? Mathf.Min(depthMetres, bestDepth) : depthMetres;
            bestCentre = cursorToCentreSqr;
            chosenPlusOne = index + 1;
        }
    }
}
