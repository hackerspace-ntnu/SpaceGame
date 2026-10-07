// What is on one player's pack, and where, on the wire.
//
// This is the one part of the backpack that could NOT ride the NetMessaging channel, and the reason
// is the same one that kept the chat system off it: NetArg has four numbers, a point and a rotation,
// and no string. An item is identified by InventoryItem.ID, which is its asset GUID — so "a water
// cell is now lying at (0.31, 0.44) on the left wing" is not expressible as a message, and neither
// is the item that goes BACK onto the pack when a full hotbar swaps with it.
//
// A NetworkList is also the only shape that answers the late joiner. Messages announce changes; a
// player who arrives an hour in has missed every one of them, and would build their copy of the pack
// out of its prefab's starting contents and then never hear that eleven things had been taken off
// it. A list is state, and NGO hands a joiner the whole of it with the spawn.
//
// Where the pack IS — shouldered, flying, open on the sand — is not here. That is a small enumerated
// state with no strings in it, so it rides NetMsg.PackState on this same player's channel, in
// BackpackController.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One placed item, in the only form a <c>NetworkList</c> will carry: unmanaged, fixed size,
    /// comparable.
    ///
    /// <para>
    /// Deliberately not <see cref="PackPlacement"/>. That one holds a <c>string</c> item id, which
    /// makes it a managed struct — a <c>NetworkList</c> of it does not compile. The two are
    /// converted at this boundary and nowhere else.
    /// </para>
    /// <para>
    /// The footprint is absent on purpose. It is derived from the item's prefab, so every machine
    /// can measure it for itself, and sending it would create a second copy of a number that must
    /// never disagree.
    /// </para>
    /// </summary>
    public struct PackPlacementWire : INetworkSerializable, IEquatable<PackPlacementWire>
    {
        public FixedString64Bytes ItemId;

        /// <summary><see cref="PackSurfaceId"/>. A byte because the enum is one, and because these
        /// values are persisted and must never be renumbered.</summary>
        public byte Surface;

        /// Metres from the surface's (0,0) corner, and degrees. Two floats rather than a half2:
        /// four bytes per item is not worth a dependency on Unity.Mathematics here.
        public float U;
        public float V;
        public float Yaw;

        /// <summary>
        /// How full the item is, quantised by <see cref="SupplyCharge.ToByte"/>. Zero for the great
        /// majority of items, which hold nothing -- the receiving end knows from the item id whether
        /// the number means anything.
        /// </summary>
        public byte Charge;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Surface);
            serializer.SerializeValue(ref U);
            serializer.SerializeValue(ref V);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Charge);
        }

        /// <summary>
        /// Required by <c>NetworkList</c>, which uses it to decide whether a write is a change
        /// worth telling anyone about.
        /// </summary>
        public bool Equals(PackPlacementWire other) =>
            ItemId.Equals(other.ItemId)
            && Surface == other.Surface
            && U.Equals(other.U)
            && V.Equals(other.V)
            && Yaw.Equals(other.Yaw)
            && Charge == other.Charge;

        public override bool Equals(object obj) => obj is PackPlacementWire other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(ItemId.GetHashCode(), Surface, U, V, Yaw, Charge);
    }

    /// <summary>
    /// Replicates the contents of the pack belonging to the player this sits on.
    ///
    /// <para>
    /// Server-authoritative in the strictest sense: the server's <see cref="PackLayout"/> is the
    /// truth, every other machine's is a mirror rebuilt from this list, and no client ever writes
    /// to either. That is what makes two players reaching into the same pack safe — both requests
    /// land on one machine, in order, and the second finds the space already empty.
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
    /// degradation this project prefers to a hard failure.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BackpackController))]
    public class BackpackNetwork : NetworkBehaviour
    {
        /// <summary>
        /// Every placement on the pack, in no particular order — a placement names its own surface
        /// and position, so unlike the fixed-slot list this replaced, the index means nothing.
        /// </summary>
        private NetworkList<PackPlacementWire> contents = new();

        /// <summary>
        /// Is this pack's front leaf standing up as a rack?
        ///
        /// <para>
        /// A <c>NetworkVariable</c> beside the list rather than a message on <c>NetMsg</c>, for the
        /// reason the list itself gives one paragraph up: it is STATE, and a message only reaches
        /// the people who were listening when it was sent. A player who walks up to somebody's
        /// deployed pack an hour after they racked it has to see a rack, and only state answers
        /// that.
        /// </para>
        /// <para>
        /// <b>Owner-writable</b>, which is the one thing here worth arguing. The rack is not
        /// contested the way the contents are: it is reached from focus mode, focus mode refuses to
        /// open unless this machine owns the pack, and there is nothing to arbitrate — two players
        /// cannot take the same fold. Owner write also means no RPC and no request/announce pair
        /// for something the wearer is doing to their own kit. Contents stay strictly
        /// server-authoritative, and that difference is deliberate.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<bool> racked = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private BackpackController controller;
        private BackpackObject bound;

        /// Reused by the change check below, so a layout event does not allocate.
        private readonly List<PackPlacement> scratch = new();

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
            BackpackObject pack = owner != null ? owner.Pack : null;

            if (pack == null)
            {
                // The pack is built in BackpackController.Awake, which has already run by the time
                // Netcode spawns us — so a null here means either there is no controller on this
                // object at all, or the controller bailed (no socket, no prefab) and has already
                // said why. Nothing to replicate.
                Debug.LogWarning($"[Backpack] No pack to replicate on '{name}' ({(owner == null ? "no BackpackController on this object" : "the controller built no pack")}); " +
                                 "its contents will not agree between machines.", this);
                return;
            }

            bound = pack;
            contents.OnListChanged += OnWireChanged;

            // Both halves of the rack, and both are needed. The event carries every later change;
            // the apply below carries the ONE that has already happened, because a NetworkVariable
            // hands a joining client its current value with no event to go with it — the same trap
            // AdoptCurrentState exists for, in its smallest possible form.
            pack.RackRequested += OnRackAsked;
            racked.OnValueChanged += OnRackChanged;

            pack.SetRacked(racked.Value);

            if (IsServer)
            {
                PublishAll();

                // The server pushes; it never listens. Every write to the layout — a take, a world
                // pickup overflowing onto it, a swap putting something back, a drag — goes through
                // this one event, so nothing has to remember to replicate itself.
                bound.Layout.OnChanged += OnLayoutChanged;
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
            racked.OnValueChanged -= OnRackChanged;

            if (bound != null)
            {
                bound.Layout.OnChanged -= OnLayoutChanged;
                bound.RackRequested -= OnRackAsked;
            }

            bound = null;
        }

        // ── The rack ─────────────────────────────────────────────────────────────

        /// <summary>
        /// This player pressed the rack key. Write it; every machine hears the change, including
        /// this one, and moves its own copy of the leaf from <see cref="OnRackChanged"/>.
        ///
        /// <para>
        /// Nothing happens locally here, and not for the arbitration reason the take and the stow
        /// have — there is nothing to arbitrate — but so the fold has exactly one source. Moving
        /// the leaf here as well would make the presser's pack the one that is right by accident
        /// rather than by the same path as everybody else's.
        /// </para>
        /// <para>
        /// Refused when we are not the owner. Focus mode should already have made that impossible,
        /// and a write that Netcode would reject anyway is worth catching where it can be
        /// explained rather than where it becomes a warning in somebody's console.
        /// </para>
        /// </summary>
        private void OnRackAsked(bool up)
        {
            if (!IsSpawned || !IsOwner) return;

            racked.Value = up;
        }

        private void OnRackChanged(bool previous, bool current)
        {
            if (bound != null) bound.SetRacked(current);
        }

        // ── Server → wire ────────────────────────────────────────────────────────

        /// <summary>
        /// Fill the list from the layout as it stands.
        ///
        /// <para>
        /// A whole-list rewrite rather than a diff. Free placement makes a diff genuinely hard —
        /// one drag changes a placement's surface, uv and yaw at once, and a swap removes one item
        /// and adds another in the same call — while a pack holds a handful of items, so the list
        /// is a few hundred bytes. Correctness is worth more than the delta here.
        /// </para>
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
        /// <para>
        /// The empty case is typed <c>default(FixedString64Bytes)</c> rather than written as
        /// <c>empty ? default : placement.ItemId</c>. Both arms of that ternary would be strings, so
        /// C# types the whole expression as string and converts the RESULT — meaning an empty id
        /// converts <c>default(string)</c>, i.e. null, and FixedString64Bytes throws an NRE on null
        /// from inside Unity.Collections. It took the entire inventory restore down once already.
        /// </para>
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

        // ── Wire → every other machine ───────────────────────────────────────────

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
        /// Take up the pack as it already stands, for a machine that was not here when it was
        /// filled.
        ///
        /// <para>
        /// <see cref="NetworkList{T}.OnListChanged"/> only fires on CHANGE. A player object that
        /// spawns on a client mid-session arrives with the current values already in the list and
        /// no events coming, so without this their copy of the pack shows whatever the prefab's
        /// starting contents were — items that may have been taken off it an hour ago, and which
        /// they would be able to reach for and be refused.
        /// </para>
        /// <para>
        /// Every item is measured on the way in, by <see cref="BackpackObject.AdoptPlacements"/>.
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

                // A charge is only meaningful for an item that carries one. Reconstructed as
                // SupplyCharge.None otherwise, so a rifle off the wire does not come back claiming
                // to be an empty tank -- the byte is 0 for both, and only the item can tell them
                // apart.
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
