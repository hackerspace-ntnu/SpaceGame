using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One slot on the hotbar: a <see cref="GearTile"/> plus the bridge from its clicks to the bar.
    ///
    /// <para>
    /// The tile draws itself — the same tile the worn-gear strip and the body screen use, so a
    /// slot looks the same everywhere it appears. What a click MEANS is the pack's question, not
    /// the tile's: the pointer events are handed to <c>PackHandController</c> through
    /// <see cref="InventoryUI"/>, which is already hit-testing the pack every frame and already
    /// knows what a legal placement is. This project has no EventSystem drag plumbing at all.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotUI : MonoBehaviour
    {
        private int slotIndex;
        private InventoryUI parentUI;
        private GearTile tile;

        /// <summary>This slot's rectangle, for the cursor hit-test in <see cref="InventoryUI"/>.</summary>
        public RectTransform Rect => tile != null ? tile.Rect : null;

        /// <summary>Makes one slot under <paramref name="parent"/>, ready for <see cref="Refresh"/>.</summary>
        public static InventorySlotUI Build(RectTransform parent, int index, InventoryUI owner)
        {
            GearTile tile = GearTile.Build(parent, $"Slot {index + 1}", (index + 1).ToString());

            var slot = tile.gameObject.AddComponent<InventorySlotUI>();
            slot.tile = tile;
            slot.Init(index, owner);

            tile.Clicked += slot.OnClicked;
            tile.HoverChanged += slot.OnHoverChanged;

            return slot;
        }

        public void Init(int index, InventoryUI parent)
        {
            slotIndex = index;
            parentUI = parent;

            if (tile != null) tile.SetKeyLabel((index + 1).ToString());
        }

        /// <summary>Shows the slot as it now stands.</summary>
        /// <param name="isDropTarget">The cursor is over this slot with something in hand, so a
        /// click would land it here.</param>
        /// <param name="isReserved">This slot's item is in the player's hand. The tile reads as
        /// empty, but as an empty tile that is spoken for.</param>
        public void Refresh(InventorySlot slot, bool isSelected, bool isHovered,
                            bool isDropTarget = false, bool isReserved = false)
        {
            if (tile == null) return;

            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            bool worn = item != null && !BodySlotRules.HandEquips(item.equipKind);

            tile.Refresh(item, isSelected, isHovered, isDropTarget, isRefused: false,
                         isReserved: isReserved, isWorn: worn);
        }

        /// <summary>The slot saying "no" — see <see cref="GearTile.Shake"/>.</summary>
        public void Shake() => tile?.Shake();

        private void OnHoverChanged(GearTile _, bool over)
        {
            if (parentUI == null) return;

            if (over) parentUI.OnSlotHovered(slotIndex);
            else parentUI.OnSlotUnhovered(slotIndex);
        }

        /// <summary>
        /// Left click: this slot's item goes into the player's hand, or whatever is already in
        /// their hand goes into this slot. The same verb the mat uses, on the same button — the
        /// bar and the pack are one surface as far as the player is concerned, and a slot that
        /// needed a different gesture would break that.
        /// </summary>
        private void OnClicked(GearTile _)
        {
            if (parentUI != null) parentUI.ClickSlot(slotIndex);
        }
    }
}
