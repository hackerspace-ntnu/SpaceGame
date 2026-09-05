// The world half of a placeable: the thing standing on the ground, and the way back.
//
// It answers Q rather than E so that a placeable which DOES something keeps E for doing it. A
// placed lamp switches on with E and is pocketed with Q; neither has to give way to the other.
//
// What it returns is authored on the prefab, not sent at placement time. A placeable is a PAIR --
// one held prefab, one placed prefab -- so the placed half already knows what it is, and nothing
// about that has to survive the wire, a save, or a client joining halfway through. The alternative,
// binding it at spawn, would need the asset id replicated and re-applied on every load.
//
// Retrieval is server-authoritative for the obvious reason: two players pressing Q on the same
// crate on the same frame must not produce two crates. The server gives, then despawns.
using UnityEngine;
using Unity.Netcode;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// Makes a placed object pickable back up into the inventory of whoever asks.
    /// Add it to the prefab a <see cref="PlaceableItem"/> spawns.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedObject : MonoBehaviour, IInteractable, IRetrievable, IInteractionReadout
    {
        [Tooltip("What goes back into the inventory when this is picked up. This is what makes " +
                 "the loop conserve — it must be the same asset as the item that places it, or " +
                 "putting a thing down and picking it up transmutes it.")]
        [SerializeField] private InventoryItem returnItem;

        [Tooltip("What this is called in the prompt. Empty uses the item's own name.")]
        [SerializeField] private string displayName = "";

        [Tooltip("Off for something placed permanently — a claim marker, a built wall. Keeps the " +
                 "prompt honest rather than offering a verb that then refuses.")]
        [SerializeField] private bool retrievable = true;

        /// <summary>What this returns as, or null if it can never be picked up.</summary>
        public InventoryItem ReturnItem => returnItem;

        // ── E: nothing, unless a subclass gives it something ─────────────────

        /// <summary>
        /// False by default: a plain placeable has no primary verb, only Q. Overriding this and
        /// <see cref="Interact"/> is how a placeable that DOES something gets its E back.
        /// </summary>
        public virtual bool CanInteract() => false;

        public virtual void Interact(Interactor interactor) { }

        // ── Q: take it back ──────────────────────────────────────────────────

        public bool CanRetrieve() => retrievable && returnItem != null;

        public void Retrieve(Interactor interactor)
        {
            if (!CanRetrieve() || interactor == null) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.RetrieveRequest,
                                   new NetArg().With(interactor.gameObject),
                                   NetTo.Server);
        }

        private void OnEnable() => this.NetOn(NetMsg.RetrieveRequest, OnRetrieveRequested);

        private void OnDisable() => this.NetOff(NetMsg.RetrieveRequest, OnRetrieveRequested);

        /// <summary>
        /// Server. Give first, despawn second — the other order destroys the crate and only then
        /// discovers the player had nowhere to put it, and the crate is gone either way.
        /// </summary>
        private void OnRetrieveRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Owns(this)) return;
            if (!CanRetrieve()) return;

            GameObject actor = arg.Resolve();
            if (actor == null) return;

            var inventory = actor.GetComponentInChildren<PlayerInventoryComponent>();
            if (inventory == null || !inventory.TryAddItem(returnItem)) return;

            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned) netObj.Despawn();
            else Destroy(gameObject);
        }

        // ── what the crosshair says ──────────────────────────────────────────

        public string Label =>
            !string.IsNullOrEmpty(displayName) ? displayName
            : returnItem != null ? returnItem.itemName
            : "Placed item";

        public string Prompt => CanRetrieve() ? "Q: pick up" : string.Empty;

        /// <summary>Null: a thing standing on the ground has no position to draw a bar for.</summary>
        public float? Value01 => null;

        public string ValueText => string.Empty;
    }
}
