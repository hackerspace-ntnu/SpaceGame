using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Bounties
{
    /// <summary>
    /// Puts the crew's bounty on the wire: one server-written <see cref="BountyProgress"/> and one server-written list of
    /// claimed hunts' catalog places, which every peer adopts into its own <see cref="BountyBoard"/>, and one request the other way — "my
    /// player takes this bounty".
    ///
    /// <para>
    /// A <see cref="NetworkVariable{T}"/> and a <see cref="NetworkList{T}"/> for <c>ObjectiveNetwork</c>'s reason: a
    /// player joining mid-hunt reads where the crew are, and which posters to stamp, on spawn. On the NetworkGameManager
    /// prefab beside <c>ObjectiveNetwork</c>; the board it speaks for is plain scene state in <c>persistentScene</c>. It
    /// only carries the state; the board holds it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class BountyNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<BountyProgress> progress = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Built with the behaviour, never in OnNetworkSpawn: Netcode collects a behaviour's network variables before
        // spawn, and a list made later is never synchronised.
        private readonly NetworkList<int> claimed = new(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // A client's copy of the list to hand the board: AdoptClaimed refills its own list from what it is given.
        private readonly List<int> adopted = new();

        private BountyBoard board;

        public override void OnNetworkSpawn()
        {
            board = BountyBoard.Instance;
            if (board == null)
            {
                Debug.LogError("[Bounties] No BountyBoard in the scene, so the crew's bounty cannot be shared.", this);
                return;
            }

            board.TakeRequested += OnTakeRequested;

            if (IsServer)
            {
                board.Changed += Publish;
                board.ClaimedChanged += PublishClaimed;
                PublishClaimed(board.Claimed);
                Publish(live: true);
                return;
            }

            progress.OnValueChanged += OnReplicated;
            claimed.OnListChanged += OnClaimedReplicated;

            // Netcode hands a joiner the current values in the spawn payload and never replays a change event for them.
            AdoptClaimed();
            board.Adopt(progress.Value, live: false);
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            progress.OnValueChanged -= OnReplicated;
            claimed.OnListChanged -= OnClaimedReplicated;
            if (board == null) return;

            board.Changed -= Publish;
            board.ClaimedChanged -= PublishClaimed;
            board.TakeRequested -= OnTakeRequested;
        }

        // ── Server ───────────────────────────────────────────────────────────

        private void Publish(bool live)
        {
            if (IsServer && IsSpawned) progress.Value = board.Progress;
        }

        /// <summary>
        /// Writes the board's claimed hunts into the list. A hand-in appends one, so only the new tail is sent; a load
        /// that replaced the list rewrites it whole.
        /// </summary>
        private void PublishClaimed(IReadOnlyList<int> handedIn)
        {
            if (!IsServer || !IsSpawned) return;

            int kept = 0;
            while (kept < claimed.Count && kept < handedIn.Count && claimed[kept] == handedIn[kept]) kept++;

            if (kept < claimed.Count) claimed.Clear();
            for (int i = claimed.Count; i < handedIn.Count; i++) claimed.Add(handedIn[i]);
        }

        // ── Clients ──────────────────────────────────────────────────────────
        // The host never listens: it wrote these values from its own board, and adopting them back would announce its
        // own change a second time.

        private void OnReplicated(BountyProgress previous, BountyProgress current) => board.Adopt(current, live: true);

        private void OnClaimedReplicated(NetworkListEvent<int> change) => AdoptClaimed();

        private void AdoptClaimed()
        {
            adopted.Clear();
            foreach (int place in claimed) adopted.Add(place);
            board.AdoptClaimed(adopted);
        }

        private void OnTakeRequested(int index)
        {
            if (IsSpawned && !IsServer) TakeRpc(index);
        }

        /// <summary>A client's player takes a bounty. The player is the sender, never an argument.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TakeRpc(int index, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null) return;

            board.Take(index, client.PlayerObject.transform);
        }
    }
}
