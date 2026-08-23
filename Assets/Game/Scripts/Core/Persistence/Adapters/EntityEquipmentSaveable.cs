using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which of an NPC's own items is actually in its hand.
    ///
    /// <b>The half <see cref="EntityInventorySaveable"/> does not cover.</b> That saver keeps WHAT is
    /// in the bag; this keeps WHICH of it is drawn, and they are genuinely different questions with
    /// different answers. The equipped slot is written at runtime by <c>EquipSlot</c>,
    /// <c>EquipFirstAvailable</c> and by an item running dry mid-fight, and none of those leave a
    /// trace in the inventory. Worse, <c>Start</c> unconditionally equips the authored
    /// <c>startingSlot</c> — so an NPC that picked up and equipped a looted rifle comes back holding
    /// the pistol its prefab ships with, with the rifle still in its bag.
    ///
    /// <b>Applied twice on purpose.</b> Restoring the slot only means something once the inventory
    /// holds what the save says it holds, and two savers on one entity run in component order, which
    /// is not an ordering anyone may depend on. So the equip is applied as the record lands (right
    /// when the inventory saver ran first, which is the common case) and again once the load has
    /// settled (right in every other case). <c>EquipSlot</c> returns early when the slot asked for is
    /// already in hand, so the second pass re-spawns nothing.
    ///
    /// The aim point comes along because the item is pointed at it every LateUpdate: without it a
    /// restored gunman spends a frame with his weapon swung back to its rest pose.
    /// </summary>
    [RequireComponent(typeof(EntityEquipmentController))]
    public class EntityEquipmentSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "entityEquipment";

        private EntityEquipmentController equipment;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private EntityEquipmentController Equipment =>
            equipment != null ? equipment : equipment = GetComponent<EntityEquipmentController>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>-1 for empty-handed, which is a real state and not a missing value.</summary>
            public int equippedSlot;

            public float autoUseTimer;
            public bool hasAimPoint;
            public Vector3 aimPoint;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            if (Equipment == null) return null;

            return new State
            {
                equippedSlot = Equipment.EquippedSlotIndex,
                autoUseTimer = Equipment.AutoUseTimer,
                hasAimPoint = Equipment.HasAimPoint,
                aimPoint = Equipment.AimPoint,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Equipment == null) return;

            hasPending = false;
            pending = default;

            if (state == null)
            {
                // Nothing recorded means empty-handed at the moment of the save. Applied rather than
                // ignored, because "put your weapon away" is exactly what a saver must be able to say
                // — and because the startingSlot latch means nothing else will now say it.
                Equipment.RestoreEquipment(-1, 0f, false, Vector3.zero);
                return;
            }

            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            Apply(in pending);
        }

        /// <summary>
        /// Idempotent: <c>EquipSlot</c> no-ops when the wanted slot is already in hand, and the aim is
        /// an assignment. Kept pending rather than consumed on the first pass, because a chunk that
        /// hydrates later may bring the inventory with it.
        /// </summary>
        public void OnLoadComplete()
        {
            if (!hasPending || Equipment == null) return;

            Apply(in pending);

            // Consumed once the equip actually took, or once the record asked for empty hands. An NPC
            // whose slot came back empty — the item was deleted from the build, or the inventory
            // record is short — is left as it is rather than retried forever.
            if (pending.equippedSlot < 0 || Equipment.EquippedSlotIndex == pending.equippedSlot)
                hasPending = false;
        }

        private void Apply(in State state) =>
            Equipment.RestoreEquipment(state.equippedSlot, state.autoUseTimer,
                                       state.hasAimPoint, state.aimPoint);
    }
}
