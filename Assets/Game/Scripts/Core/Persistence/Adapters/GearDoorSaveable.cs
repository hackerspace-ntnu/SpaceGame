using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the gear doors of an entity (the workshop's gate and its vault): which gear lies in each slot, where
    /// that differs from the start. A gear on a peg and a gear gone from the pile are items out of somebody's hotbar;
    /// without this the pile would lie there again on the next load and the pegs would be bare, and a gate the players
    /// opened would stand shut.
    ///
    /// <para>
    /// On the entity's root, beside its <c>SaveableEntity</c>; the doors are somewhere below it. Every door's slots
    /// share one table, named by <see cref="GearDoor.SlotName"/> (the gate's bare, as saves from before the vault have
    /// them; any other door's behind its own id). Server-authoritative on the way back in: each door lands every slot
    /// and announces it, so clients follow by message.
    /// </para>
    /// </summary>
    public class GearDoorSaveable : MonoBehaviour, ISaveable
    {
        /// <summary>Written into save files. NEVER rename.</summary>
        public const string Key = "gearDoor";

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>The item id lying in each slot that differs from the start, by slot name; "" for an empty one.</summary>
            public Dictionary<string, string> slots;
        }

        // Looked up each time, not cached in Awake: EditMode tests never run Awake.
        private GearDoor[] Doors => GetComponentsInChildren<GearDoor>(true);

        public object CaptureState()
        {
            var slots = new Dictionary<string, string>();
            foreach (GearDoor door in Doors)
            {
                for (int slot = 0; slot < door.SlotCount; slot++)
                {
                    int kind = door.KindIn(slot);
                    if (kind == door.StartKind(slot)) continue;
                    slots[door.SlotName(slot)] = kind < 0 ? string.Empty : door.KindAt(kind).item.ID;
                }
            }

            return slots.Count == 0 ? null : new State { slots = slots };   // the authored start: nothing to say
        }

        public void RestoreState(JObject state)
        {
            State restored = state != null ? state.ToObject<State>(SaveSerializer.Serializer) : default;
            foreach (GearDoor door in Doors) Restore(door, restored.slots);
        }

        private void Restore(GearDoor door, Dictionary<string, string> saved)
        {
            var kinds = new int[door.SlotCount];
            for (int slot = 0; slot < kinds.Length; slot++)
            {
                kinds[slot] = door.StartKind(slot);
                if (saved == null || !saved.TryGetValue(door.SlotName(slot), out string id)) continue;
                if (string.IsNullOrEmpty(id))
                {
                    kinds[slot] = -1;
                    continue;
                }

                int kind = door.KindById(id);
                if (kind >= 0)
                {
                    kinds[slot] = kind;
                    continue;
                }

                // A gear kind this build no longer has: the slot keeps its start rather than losing the gear in silence.
                Debug.LogError($"[GearDoor] the save names item '{id}' in {door.SlotName(slot)}, which '{door.name}' " +
                               "has no gear kind for; the slot is left as it starts.", this);
            }
            door.Restore(kinds);
        }
    }
}
