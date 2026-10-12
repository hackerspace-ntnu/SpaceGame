using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where a copy of an item lies loose in the world: on the ground, nobody's hands or pack. Any machine; a copy in a
    /// chunk that has not streamed in is not found, which to a waypoint is the same as not lying anywhere.
    /// </summary>
    public static class LooseItems
    {
        /// <summary>How many contacts one search considers. Far more than any one place holds.</summary>
        private const int SearchLimit = 64;

        private static readonly List<ScanContact> Contacts = new(SearchLimit);

        /// <summary>
        /// The nearest loose copy of <paramref name="item"/> within <paramref name="radius"/> of <paramref name="near"/>,
        /// and where it lies.
        /// </summary>
        public static bool TryFind(InventoryItem item, Vector3 near, float radius, out PickupableItem found, out Vector3 position)
        {
            found = null;
            position = default;
            if (item == null) return false;

            ScannerRegistry.Collect(near, radius, Contacts, SearchLimit);

            // Collect sorts nearest first, so the first match is the answer.
            foreach (ScanContact contact in Contacts)
            {
                if (contact.Target is not PickupableItem pickup || pickup.Item != item) continue;

                // The copy in a hand is an Instantiate of the same prefab, pickup and all, and it registers like any
                // other: without this a waypoint follows the player carrying the thing it points them at. The mark is
                // what every equip path puts on it.
                if (BodyAttachment.Covers(pickup.transform, null)) continue;

                found = pickup;
                position = contact.Position;
                return true;
            }

            return false;
        }

        /// <summary>Where the nearest loose copy of <paramref name="item"/> lies.</summary>
        public static bool TryFind(InventoryItem item, Vector3 near, float radius, out Vector3 position) =>
            TryFind(item, near, radius, out _, out position);
    }
}
