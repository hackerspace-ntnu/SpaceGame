using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where a scroll step lands on the hotbar. Shared by the local and networked inventories so
    /// the two cannot drift apart.
    /// </summary>
    public static class HotbarNavigation
    {
        /// <summary>Returned when the step would not move the selection anywhere new.</summary>
        public const int NoChange = -1;

        /// <summary>
        /// The slot a scroll step should select, clamped to both ends of the hotbar: scrolling past
        /// the first or last slot stays put rather than wrapping around.
        /// <para>
        /// Returns <see cref="NoChange"/> when the selection would not move. That matters because
        /// SelectSlot treats a repeat of the currently selected index as a request to deselect, so
        /// re-sending the clamped index would empty the player's hands every time they scrolled
        /// into the end of the bar.
        /// </para>
        /// </summary>
        /// <param name="currentSlot">Selected slot, or any negative value for an empty selection.</param>
        /// <param name="direction">Negative for the previous slot, positive for the next.</param>
        public static int GetScrollTarget(int currentSlot, int direction, int inventorySize)
        {
            if (inventorySize <= 0 || direction == 0) return NoChange;

            // Nothing selected leaves no slot to step from, so either direction takes the first one.
            int target = currentSlot < 0 || currentSlot >= inventorySize
                ? 0
                : Mathf.Clamp(currentSlot + (direction > 0 ? 1 : -1), 0, inventorySize - 1);

            return target == currentSlot ? NoChange : target;
        }
    }
}
