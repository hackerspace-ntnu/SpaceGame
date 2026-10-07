// Which site a click on the body screen belongs to when two of their boxes contain the cursor.
//
// Two cases that shipped wrong, in the order the rules now answer them. First: a click aimed at a
// gauntlet while a torso item was on the cursor went to whichever box the geometry liked, when the
// item in hand had already said which slot was meant. Second: a worn ornithopter's wings hang down
// both flanks, so the torso's projected box swallowed the arms, and a click aimed squarely at a
// gauntlet went to the torso — which lit up and beeped — because the wing's box happened to be
// centred nearer the cursor.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class NearestSiteTests
    {
        // Indices as BodyFocusSession uses them: the array is indexed by BodySlot.
        private const int Torso = (int)BodySlot.Torso;
        private const int Left = (int)BodySlot.LeftGauntlet;
        private const int Right = (int)BodySlot.RightGauntlet;

        [Test]
        public void NothingOfferedPicksNothing()
        {
            var pick = new NearestSite();

            Assert.IsFalse(pick.Any);
            Assert.AreEqual(-1, pick.Index);
        }

        // ── Rank one: what the cursor is carrying ────────────────────────────

        [Test]
        public void CarryingAGauntletTheArmWinsOverANearerTorso()
        {
            // The torso is in front AND under the cursor's middle — it wins on every geometric
            // rule there is — and it still loses, because a gauntlet cannot go there.
            var pick = new NearestSite();
            pick.Offer(Torso, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 1f);
            pick.Offer(Right, accepts: true, depthMetres: 4.6f, cursorToCentreSqr: 900f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void CarryingAGauntletTheArmWinsWhicheverOrderTheyCome()
        {
            var pick = new NearestSite();
            pick.Offer(Right, accepts: true, depthMetres: 4.6f, cursorToCentreSqr: 900f);
            pick.Offer(Torso, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 1f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void CarryingATorsoItemTheTorsoWinsOverANearerArm()
        {
            var pick = new NearestSite();
            pick.Offer(Right, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 1f);
            pick.Offer(Torso, accepts: true, depthMetres: 4.6f, cursorToCentreSqr: 900f);

            Assert.AreEqual(Torso, pick.Index);
        }

        [Test]
        public void CarryingAGauntletTheNEARERArmTakesIt()
        {
            // Both arms accept it, so rank one cannot separate them and the cursor's aim decides:
            // the closest gauntlet, which is what the player is pointing at.
            var pick = new NearestSite();
            pick.Offer(Left, accepts: true, depthMetres: 4.0f, cursorToCentreSqr: 900f);
            pick.Offer(Right, accepts: true, depthMetres: 4.0f, cursorToCentreSqr: 16f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void ARefusedSiteStillWinsWhenItIsTheOnlyOneUnderTheCursor()
        {
            // Outranked is not unclickable: clicking a site the carried item cannot go to has to
            // reach it, or there is nothing to shake and no red to explain why not.
            var pick = new NearestSite();
            pick.Offer(Torso, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 1f);

            Assert.AreEqual(Torso, pick.Index);
        }

        // ── Rank two: depth, then aim. Carrying nothing, so nothing accepts. ──

        [Test]
        public void TheNearerSiteWinsEvenWhenTheFurtherOneIsCentredOnTheCursor()
        {
            var pick = new NearestSite();
            pick.Offer(Torso, accepts: false, depthMetres: 4.6f, cursorToCentreSqr: 4f);     // the wing, dead centre
            pick.Offer(Right, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 900f);   // the gauntlet, off to one side

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void OrderDoesNotMatter()
        {
            var pick = new NearestSite();
            pick.Offer(Right, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 900f);
            pick.Offer(Torso, accepts: false, depthMetres: 4.6f, cursorToCentreSqr: 4f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void SitesTheSameDistanceAwayAreDecidedByAim()
        {
            // An arm folded across the chest: the two are in the same place, so the box the cursor
            // is nearest the middle of is the one it is pointing at.
            var pick = new NearestSite();
            pick.Offer(Right, accepts: false, depthMetres: 4.00f, cursorToCentreSqr: 400f);
            pick.Offer(Torso, accepts: false, depthMetres: 4.05f, cursorToCentreSqr: 25f);

            Assert.AreEqual(Torso, pick.Index);
        }

        [Test]
        public void AimCannotWalkTheDepthBarBackwards()
        {
            // A site just behind the front one can win on aim, but the bar the next site has to
            // beat stays the front of the stack — otherwise a third site further back again gets
            // in on a tie against the winner rather than against what is actually nearest.
            var pick = new NearestSite();
            pick.Offer(Right, accepts: false, depthMetres: 4.00f, cursorToCentreSqr: 400f);
            pick.Offer(Torso, accepts: false, depthMetres: 4.05f, cursorToCentreSqr: 25f);
            pick.Offer(Left, accepts: false, depthMetres: 4.12f, cursorToCentreSqr: 1f);

            Assert.AreEqual(Torso, pick.Index);
        }

        [Test]
        public void ASiteFurtherAwayNeverWins()
        {
            var pick = new NearestSite();
            pick.Offer(Right, accepts: false, depthMetres: 4.0f, cursorToCentreSqr: 1f);
            pick.Offer(Torso, accepts: false, depthMetres: 6.0f, cursorToCentreSqr: 0f);

            Assert.AreEqual(Right, pick.Index);
        }

        [Test]
        public void APromotionResetsTheDepthBarToTheWinnersOwn()
        {
            // The losing rank's depth must not survive the promotion: a third site that accepts
            // has to be judged against the accepting winner, not against the nearer thing that was
            // already ruled out for being the wrong slot.
            var pick = new NearestSite();
            pick.Offer(Torso, accepts: false, depthMetres: 3.0f, cursorToCentreSqr: 1f);
            pick.Offer(Right, accepts: true, depthMetres: 5.0f, cursorToCentreSqr: 400f);
            pick.Offer(Left, accepts: true, depthMetres: 4.0f, cursorToCentreSqr: 900f);

            Assert.AreEqual(Left, pick.Index);
        }
    }
}
