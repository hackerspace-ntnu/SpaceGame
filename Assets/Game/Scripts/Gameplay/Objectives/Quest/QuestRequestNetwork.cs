using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Carries a client's conversation to the server: "my player finished talking to this character" and "my
    /// player accepted the quest this character offered" (see <see cref="QuestRequests"/>). The quests themselves,
    /// and every other request about them, travel on <see cref="ObjectiveNetwork"/>.
    ///
    /// <para>
    /// On the NetworkGameManager prefab in <c>persistentScene</c>, beside <see cref="ObjectiveNetwork"/> and for its
    /// reasons: it spawns on every peer before any player does, and the characters it speaks for need no
    /// NetworkObject of their own.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class QuestRequestNetwork : NetworkBehaviour
    {
        /// <summary>The spawned instance on this machine, or null outside a session.</summary>
        public static QuestRequestNetwork Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        public override void OnNetworkSpawn() => Active = this;

        public override void OnNetworkDespawn()
        {
            if (Active == this) Active = null;
        }

        internal void SendTalked(string actorId) => ReportTalkedRpc(actorId);

        internal void SendAccepted(string actorId, string chainId) => RequestAcceptRpc(actorId, chainId);

        /// <summary>A client's player finished a conversation. Judged against the SENDER's body, never an argument.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportTalkedRpc(FixedString64Bytes actorId, RpcParams rpcParams = default) =>
            QuestRequests.ServerTalked(actorId.ToString(), SenderBody(rpcParams));

        /// <summary>A client's player accepted a quest. Judged against the SENDER's body, never an argument.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestAcceptRpc(FixedString64Bytes actorId, FixedString64Bytes chainId, RpcParams rpcParams = default) =>
            QuestRequests.ServerAccepted(actorId.ToString(), chainId.ToString(), SenderBody(rpcParams));

        private Transform SenderBody(RpcParams rpcParams) =>
            NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client)
            && client.PlayerObject != null
                ? client.PlayerObject.transform
                : null;
    }
}
