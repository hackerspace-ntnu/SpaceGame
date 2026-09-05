using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what the player wears: the three body slots, positional by <see cref="BodySlot"/>,
    /// and what each worn item has become — the wing pack's deployed craft, chiefly.
    ///
    /// The same format as the hotbar's, through the same <see cref="GearSaveCodec"/>, minus the
    /// selection: a body has no selected slot, every slot is live at once.
    /// </summary>
    public class BodyEquipmentSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "body";

        public struct State
        {
            /// Positional by BodySlot: Back, LeftGauntlet, RightGauntlet. Null is empty.
            public List<string> itemIds;

            /// Positional like itemIds; omitted when nothing has state.
            public List<Dictionary<string, string>> itemStates;
        }

        private IBodyEquipment body;
        private BodyEquipmentController controller;

        private IBodyEquipment Body => body ??= GetComponent<IBodyEquipment>();

        private BodyEquipmentController Controller =>
            controller != null ? controller : controller = GetComponent<BodyEquipmentController>();

        public string SaveKey => Key;

        private List<InventorySlot> Slots()
        {
            var slots = new List<InventorySlot>(GearRef.BodySlotCount);
            for (int i = 0; i < GearRef.BodySlotCount; i++) slots.Add(Body.GetSlot((BodySlot)i));
            return slots;
        }

        public object CaptureState()
        {
            if (Body == null) return null;

            // Worn instances diverge from their slots for as long as they are worn — which, unlike
            // a held item, is until they are moved. Without this the craft a pilot is flying would
            // be saved as "not deployed".
            if (Controller != null) Controller.WriteBackWornState();

            List<InventorySlot> slots = Slots();

            return new State
            {
                itemIds = GearSaveCodec.CaptureIds(slots),
                itemStates = GearSaveCodec.CaptureStates(slots),
            };
        }

        public void RestoreState(JObject state)
        {
            if (Body == null || state == null) return;
            if (state["itemIds"] is not JArray ids) return;

            Body.RestoreSlots(GearSaveCodec.ReadItems(ids, this));

            // After RestoreSlots: assigning an item clears its bag.
            GearSaveCodec.RestoreStates(Slots(), state["itemStates"] as JArray);

            // Restoring the slots wore the items before their bags were back. Second pass.
            if (Controller != null) Controller.ReapplyWornState();
        }

        /// <summary>
        /// Finish any worn item whose state names something else in the world — the wing pack's
        /// craft, which a chunk may hydrate after this player binds. Every worn instance exists,
        /// so every one is asked; each keeps its own pending reference until the referent turns up.
        /// </summary>
        public void OnLoadComplete()
        {
            // Items the hotbar saver could not seat — an older save's fourth slot. Seated now,
            // after every restore has run, so nothing can overwrite them; named when they fit
            // nowhere, so a lost item is at least a loud one.
            if (Body != null)
            {
                foreach (InventoryItem unplaced in Body.DrainOverflow())
                    Debug.LogWarning($"[Save] '{unplaced.itemName}' was in a hotbar slot this build no longer has and fits no free body slot — it was not restored.", this);
            }

            if (Controller == null) return;

            foreach (UsableItem worn in Controller.WornItems)
            {
                if (worn is IItemDeferredRestore pending && pending.HasPendingRestore)
                    pending.TryCompleteRestore();
            }
        }
    }
}
