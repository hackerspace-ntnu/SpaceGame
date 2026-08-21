// One thing a trader will swap for one other thing.
//
// Barter, not commerce. There is no currency in this game and inventing one to support trading
// would be the tail wagging the dog — an offer is "this for that", which is both what a desert
// scavenger economy should feel like and what the existing Inventory (which has no stacking and no
// value field) can actually express.
using System;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Trading
{
    [Serializable]
    public class TradeOffer
    {
        [Tooltip("What the trader wants from you.")]
        public InventoryItem wants;

        [Min(1)]
        [Tooltip("How many of it. Inventory has no stacking, so this counts SLOTS holding that item.")]
        public int wantsCount = 1;

        [Tooltip("What they hand over in return.")]
        public InventoryItem gives;

        [Min(1)]
        public int givesCount = 1;

        [TextArea(1, 3)]
        [Tooltip("How they put it. Shown on the offer row — this is where a trader gets a voice.")]
        public string pitch;

        [Tooltip("How many times this offer can be taken. -1 for unlimited. Anything else and the " +
                 "offer is spent once it runs out, which is what makes a trader worth finding early.")]
        public int stock = -1;

        public bool IsValid => wants != null && gives != null;

        public bool InStock => stock != 0;

        /// <summary>Human-readable summary for the UI when no pitch is authored.</summary>
        public string Summary()
        {
            if (!IsValid) return "(incomplete offer)";

            string want = wantsCount > 1 ? $"{wantsCount}x {wants.itemName}" : wants.itemName;
            string give = givesCount > 1 ? $"{givesCount}x {gives.itemName}" : gives.itemName;
            return $"{want}  →  {give}";
        }
    }
}
