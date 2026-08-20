// Where the scroll wheel lands on the hotbar.
//
// The trap these pin: SelectSlot toggles. Handing it the index that is already selected clears the
// selection instead of keeping it, so a clamped step at either end of the bar cannot simply re-send
// the current slot -- doing that would empty the player's hands the moment they scrolled into the
// wall. GetScrollTarget answers NoChange there instead, and the callers skip the call.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class HotbarNavigationTests
    {
        private const int Size = 4;

        [Test]
        public void ScrollingUpSelectsThePreviousSlot()
        {
            Assert.AreEqual(1, HotbarNavigation.GetScrollTarget(2, -1, Size));
        }

        [Test]
        public void ScrollingDownSelectsTheNextSlot()
        {
            Assert.AreEqual(3, HotbarNavigation.GetScrollTarget(2, 1, Size));
        }

        [Test]
        public void ScrollingUpFromTheFirstSlotStaysPut()
        {
            Assert.AreEqual(HotbarNavigation.NoChange, HotbarNavigation.GetScrollTarget(0, -1, Size));
        }

        [Test]
        public void ScrollingDownFromTheLastSlotStaysPut()
        {
            Assert.AreEqual(HotbarNavigation.NoChange, HotbarNavigation.GetScrollTarget(Size - 1, 1, Size));
        }

        [Test]
        public void ScrollingNeverWrapsAround()
        {
            int slot = 0;
            for (int i = 0; i < Size * 2; i++)
            {
                int target = HotbarNavigation.GetScrollTarget(slot, 1, Size);
                if (target != HotbarNavigation.NoChange) slot = target;
            }

            Assert.AreEqual(Size - 1, slot, "Scrolling off the end wrapped back to the start.");
        }

        [Test]
        public void ScrollingWithNothingSelectedTakesTheFirstSlot()
        {
            Assert.AreEqual(0, HotbarNavigation.GetScrollTarget(-1, 1, Size));
            Assert.AreEqual(0, HotbarNavigation.GetScrollTarget(-1, -1, Size));
        }

        [Test]
        public void ScrollingNeverClearsTheSelection()
        {
            for (int slot = 0; slot < Size; slot++)
            {
                foreach (int direction in new[] { -1, 1 })
                {
                    int target = HotbarNavigation.GetScrollTarget(slot, direction, Size);
                    Assert.That(target == HotbarNavigation.NoChange || target >= 0,
                        $"Slot {slot} scrolling {direction} asked for an empty selection.");
                }
            }
        }

        [Test]
        public void AnEmptyHotbarIsLeftAlone()
        {
            Assert.AreEqual(HotbarNavigation.NoChange, HotbarNavigation.GetScrollTarget(-1, 1, 0));
        }

        [Test]
        public void ASingleSlotHotbarCannotDeselectItself()
        {
            Assert.AreEqual(HotbarNavigation.NoChange, HotbarNavigation.GetScrollTarget(0, 1, 1));
            Assert.AreEqual(HotbarNavigation.NoChange, HotbarNavigation.GetScrollTarget(0, -1, 1));
        }

        [Test]
        public void ASelectionPastTheEndOfTheBarRecovers()
        {
            Assert.AreEqual(0, HotbarNavigation.GetScrollTarget(Size + 3, -1, Size));
        }
    }
}
