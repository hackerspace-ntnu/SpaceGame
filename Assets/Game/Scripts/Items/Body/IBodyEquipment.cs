using System;
using System.Collections.Generic;

namespace SpaceGame.Items
{
    /// <summary>
    /// The three worn slots on a player, as every caller sees them: the controller that wears the
    /// items, the gear screen that arranges them, the HUD that draws them and the saver that keeps
    /// them. Mirrors <see cref="IPlayerInventory"/>'s role for the hotbar.
    /// </summary>
    public interface IBodyEquipment
    {
        /// <summary>A slot's contents changed — item or, after a move, the per-instance state bag.</summary>
        event Action<BodySlot, InventorySlot> OnBodySlotChanged;

        InventorySlot GetSlot(BodySlot slot);

        /// <summary>
        /// Ask the server to move whatever is in <paramref name="from"/> into <paramref name="to"/>,
        /// swapping if the target is occupied. Either slot may be a hotbar slot. The answer arrives
        /// as slot-change events, or not at all — nothing moves locally.
        /// </summary>
        void RequestMove(GearRef from, GearRef to);

        /// <summary>Server only: assign all three slots at once, as a load does. Positional by <see cref="BodySlot"/>.</summary>
        void RestoreSlots(IReadOnlyList<InventoryItem> items);

        /// <summary>Server only: put an item in the first empty body slot that takes its kind. False when none does.</summary>
        bool TryPlaceInBody(InventoryItem item);

        /// <summary>
        /// Server only: an item another saver could not seat — the fourth entry of a hotbar save
        /// written when the bar was four wide. Held until <see cref="DrainOverflow"/>, which runs
        /// once every saver has restored, so it cannot be overwritten by a body restore that
        /// happens to run later.
        /// </summary>
        void QueueOverflow(InventoryItem item);

        /// <summary>Server only: seat every queued item that fits; return the ones that did not.</summary>
        List<InventoryItem> DrainOverflow();

        /// <summary>Is this player riding something? Nothing on the body moves or fires while they are.</summary>
        bool IsMounted { get; }
    }
}
