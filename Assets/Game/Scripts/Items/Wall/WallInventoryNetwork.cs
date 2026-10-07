using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Replicates what is on one inventory wall, and where.
    ///
    /// <para>
    /// Server-authoritative in the strictest sense: the server's <see cref="PackLayout"/> is the
    /// truth, every other machine's is a mirror rebuilt from this list, and no client ever writes
    /// to either. That is what makes two players standing at one wall safe — both requests land on
    /// one machine, in order, and the second finds the space already taken.
    /// </para>
    /// <para>
    /// A <c>NetworkList</c> and not messages, for the two reasons <c>BackpackNetwork</c> gives.
    /// <see cref="NetArg"/> has four numbers, a point and a rotation, and no string — and an item
    /// is identified by its asset GUID, so "a charge cell is now at (1.71, 0.63) on the wall" is
    /// not expressible as a message. And a list is what answers the LATE JOINER: messages announce
    /// changes, and a player who arrives an hour in has missed every one of them.
    /// </para>
    /// <para>
    /// Goes on the same GameObject as the <see cref="WallInventory"/>, which on the ship is a child
    /// of the hull's <c>NetworkObject</c>. A <c>NetworkBehaviour</c> under a spawned
    /// <c>NetworkObject</c> replicates as part of it; it does not need one of its own, and adding
    /// one would make the wall a separate spawned entity that had to be kept parented by hand.
    /// </para>
    /// <para>
    /// Without it the wall still works, single-player-style, with each machine running its own copy
    /// of the contents — the degradation this project prefers to a hard failure.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WallInventory))]
    public class WallInventoryNetwork : NetworkBehaviour
    {
        /// <summary>
        /// Every placement on the wall, in no particular order — a placement names its own surface
        /// and position, so the index means nothing.
        /// </summary>
        private readonly NetworkList<PackPlacementWire> contents = new();

        private WallInventory bound;

        /// Reused by the change check below, so a layout event does not allocate.
        private readonly List<PackPlacement> scratch = new();

        public override void OnNetworkSpawn()
        {
            bound = GetComponent<WallInventory>();

            if (bound == null)
            {
                Debug.LogWarning($"[Wall] No WallInventory on '{name}'; its contents will not " +
                                 "agree between machines.", this);
                return;
            }

            contents.OnListChanged += OnWireChanged;

            if (IsServer)
            {
                PublishAll();

                // The server pushes; it never listens. Every write to the layout — a take, a stow,
                // a save being restored — goes through this one event, so nothing has to remember
                // to replicate itself.
                bound.Layout.OnChanged += OnLayoutChanged;
                return;
            }

            AdoptCurrentState();
        }

        public override void OnNetworkDespawn() => Unbind();

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

            if (bound != null) bound.Layout.OnChanged -= OnLayoutChanged;

            bound = null;
        }

        // ── Server → the wire ────────────────────────────────────────────────

        /// <summary>
        /// A whole-list rewrite rather than a diff. Free placement makes a diff genuinely hard —
        /// one move changes a placement's surface, uv and yaw at once, and a swap removes one item
        /// and adds another in the same call — while a wall holds tens of items, so the list is a
        /// couple of kilobytes at worst. Correctness is worth more than the delta here.
        /// </summary>
        private void PublishAll()
        {
            if (bound == null) return;

            contents.Clear();

            foreach (PackPlacement placement in bound.Layout.Placements)
                contents.Add(ToWire(placement));
        }

        private void OnLayoutChanged()
        {
            if (!IsServer || bound == null) return;

            // Nothing actually different means nothing sent. The layout raises one event per
            // placement during a bulk rebuild, and without this each of them would clear and refill
            // the list on every client.
            if (MatchesWire()) return;

            PublishAll();
        }

        private bool MatchesWire()
        {
            scratch.Clear();
            scratch.AddRange(bound.Layout.Placements);

            if (scratch.Count != contents.Count) return false;

            for (int i = 0; i < scratch.Count; i++)
                if (!contents[i].Equals(ToWire(scratch[i]))) return false;

            return true;
        }

        /// <summary>
        /// The wire form of one placement.
        ///
        /// The empty case is typed <c>default(FixedString64Bytes)</c> rather than written as
        /// <c>empty ? default : placement.ItemId</c>. Both arms of that ternary would be strings, so
        /// C# types the whole expression as string and converts the RESULT — meaning an empty id
        /// converts <c>default(string)</c>, i.e. null, and FixedString64Bytes throws an NRE on null
        /// from inside Unity.Collections. It took the entire inventory restore down once already.
        /// </summary>
        private static PackPlacementWire ToWire(PackPlacement placement) => new()
        {
            ItemId = string.IsNullOrEmpty(placement.ItemId)
                ? default(FixedString64Bytes)
                : new FixedString64Bytes(placement.ItemId),
            Surface = (byte)placement.Surface,
            U = placement.Uv.x,
            V = placement.Uv.y,
            Yaw = placement.Yaw,
            Charge = SupplyCharge.ToByte(placement.Charge),
        };

        // ── Wire → every other machine ───────────────────────────────────────

        private void OnWireChanged(NetworkListEvent<PackPlacementWire> change)
        {
            // The server's own echo of the write it just made. Applying it back would raise
            // OnChanged, which would write to the list again — the loop that a re-entrancy flag
            // would otherwise have to guard. The server's layout is already the truth.
            if (IsServer) return;

            // Always the whole list, whatever the event says. An index into this list identifies
            // nothing on its own — a placement carries its own surface and position — so there is
            // no such thing as applying element N in isolation.
            AdoptCurrentState();
        }

        /// <summary>
        /// Take up the wall as it already stands, for a machine that was not here when it was
        /// filled.
        ///
        /// <para>
        /// <see cref="NetworkList{T}.OnListChanged"/> only fires on CHANGE. A ship that spawns on a
        /// client mid-session arrives with the current values already in the list and no events
        /// coming, so without this their copy of the wall shows whatever the prefab's starting
        /// contents were — items that may have been taken off it an hour ago, and which they would
        /// be able to reach for and be refused.
        /// </para>
        /// <para>
        /// Every item is measured on the way in, by <see cref="PackContainer.AdoptPlacements"/>.
        /// The wire carries no footprint — it is derived from the prefab, so sending it would be a
        /// second copy of a number that must never disagree — and the layout will not accept a
        /// placement without one.
        /// </para>
        /// </summary>
        private void AdoptCurrentState()
        {
            if (bound == null) return;

            bound.AdoptPlacements(FromWire());
        }

        private IEnumerable<PackPlacement> FromWire()
        {
            for (int i = 0; i < contents.Count; i++)
            {
                PackPlacementWire wire = contents[i];

                string itemId = wire.ItemId.Value;
                if (string.IsNullOrEmpty(itemId)) continue;

                // Only an item that CARRIES a charge gets one back. The byte is 0 both for an
                // empty tank and for a rifle, and only the item can tell the two apart.
                InventoryItem asset = bound != null ? bound.ItemFor(itemId) : null;
                float charge = SupplyCharge.Carries(asset)
                    ? SupplyCharge.FromByte(wire.Charge)
                    : SupplyCharge.None;

                yield return new PackPlacement(
                    itemId, (PackSurfaceId)wire.Surface, new Vector2(wire.U, wire.V), wire.Yaw,
                    charge);
            }
        }
    }
}
