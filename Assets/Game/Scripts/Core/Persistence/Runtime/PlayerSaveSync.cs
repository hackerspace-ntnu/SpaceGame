using System;
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
    ///
    /// <para>
    /// <b>A claim is a request, not a fact.</b> The claim RPC is invokable by anyone — it has to be,
    /// since the owner is not the server — so everything it says is checked before it is believed.
    /// An unchecked claim hands the caller the host's position, hotbar, backpack and health, and it
    /// does worse than that on the way out: see <see cref="OnNetworkDespawn"/>.
    /// </para>
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

        /// <summary>
        /// Captures this player's final state into their record on the way out, so a save written
        /// after they disconnect still has them where they left off.
        ///
        /// <para>
        /// <b>Only if the profile is still ours.</b> If a second object took this profile over — the
        /// same person reconnecting, or a claim this object lost a race to — then the live player
        /// speaking for it is somebody else, and <c>Unbind</c> would capture THEIR state into the
        /// record and then drop them from the bound-player table. They would keep playing while
        /// nothing captured them again, and every <c>SaveRef</c> naming them (a mount's rider, an
        /// NPC's target) would stop resolving, so mounts would save as riderless. Unbinding a
        /// profile is only ever this object's business while this object is the one bound to it.
        /// </para>
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (!IsServer || string.IsNullOrEmpty(BoundProfileId)) return;

            PlayerSaveService players = SaveManager.Instance?.Players;

            if (players != null &&
                players.TryGetBoundPlayer(BoundProfileId, out GameObject live) &&
                live != gameObject)
            {
                Debug.LogWarning($"[Save] '{name}' is leaving with profile '{BoundProfileId}', which " +
                                 $"'{live.name}' now holds. Leaving that binding alone — unbinding it " +
                                 "here would capture the wrong player's state into the record and " +
                                 "silently stop capturing them at all.", this);

                BoundProfileId = null;
                return;
            }

            players?.Unbind(BoundProfileId);
            BoundProfileId = null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ClaimProfileServerRpc(FixedString64Bytes profileId, RpcParams rpcParams = default)
        {
            string profile = profileId.ToString();
            ulong sender = rpcParams.Receive.SenderClientId;

            // 1. The sender must be claiming for its OWN body. The RPC is broadcast on this
            //    object's channel and anyone may invoke it, so without this a client can claim on
            //    the host's player object and be handed the host's saved state.
            if (sender != OwnerClientId)
            {
                Debug.LogWarning($"[Save] Client {sender} tried to claim a profile on '{name}', which " +
                                 $"belongs to client {OwnerClientId}. Ignored.", this);
                return;
            }

            // 2. The id must look like an id. PlayerProfile.LocalId is always a GUID, so anything
            //    else is either a corrupted PlayerPrefs entry or a hand-built packet; either way it
            //    would become a key in the save file that nothing ever matches again.
            if (!IsWellFormed(profile))
            {
                Debug.LogWarning($"[Save] Client {sender} claimed a malformed profile id " +
                                 $"('{profile}'). Ignored; nothing will be saved for them.", this);
                return;
            }

            // 3. Nobody else may already be playing it. Two live objects on one profile is the
            //    collision PlayerProfile's own docs describe: the second overwrites the first, both
            //    restore the same inventory, and only one is ever captured back out.
            SaveManager manager = SaveManager.Instance;
            PlayerSaveService players = manager?.Players;

            if (players != null &&
                players.TryGetBoundPlayer(profile, out GameObject existing) &&
                existing != gameObject)
            {
                Debug.LogError($"[Save] Client {sender} claimed profile '{profile}', which " +
                               $"'{existing.name}' is already playing. Refused — two bodies on one " +
                               "profile means only one of them is ever saved. Two copies of the game " +
                               "on one machine need distinct instance names; see PlayerProfile.", this);
                return;
            }

            // 4. And this body may not change its mind. Rebinding an object from one profile to
            //    another abandons the first record's live reference with nothing to unbind it.
            if (!string.IsNullOrEmpty(BoundProfileId) && BoundProfileId != profile)
            {
                Debug.LogWarning($"[Save] '{name}' is already bound to profile '{BoundProfileId}' and " +
                                 $"cannot be rebound to '{profile}'. Ignored.", this);
                return;
            }

            BoundProfileId = profile;

            if (manager == null || players == null)
            {
                Debug.LogWarning("[Save] A player claimed a profile with no SaveManager in the scene. " +
                                 "Nothing will be restored or saved for them.", this);
                return;
            }

            // The host was already placed at its saved position by NetworkGameManager, before the
            // chunks around it were loaded. Moving it again here would be at best redundant and at
            // worst a teleport into a chunk that has since streamed different ground underneath.
            bool applyPosition = !IsLocalPlayerOnHost();

            bool restored = players.Bind(profile, gameObject, applyPosition);

            if (restored) manager.NotifyLoadApplied();
        }

        /// <summary>
        /// Whether a claimed id is one <see cref="PlayerProfile"/> could have produced.
        ///
        /// Public so the rule can be exercised from an EditMode test without a session, and static
        /// so the test does not need a NetworkBehaviour to exist.
        /// </summary>
        public static bool IsWellFormed(string profileId) =>
            !string.IsNullOrEmpty(profileId) && Guid.TryParse(profileId, out _);

        /// <summary>True when this object is the host's own player, whose position was resolved before spawn.</summary>
        private bool IsLocalPlayerOnHost() =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsHost &&
            OwnerClientId == NetworkManager.Singleton.LocalClientId;
    }
}
