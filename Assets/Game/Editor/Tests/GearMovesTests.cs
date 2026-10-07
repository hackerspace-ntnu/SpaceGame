// Every branch of the server's move decision, in the order the server takes them.
//
// The gear screen predicts this answer for its hover colour, so a branch that disagrees between the
// two would show green and then do nothing — which is the failure this table exists to prevent.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class GearMovesTests
    {
        private static readonly GearRef Hot0 = GearRef.Hotbar(0);
        private static readonly GearRef Hot1 = GearRef.Hotbar(1);
        private static readonly GearRef Left = GearRef.Body(BodySlot.LeftGauntlet);
        private static readonly GearRef Right = GearRef.Body(BodySlot.RightGauntlet);
        private static readonly GearRef Back = GearRef.Body(BodySlot.Torso);

        [Test]
        public void GauntletIntoAnEmptyGauntletSlotMoves()
        {
            MoveResult r = GearMoves.Resolve(Hot0, EquipKind.Gauntlet, Left, null, mounted: false);
            Assert.IsTrue(r.Allowed);
            Assert.IsFalse(r.IsSwap);
        }

        [Test]
        public void GauntletIntoTheBackIsRefused()
        {
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Gauntlet, Back, null, false).Allowed);
        }

        [Test]
        public void HandItemIntoAGauntletSlotIsRefused()
        {
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Hand, Right, null, false).Allowed);
        }

        [Test]
        public void WingsIntoTheBackMove()
        {
            Assert.IsTrue(GearMoves.Resolve(Hot1, EquipKind.Back, Back, null, false).Allowed);
        }

        [Test]
        public void TwoGauntletsSwapAcrossTheLists()
        {
            MoveResult r = GearMoves.Resolve(Hot0, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, false);
            Assert.IsTrue(r.Allowed);
            Assert.IsTrue(r.IsSwap);
        }

        [Test]
        public void SwappingAHandItemOntoTheBodyIsRefused()
        {
            // The gauntlet could come down to the hotbar, but the hand item cannot go up.
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Hand, Left, EquipKind.Gauntlet, false).Allowed);
        }

        [Test]
        public void GauntletsSwapBetweenArms()
        {
            Assert.IsTrue(GearMoves.Resolve(Left, EquipKind.Gauntlet, Right, EquipKind.Gauntlet, false).IsSwap);
        }

        [Test]
        public void HotbarToHotbarAlwaysMoves()
        {
            Assert.IsTrue(GearMoves.Resolve(Hot0, EquipKind.Back, Hot1, EquipKind.Hand, false).IsSwap);
            Assert.IsTrue(GearMoves.Resolve(Hot0, EquipKind.Gauntlet, Hot1, null, false).Allowed);
        }

        [Test]
        public void AnEmptySourceIsRefused()
        {
            Assert.IsFalse(GearMoves.Resolve(Hot0, null, Left, null, false).Allowed);
        }

        [Test]
        public void TheSameSlotIsRefused()
        {
            Assert.IsFalse(GearMoves.Resolve(Left, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, false).Allowed);
        }

        [Test]
        public void NothingMovesWhileMounted()
        {
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Gauntlet, Left, null, mounted: true).Allowed);
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Hand, Hot1, null, mounted: true).Allowed);
        }

        [Test]
        public void NoSuchSlotIsRefused()
        {
            Assert.IsFalse(GearMoves.Resolve(GearRef.None, EquipKind.Hand, Hot0, null, false).Allowed);
            Assert.IsFalse(GearMoves.Resolve(Hot0, EquipKind.Hand, GearRef.None, null, false).Allowed);
        }
    }
}
