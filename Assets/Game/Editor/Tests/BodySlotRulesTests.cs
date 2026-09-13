// What fits where on the body, pinned as a truth table.
//
// The server refuses a move with these rules and the gear screen colours its hover with them; both
// read this one class, so the table is the whole contract. In Editor/ because the types live in
// Assembly-CSharp, which an asmdef'd test cannot reference.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BodySlotRulesTests
    {
        [TestCase(BodySlot.Torso, EquipKind.Back, true)]
        [TestCase(BodySlot.Torso, EquipKind.Chest, true)]
        [TestCase(BodySlot.Torso, EquipKind.Gauntlet, false)]
        [TestCase(BodySlot.Torso, EquipKind.Hand, false)]
        [TestCase(BodySlot.LeftGauntlet, EquipKind.Gauntlet, true)]
        [TestCase(BodySlot.LeftGauntlet, EquipKind.Back, false)]
        [TestCase(BodySlot.LeftGauntlet, EquipKind.Chest, false)]
        [TestCase(BodySlot.LeftGauntlet, EquipKind.Hand, false)]
        [TestCase(BodySlot.RightGauntlet, EquipKind.Gauntlet, true)]
        [TestCase(BodySlot.RightGauntlet, EquipKind.Back, false)]
        [TestCase(BodySlot.RightGauntlet, EquipKind.Chest, false)]
        [TestCase(BodySlot.RightGauntlet, EquipKind.Hand, false)]
        public void BodySlotsTakeOnlyTheirOwnKinds(BodySlot slot, EquipKind kind, bool expected)
        {
            Assert.AreEqual(expected, BodySlotRules.Accepts(slot, kind));
        }

        [TestCase(EquipKind.Hand)]
        [TestCase(EquipKind.Gauntlet)]
        [TestCase(EquipKind.Back)]
        public void AHotbarSlotStoresAnything(EquipKind kind)
        {
            Assert.IsTrue(BodySlotRules.Accepts(GearRef.Hotbar(0), kind),
                "the hotbar is storage; a worn item lying there is inert, not refused");
        }

        [Test]
        public void NoSlotAcceptsNothing()
        {
            Assert.IsFalse(BodySlotRules.Accepts(GearRef.None, EquipKind.Hand));
        }

        [Test]
        public void OnlyHandItemsGoIntoTheHand()
        {
            Assert.IsTrue(BodySlotRules.HandEquips(EquipKind.Hand));
            Assert.IsFalse(BodySlotRules.HandEquips(EquipKind.Gauntlet));
            Assert.IsFalse(BodySlotRules.HandEquips(EquipKind.Back));
        }

        [Test]
        public void FirstSlotForEachKind()
        {
            Assert.AreEqual(BodySlot.Torso, BodySlotRules.FirstSlotFor(EquipKind.Back));
            Assert.AreEqual(BodySlot.Torso, BodySlotRules.FirstSlotFor(EquipKind.Chest));
            Assert.AreEqual(BodySlot.LeftGauntlet, BodySlotRules.FirstSlotFor(EquipKind.Gauntlet));
            Assert.IsNull(BodySlotRules.FirstSlotFor(EquipKind.Hand), "a hand item has no body slot");
        }

        [Test]
        public void BackAndChestGearShareOneSlotAndSoExcludeEachOther()
        {
            // The design asks for "one or the other". This is the whole of the mechanism: both
            // kinds resolve to the SAME slot, so wearing one displaces the other through the
            // ordinary swap and no exclusion rule exists to be forgotten. If a later change gives
            // the chest a slot of its own, this test is the one that must be argued with first.
            Assert.AreEqual(BodySlotRules.FirstSlotFor(EquipKind.Back),
                            BodySlotRules.FirstSlotFor(EquipKind.Chest));

            Assert.AreEqual(3, GearRef.BodySlotCount,
                "old saves are positional by BodySlot; adding a slot here silently reads them wrong");
        }
    }
}
