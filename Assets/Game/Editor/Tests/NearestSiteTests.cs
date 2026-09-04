// Which site a click on the body screen belongs to when two of their boxes contain the cursor.
// The case that matters is the one that shipped wrong: a worn ornithopter's wings hang down both
// flanks, so the torso's projected box swallows the arms, and a click aimed squarely at a gauntlet
// went to the torso — which lit up and beeped — because the wing's box happened to be centred
// nearer the cursor.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class NearestSiteTests
    {
        // Indices as BodyFocusSession uses them: the array is indexed by BodySlot.
        private const int Torso = (int)BodySlot.Torso;
        private const int Right = (int)BodySlot.RightGauntlet;

        [Test]
        public void NothingOfferedPicksNothing()
        {
            var pick = new NearestSite();

            Assert.IsFalse(pick.Any);
            Assert.AreEqual(-1, pick.Index);
        }

        [Test]
        public void TheNearerSiteWinsEvenWhenTheFurtherOneIsCentredOnTheCursor()
        {
            var pick = new NearestSite();
            pick.Offer(Torso, depthMetres: 4.6f, cursorToCentreSqr: 4f);     // the wing, dead centre
            pick.Offer(Right, depthMetres: 4.0f, cursorToCentreSqr: 900f);   // the gauntlet, off to one side

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void OrderDoesNotMatter()
        {
            var pick = new NearestSite();
            pick.Offer(Right, depthMetres: 4.0f, cursorToCentreSqr: 900f);
            pick.Offer(Torso, depthMetres: 4.6f, cursorToCentreSqr: 4f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void SitesTheSameDistanceAwayAreDecidedByAim()
        {
            // An arm folded across the chest: the two are in the same place, so the box the cursor
            // is nearest the middle of is the one it is pointing at.
            var pick = new NearestSite();
            pick.Offer(Right, depthMetres: 4.00f, cursorToCentreSqr: 400f);
            pick.Offer(Torso, depthMetres: 4.05f, cursorToCentreSqr: 25f);

            Assert.AreEqual(Torso, pick.Index);
        }

        [Test]
        public void AimCannotWalkTheDepthBarBackwards()
        {
            // A site just behind the front one can win on aim, but the bar the next site has to
            // beat stays the front of the stack — otherwise a third site further back again gets
            // in on a tie against the winner rather than against what is actually nearest.
            var pick = new NearestSite();
            pick.Offer(Right, depthMetres: 4.00f, cursorToCentreSqr: 400f);
            pick.Offer(Torso, depthMetres: 4.05f, cursorToCentreSqr: 25f);
            pick.Offer((int)BodySlot.LeftGauntlet, depthMetres: 4.12f, cursorToCentreSqr: 1f);

            Assert.AreEqual(Torso, pick.Index);
        }

        [Test]
        public void ASiteFurtherAwayNeverWins()
        {
            var pick = new NearestSite();
            pick.Offer(Right, depthMetres: 4.0f, cursorToCentreSqr: 1f);
            pick.Offer(Torso, depthMetres: 6.0f, cursorToCentreSqr: 0f);

            Assert.AreEqual(Right, pick.Index);
        }
    }
}
