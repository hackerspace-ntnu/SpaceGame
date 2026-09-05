// The shape of the body screen's rail, which is the one thing about that screen worth asserting:
// a layout written as offsets inside a builder can only be checked by opening the game and looking
// at it, and "the torso tile is above the gauntlets" is a claim, not a matter of taste.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class GearRailLayoutTests
    {
        private static IReadOnlyList<GearRailLayout.Placement> Rail => GearRailLayout.Build(3);

        private static Vector2 At(GearRef slot) =>
            Rail.First(p => p.Slot == slot).At;

        private static Vector2 Body(BodySlot slot) => At(GearRef.Body(slot));
        private static Vector2 Hand(int index) => At(GearRef.Hotbar(index));

        [Test]
        public void EverySlotGetsExactlyOneTile()
        {
            IReadOnlyList<GearRailLayout.Placement> rail = Rail;

            Assert.AreEqual(6, rail.Count);
            Assert.AreEqual(6, rail.Select(p => p.Slot).Distinct().Count());
        }

        [Test]
        public void TheRowsDescendTorsoThenGauntletsThenHands()
        {
            Assert.Greater(Body(BodySlot.Torso).y, Body(BodySlot.LeftGauntlet).y);
            Assert.Greater(Body(BodySlot.LeftGauntlet).y, Hand(0).y);
        }

        [Test]
        public void TheTwoGauntletsShareARow()
        {
            Assert.AreEqual(Body(BodySlot.LeftGauntlet).y, Body(BodySlot.RightGauntlet).y, 0.001f);
        }

        [Test]
        public void TheThreeHandSlotsShareARow()
        {
            Assert.AreEqual(Hand(0).y, Hand(1).y, 0.001f);
            Assert.AreEqual(Hand(1).y, Hand(2).y, 0.001f);
        }

        [Test]
        public void QSitsLeftOfE()
        {
            // Mirrored against the figure behind it, deliberately: the tile is labelled with the
            // key, and the key is what the player presses.
            Assert.Less(Body(BodySlot.LeftGauntlet).x, Body(BodySlot.RightGauntlet).x);
        }

        [Test]
        public void TheHandSlotsRunLeftToRightInNumberOrder()
        {
            Assert.Less(Hand(0).x, Hand(1).x);
            Assert.Less(Hand(1).x, Hand(2).x);
        }

        [Test]
        public void EveryRowIsCentredOnTheSameColumn()
        {
            Assert.AreEqual(GearRailLayout.CentreFromLeft, Body(BodySlot.Torso).x, 0.001f);
            Assert.AreEqual(GearRailLayout.CentreFromLeft,
                            (Body(BodySlot.LeftGauntlet).x + Body(BodySlot.RightGauntlet).x) * 0.5f, 0.001f);
            Assert.AreEqual(GearRailLayout.CentreFromLeft, Hand(1).x, 0.001f);
        }

        [Test]
        public void TheWidestRowClearsTheLeftEdge()
        {
            // The rail is anchored to the left edge, so a tile whose left side is negative is off
            // screen — the failure that decides how far in the block sits.
            Assert.Greater(Hand(0).x - HotbarStyle.SlotWidth * 0.5f, 0f);
        }

        [Test]
        public void TheBlockIsCentredOnItsAnchor()
        {
            Assert.AreEqual(0f, Body(BodySlot.Torso).y + Hand(0).y, 0.001f);
        }

        [Test]
        public void CaptionsBracketThePyramidWithoutTouchingIt()
        {
            Assert.Greater(GearRailLayout.CaptionAboveY, Body(BodySlot.Torso).y + HotbarStyle.SlotHeight * 0.5f);
            Assert.Less(GearRailLayout.CaptionBelowY, Hand(0).y - HotbarStyle.SlotHeight * 0.5f);
        }

        [Test]
        public void AFourthHandSlotStaysCentredAndInOrder()
        {
            // GetInventorySize is what builds the hand row, so the row has to survive a hotbar that
            // is not three wide rather than assume the number it has today.
            IReadOnlyList<GearRailLayout.Placement> rail = GearRailLayout.Build(4);
            List<float> hands = rail.Where(p => p.Slot.IsHotbar).Select(p => p.At.x).ToList();

            Assert.AreEqual(4, hands.Count);
            CollectionAssert.AreEqual(hands.OrderBy(x => x).ToList(), hands);
            Assert.AreEqual(GearRailLayout.CentreFromLeft, (hands[0] + hands[3]) * 0.5f, 0.001f);
        }

        [Test]
        public void EveryTileIsLabelledWithItsKey()
        {
            IReadOnlyList<GearRailLayout.Placement> rail = Rail;

            Assert.AreEqual("Q", rail.First(p => p.Slot == GearRef.Body(BodySlot.LeftGauntlet)).Key);
            Assert.AreEqual("E", rail.First(p => p.Slot == GearRef.Body(BodySlot.RightGauntlet)).Key);
            Assert.AreEqual("1", rail.First(p => p.Slot == GearRef.Hotbar(0)).Key);
            Assert.IsNotEmpty(rail.First(p => p.Slot == GearRef.Body(BodySlot.Torso)).Key);
        }
    }
}
