using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Connects a spawned player object to the save record belonging to whoever is playing it.
    ///
    /// The server cannot work this out on its own. A profile id lives on the client's machine, and
    /// Netcode's connection payload — the usual way to send one — is only delivered when connection
    /// approval is enabled, which this project deliberately leaves off so the lobby and Relay flows
    /// stay as they are. So the owner reports its profile the moment its player object spawns, and
    /// the server answers by restoring that profile's state.
    ///
    /// The host is the common case and pays nothing for this: its profile is known locally before
    /// any spawn, so <c>NetworkGameManager</c> already spawned it at the saved position and this
    /// component only fills in inventory and health. A remote client spawns at the spawn point and
    /// is moved, which costs one visible hop and is the price of not touching connection approval.
    /// </summary>
    public class PlayerSaveSync : NetworkBehaviour
    {
        /// <summary>The profile this object is playing for, once known. Server-side only.</summary>
        public string BoundProfileId { get; private set; }

        public override void OnNetworkSpawn()
        {
            // The profile id alone. A display name would have to be truncated to fit
            // FixedString64Bytes — which throws rather than truncating on its own — and nothing on
            // the server reads one.
            if (IsOwner) ClaimProfileServerRpc(PlayerProfile.LocalId);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer || string.IsNullOrEmpty(BoundProfileId)) return;

            // Captures this player's final state into their record on the way out, so a save written
            // after they disconnect still has them where they left off.
            SaveManager.Instance?.Players?.Unbind(BoundProfileId);
            BoundProfileId = null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ClaimProfileServerRpc(FixedString64Bytes profileId)
        {
            string profile = profileId.ToString();
            if (string.IsNullOrEmpty(profile)) return;

            BoundProfileId = profile;

            SaveManager manager = SaveManager.Instance;
            if (manager == null || manager.Players == null)
            {
                Debug.LogWarning("[Save] A player claimed a profile with no SaveManager in the scene. " +
                                 "Nothing will be restored or saved for them.", this);
                return;
            }

            // The host was already placed at its saved position by NetworkGameManager, before the
            // chunks around it were loaded. Moving it again here would be at best redundant and at
            // worst a teleport into a chunk that has since streamed different ground underneath.
            bool applyPosition = !IsLocalPlayerOnHost();

            bool restored = manager.Players.Bind(profile, gameObject, applyPosition);

            if (restored) manager.NotifyLoadApplied();
        }

        /// <summary>True when this object is the host's own player, whose position was resolved before spawn.</summary>
        private bool IsLocalPlayerOnHost() =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsHost &&
            OwnerClientId == NetworkManager.Singleton.LocalClientId;
    }
}
