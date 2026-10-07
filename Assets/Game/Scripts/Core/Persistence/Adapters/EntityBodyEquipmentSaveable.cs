using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// What an NPC wears, positional by BodySlot (append-only, like the slots themselves). Item ids only:
    /// no per-item state, because the one worn item with state — a deployed wing pack — belongs to a
    /// flier, and a flier is never saved (D4).
    /// </summary>
    public class EntityBodyEquipmentSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "npcWorn";     // written into save files — NEVER rename
        public string SaveKey => Key;

        public struct State
        {
            public List<string> items;
        }

        private EntityBodyEquipment body;

        // Lazy, not cached in Awake: EditMode tests never run Awake.
        private EntityBodyEquipment Body => body != null ? body : body = GetComponent<EntityBodyEquipment>();

        public object CaptureState()
        {
            if (Body == null) return null;

            var ids = new List<string>(GearRef.BodySlotCount);
            for (int i = 0; i < GearRef.BodySlotCount; i++)
            {
                InventoryItem item = Body.ItemIn((BodySlot)i);
                ids.Add(item != null ? item.ID : string.Empty);
            }
            return new State { items = ids };
        }

        public void RestoreState(JObject state)
        {
            // Null: nothing saved for this NPC, so the prefab's starting gear stands.
            if (state == null || Body == null) return;
            if (state["items"] is not JArray items)
            {
                // Malformed, not "nothing worn": stripping the body on a bad record would lose gear quietly.
                Debug.LogWarning($"[Save] '{name}' has an {Key} record with no items array; its worn gear was left as it is.", this);
                return;
            }
            Body.RestoreWorn(GearSaveCodec.ReadItems(items, this));
        }
    }
}
