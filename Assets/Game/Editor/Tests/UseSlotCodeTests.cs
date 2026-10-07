// How a gear slot rides in NetArg.A.
//
// The property that matters: a hotbar slot's code IS its index, unchanged from before body slots
// existed, so the server's stale-slot guard keeps reading it as it always has. Body codes must be
// numbers no hotbar could produce.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class UseSlotCodeTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void HotbarCodesAreTheBareIndex(int index)
        {
            Assert.AreEqual(index, UseSlotCode.Encode(GearRef.Hotbar(index)));
        }

        [TestCase(BodySlot.Torso)]
        [TestCase(BodySlot.LeftGauntlet)]
        [TestCase(BodySlot.RightGauntlet)]
        public void BodyCodesRoundTrip(BodySlot slot)
        {
            int code = UseSlotCode.Encode(GearRef.Body(slot));

            Assert.GreaterOrEqual(code, 256, "a body code must be out of any hotbar's range");
            Assert.AreEqual(GearRef.Body(slot), UseSlotCode.Decode(code));
            Assert.AreEqual(GearArea.Body, UseSlotCode.AreaOf(code));
        }

        [Test]
        public void HotbarCodesRoundTrip()
        {
            Assert.AreEqual(GearRef.Hotbar(2), UseSlotCode.Decode(2));
            Assert.AreEqual(GearArea.Hotbar, UseSlotCode.AreaOf(2));
        }

        [Test]
        public void NothingSelectedIsMinusOne()
        {
            Assert.AreEqual(-1, UseSlotCode.Encode(GearRef.None));
            Assert.IsTrue(UseSlotCode.Decode(-1).IsNone);
        }
    }
}
