// What a site on the body shows, for every combination of what is worn there and what the cursor
// carries. Legality comes from GearMoves, the same table the server uses and the tiles predict
// with, so a site can never light amber for a move the server would refuse.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BodySiteStateTests
    {
        private static readonly GearRef Hot0 = GearRef.Hotbar(0);
        private static readonly GearRef Left = GearRef.Body(BodySlot.LeftGauntlet);

        [Test]
        public void NothingCarriedShowsWhatIsThere()
        {
            Assert.AreEqual(SiteState.Empty, BodySiteState.Resolve(BodySlot.LeftGauntlet, null, GearRef.None, null, hovered: false));
            Assert.AreEqual(SiteState.Worn, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, GearRef.None, null, hovered: true));
        }

        [Test]
        public void ALegalCarryOverAnEmptySiteIsAPreview()
        {
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.RightGauntlet, null, Hot0, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.Torso, null, Hot0, EquipKind.Back, hovered: true));
        }

        [Test]
        public void ALegalCarryOverAFilledSiteIsASwap()
        {
            Assert.AreEqual(SiteState.SwapOutline, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Gauntlet, hovered: false));
        }

        [Test]
        public void AnIllegalCarryIsOnlyRefusedWhileHovered()
        {
            Assert.AreEqual(SiteState.Refused, BodySiteState.Resolve(BodySlot.Torso, null, Hot0, EquipKind.Gauntlet, hovered: true));
            Assert.AreEqual(SiteState.Empty, BodySiteState.Resolve(BodySlot.Torso, null, Hot0, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Refused, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Hand, hovered: true));
            Assert.AreEqual(SiteState.Worn, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Hand, hovered: false));
        }

        [Test]
        public void TheOriginOfTheCarryIsReserved()
        {
            Assert.AreEqual(SiteState.Reserved, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Reserved, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, hovered: true));
        }

        [Test]
        public void AGauntletCarriedFromOneArmPreviewsOnTheOther()
        {
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.RightGauntlet, null, Left, EquipKind.Gauntlet, hovered: false));
        }
    }
}
