using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists both compartments of the player's backpack.
    ///
    /// Lives on the player rather than on the pack. The pack is not an independent world object —
    /// <c>BackpackController</c> instantiates one per player in Awake and parents it to the back
    /// socket, deploying it moves that same instance rather than spawning another — so its contents
    /// belong to the player's record. Giving the pack its own record would create a second,
    /// competing copy of the same twenty-two slots.
    ///
    /// As with the hotbar, the component is only wiring: the format lives in
    /// <see cref="BackpackSaveCodec"/>, which takes a <see cref="BackpackContainer"/> directly so it
    /// can be tested without a controller, a socket and a pack prefab.
    /// </summary>
    [RequireComponent(typeof(BackpackController))]
    public class BackpackSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = BackpackSaveCodec.Key;

        private BackpackController controller;

        private BackpackController Controller =>
            controller != null ? controller : controller = GetComponent<BackpackController>();

        /// <summary>
        /// The pack's storage, or null before the controller has built one.
        ///
        /// Read through the controller rather than found in children: deploying unparents the pack
        /// into the world, so a child search would lose it at exactly the moment a player is most
        /// likely to be looking at its contents.
        /// </summary>
        private BackpackContainer Container => Controller != null ? Controller.Pack?.Container : null;

        public string SaveKey => Key;

        public object CaptureState()
        {
            BackpackContainer container = Container;
            return container == null ? null : BackpackSaveCodec.Capture(container);
        }

        public void RestoreState(JObject state)
        {
            BackpackContainer container = Container;
            if (container == null) return;

            BackpackSaveCodec.Restore(container, state, this);
        }
    }

    /// <summary>The backpack's save format and the rules for reading it back.</summary>
    public static class BackpackSaveCodec
    {
        public const string Key = "backpack";

        public struct State
        {
            public List<string> strapItemIds;
            public List<string> mainItemIds;
        }

        public static State Capture(BackpackContainer container) => new()
        {
            // Inventory.GetItemIDs is already positional and already nulls out empty slots, which is
            // exactly the shape Restore reads back.
            strapItemIds = container.Get(BackpackCompartment.Strap).GetItemIDs(),
            mainItemIds = container.Get(BackpackCompartment.Main).GetItemIDs(),
        };

        /// <param name="context">Optional, only for routing warnings to the right object in the console.</param>
        public static void Restore(BackpackContainer container, JObject state, Object context = null)
        {
            if (container == null || state == null) return;

            RestoreCompartment(container.Get(BackpackCompartment.Strap), state["strapItemIds"] as JArray, context);
            RestoreCompartment(container.Get(BackpackCompartment.Main), state["mainItemIds"] as JArray, context);
        }

        private static void RestoreCompartment(Inventory inventory, JArray ids, Object context)
        {
            // A compartment absent from the payload keeps whatever it has. Clearing it would empty
            // the pack of anyone loading a save written before that compartment existed.
            if (inventory == null || ids == null) return;

            int size = inventory.GetSize();

            for (int i = 0; i < size; i++)
            {
                string id = i < ids.Count && ids[i]?.Type == JTokenType.String ? ids[i].Value<string>() : null;

                InventoryItem item = string.IsNullOrEmpty(id) ? null : Registry<InventoryItem>.Get(id);

                if (item == null && !string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[Save] Backpack item '{id}' is not in the registry — its slot " +
                                     "was left empty. Was the item asset deleted?", context);
                }

                inventory.RestoreSlot(i, item);
            }
        }
    }
}
