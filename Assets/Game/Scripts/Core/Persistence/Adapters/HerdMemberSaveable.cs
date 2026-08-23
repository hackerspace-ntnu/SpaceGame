using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists one herd member's place in the ring.
    ///
    /// A member's slot is derived from its index in the herd's registration list — the order Unity
    /// happened to enable the members in, which a reload does not reproduce. So without this the
    /// whole herd re-derives different slots on the first frame after a load and visibly rotates
    /// around its destination, every animal walking past the one next to it.
    ///
    /// The herd's shared phase and destination are not here: they belong to no member, so they live
    /// in <see cref="HerdStateSaveable"/>.
    /// </summary>
    [RequireComponent(typeof(HerdModule))]
    public class HerdMemberSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "herdMember";     // written into save files — NEVER rename

        private HerdModule herd;

        private HerdModule Herd => herd != null ? herd : herd = GetComponent<HerdModule>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>-1 until the member has been given a slot.</summary>
            public int slotIndex;

            /// <summary>The world position of that slot, already sampled onto the NavMesh.</summary>
            public Vector3 slotPosition;

            public bool slotAssigned;
        }

        public object CaptureState()
        {
            if (Herd == null) return null;

            return new State
            {
                slotIndex = Herd.SlotIndex,
                slotPosition = Herd.SlotPosition,
                slotAssigned = Herd.SlotAssigned,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Herd == null) return;

            if (state == null)
            {
                Herd.RestoreSlot(-1, Vector3.zero, false);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Herd.RestoreSlot(restored.slotIndex, restored.slotPosition, restored.slotAssigned);
        }
    }
}
