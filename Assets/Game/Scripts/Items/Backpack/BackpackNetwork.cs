// What is in one player's pack, on the wire.
//
// This is the one part of the backpack that could NOT ride the NetMessaging channel, and the reason
// is the same one that kept the chat system off it: NetArg has four numbers, a point and a rotation,
// and no string. An item is identified by InventoryItem.ID, which is its asset GUID — so "slot 7 now
// holds a water cell" is not expressible as a message, and neither is the item that goes BACK into
// the pack when a full hotbar swaps with it.
//
// A NetworkList is also the only shape that answers the late joiner. Messages announce changes; a
// player who arrives an hour in has missed every one of them, and would build their copy of the pack
// out of its prefab's starting contents and then never hear that eleven things had been taken out of
// it. A list is state, and NGO hands a joiner the whole of it with the spawn.
//
// Where the pack IS — shouldered, flying, open on the sand — is not here. That is a small enumerated
// state with no strings in it, so it rides NetMsg.PackState on this same player's channel, in
// BackpackController.
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Replicates the contents of the pack belonging to the player this sits on.
    ///
    /// <para>
    /// Server-authoritative in the strictest sense: the server's <see cref="BackpackContainer"/> is
    /// the truth, every other machine's is a mirror rebuilt from this list, and no client ever
    /// writes to either. That is what makes two players reaching into the same pack safe — both
    /// requests land on one machine, in order, and the second finds the slot already empty.
    /// </para>
    /// <para>
    /// Add it beside <see cref="BackpackController"/> on <c>PlayerCharacterNetworked</c>, which is a
    /// prefab VARIANT of <c>PlayerCharacter</c> — the controller is inherited onto the very root
    /// object the NetworkObject is added to, so the two do share a GameObject. It must go on the
    /// variant and not on the base: the base is the offline player, has no NetworkObject, and a
    /// NetworkBehaviour without one is an error in Netcode.
    /// </para>
    /// <para>
    /// Without it the pack still works, single-player-style, with each machine running its own copy
    /// of the contents — which is exactly what the whole game did before it had netcode, and is the
    /// degradation this project prefers to a hard failure. It is also what it has been doing:
    /// nothing in the project referenced this class, so a client's pack UI showed its own idea of
    /// the contents while the server's — the one that gets saved — said something else.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BackpackController))]
    public class BackpackNetwork : NetworkBehaviour
    {
        /// <summary>
        /// Both compartments end to end: straps first, then the main compartment. One list rather
        /// than two because a NetworkList is a native allocation with a lifetime to manage, and the
        /// index arithmetic below is cheaper than a second one of those.
        /// </summary>
        private NetworkList<FixedString64Bytes> contents = new();

        private BackpackController controller;
        private BackpackContainer bound;

        private static int WireLength => BackpackContainer.StrapSlots + BackpackContainer.MainSlots;

        private static int WireIndex(BackpackCompartment compartment, int slot) =>
            compartment == BackpackCompartment.Strap ? slot : BackpackContainer.StrapSlots + slot;

        private static bool FromWireIndex(int wire, out BackpackCompartment compartment, out int slot)
        {
            compartment = BackpackCompartment.Strap;
            slot = wire;

            if (wire < 0 || wire >= WireLength) return false;

            if (wire >= BackpackContainer.StrapSlots)
            {
                compartment = BackpackCompartment.Main;
                slot = wire - BackpackContainer.StrapSlots;
            }

            return true;
        }

        /// <summary>
        /// The controller this replicates, resolved on demand rather than cached in Awake — Awake
        /// order between two components on one object is not something Unity promises, and every
        /// read of this happens from <see cref="OnNetworkSpawn"/> or later anyway.
        /// </summary>
        private BackpackController Controller =>
            controller != null ? controller : controller = GetComponent<BackpackController>();

        public override void OnNetworkSpawn()
        {
            BackpackController owner = Controller;

            BackpackContainer container = owner != null && owner.Pack != null
                ? owner.Pack.Container
                : null;

            if (container == null)
            {
                // The pack is built in BackpackController.Awake, which has already run by the time
                // Netcode spawns us — so a null here means either there is no controller on this
                // object at all, or the controller bailed (no socket, no prefab) and has already
                // said why. Nothing to replicate.
                Debug.LogWarning($"[Backpack] No pack to replicate on '{name}' ({(owner == null ? "no BackpackController on this object" : "the controller built no pack")}); " +
                                 "its contents will not agree between machines.", this);
                return;
            }

            bound = container;
            contents.OnListChanged += OnWireChanged;

            if (IsServer)
            {
                PublishAll();

                // The server pushes; it never listens. Every write to the container — a take, a
                // world pickup overflowing into it, a swap putting something back — goes through
                // this one event, so nothing has to remember to replicate itself.
                bound.OnSlotChanged += OnContainerChanged;
                return;
            }

            AdoptCurrentState();
        }

        public override void OnNetworkDespawn()
        {
            Unbind();
        }

        public override void OnDestroy()
        {
            Unbind();

            // NetworkBehaviour.OnDestroy disposes this behaviour's NetworkVariables — `contents`
            // among them, a NetworkList backed by a native container — and deregisters it from its
            // NetworkObject. Without this the native allocation leaks on every despawn.
            base.OnDestroy();
        }

        private void Unbind()
        {
            contents.OnListChanged -= OnWireChanged;

            if (bound != null) bound.OnSlotChanged -= OnContainerChanged;
            bound = null;
        }

        // ── Server → wire ────────────────────────────────────────────────────────

        /// <summary>
        /// Fill the list from the container as it stands, once, at spawn.
        ///
        /// The pack has already been built and stocked from its prefab's starting contents by the
        /// time this runs, and on a session that has been going a while it may have been emptied
        /// and refilled long before this player's body existed. Either way the list is written from
        /// what is actually in it, never assumed.
        /// </summary>
        private void PublishAll()
        {
            contents.Clear();

            for (int i = 0; i < WireLength; i++)
            {
                FromWireIndex(i, out BackpackCompartment compartment, out int slot);
                contents.Add(IdOf(bound.GetSlot(compartment, slot)));
            }
        }

        private void OnContainerChanged(BackpackCompartment compartment, int index, InventorySlot slot)
        {
            if (!IsServer) return;

            int wire = WireIndex(compartment, index);
            if (wire < 0 || wire >= contents.Count) return;

            FixedString64Bytes id = IdOf(slot);
            if (contents[wire].Equals(id)) return;   // nothing changed; do not wake every client

            contents[wire] = id;
        }

        /// <summary>
        /// The wire form of one slot.
        ///
        /// <para>
        /// The empty case is typed <c>default(FixedString64Bytes)</c> rather than written as
        /// <c>slot.IsEmpty ? default : slot.Item.ID</c>. Both arms of that ternary are strings, so
        /// C# types the whole expression as string and converts the RESULT — meaning an empty slot
        /// converts <c>default(string)</c>, i.e. null, and FixedString64Bytes throws an NRE on null
        /// from inside Unity.Collections. It took the entire inventory restore down once already.
        /// </para>
        /// </summary>
        private static FixedString64Bytes IdOf(InventorySlot slot)
        {
            if (slot == null || slot.IsEmpty || string.IsNullOrEmpty(slot.Item.ID))
                return default(FixedString64Bytes);

            return new FixedString64Bytes(slot.Item.ID);
        }

        // ── Wire → every other machine ───────────────────────────────────────────

        private void OnWireChanged(NetworkListEvent<FixedString64Bytes> change)
        {
            // The server's own echo of the write it just made. Applying it back would raise
            // OnSlotChanged, which would write to the list again — the loop that a re-entrancy flag
            // would otherwise have to guard. The server's container is already the truth.
            if (IsServer) return;

            // Clear and Add arrive as events with no meaningful index (a full rebuild), so anything
            // that is not a single-element change is answered by re-reading the whole list.
            if (change.Type != NetworkListEvent<FixedString64Bytes>.EventType.Value)
            {
                AdoptCurrentState();
                return;
            }

            ApplyToContainer(change.Index, change.Value);
        }

        /// <summary>
        /// Take up the pack as it already stands, for a machine that was not here when it was
        /// filled.
        ///
        /// <para>
        /// <see cref="NetworkList{T}.OnListChanged"/> only fires on CHANGE. A player object that
        /// spawns on a client mid-session arrives with the current values already in the list and
        /// no events coming, so without this their copy of the pack shows whatever the prefab's
        /// starting contents were — items that may have been taken out of it an hour ago, and which
        /// they would be able to reach for and be refused.
        /// </para>
        /// </summary>
        private void AdoptCurrentState()
        {
            for (int i = 0; i < contents.Count; i++)
                ApplyToContainer(i, contents[i]);
        }

        private void ApplyToContainer(int wire, FixedString64Bytes id)
        {
            if (bound == null) return;
            if (!FromWireIndex(wire, out BackpackCompartment compartment, out int slot)) return;

            string itemId = id.Value;
            InventoryItem item = string.IsNullOrEmpty(itemId)
                ? null
                : Registry<InventoryItem>.Get(itemId);

            // RestoreSlot, not TryPlaceAt or TryAddItem: this is a positional assignment of a known
            // state, and it has to be able to write a null into an occupied slot (something was
            // taken out) and an item into one that is not empty (something was swapped in). It is
            // also the only one of the three that raises OnSlotChanged, which is what rebuilds the
            // display object hanging in that socket.
            bound.Get(compartment).RestoreSlot(slot, item);
        }
    }
}
