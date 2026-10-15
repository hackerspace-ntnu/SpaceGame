using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Puts the crew's place in every objective chain on the wire: one server-written list that
    /// every peer adopts into its own <see cref="ObjectiveDirector"/>, and the reports the other way
    /// — "my player has done their part of this step", "my player finished talking to this
    /// character", "my player accepted this quest" (see <see cref="QuestRequests"/>).
    ///
    /// <para>
    /// A <see cref="NetworkList{T}"/> rather than a message, for the reason the module rack
    /// replicates a mask: a message answers whoever was listening, and a player joining halfway
    /// through the salvage was not. The value is simply true when they arrive. Entry <c>i</c> is the
    /// director's run <c>i</c>, the main chain first — the same order on every machine, because it is
    /// the order of the director's serialized chains.
    /// </para>
    /// <para>
    /// On the NetworkGameManager prefab in <c>persistentScene</c>, beside <c>ChatNetwork</c> and for
    /// its reasons: it already carries a NetworkObject, it spawns on every peer before any player
    /// does, and the director it speaks for is plain scene state with no NetworkObject of its own.
    /// It is not a second source of truth — the director holds the progress; this only carries it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class ObjectiveNetwork : NetworkBehaviour
    {
        // Netcode requires a NetworkList to exist before spawn, so it is built here, not in Awake.
        private readonly NetworkList<ObjectiveProgress> progress = new(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private ObjectiveDirector director;

        /// <summary>The spawned instance on this machine, or null outside a session.</summary>
        public static ObjectiveNetwork Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        public override void OnNetworkSpawn()
        {
            Active = this;

            director = ObjectiveDirector.Instance;
            if (director == null)
            {
                Debug.LogError("[Objectives] No ObjectiveDirector in the scene, so the crew's " +
                               "objectives cannot be shared.", this);
                return;
            }

            progress.OnListChanged += OnReplicated;
            director.LocalPlayerFinished += OnLocalPlayerFinished;

            if (IsServer)
            {
                director.Changed += Publish;
                foreach (ObjectiveRun run in director.Runs) Publish(run, live: true);
                return;
            }

            // The read-on-spawn. Netcode hands a joiner the current list in the spawn payload and
            // never replays OnListChanged for it, so this is the only moment a late joiner learns
            // where the crew are — and it is not "live": they missed the briefings, by definition.
            for (int i = 0; i < progress.Count; i++)
                director.Adopt(i, progress[i], live: false);
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            if (Active == this) Active = null;

            progress.OnListChanged -= OnReplicated;
            if (director == null) return;

            director.Changed -= Publish;
            director.LocalPlayerFinished -= OnLocalPlayerFinished;
        }

        private void Publish(ObjectiveRun run, bool live)
        {
            if (!IsServer || !IsSpawned) return;

            while (progress.Count <= run.Index) progress.Add(default);
            progress[run.Index] = run.Progress;
        }

        private void OnReplicated(NetworkListEvent<ObjectiveProgress> change)
        {
            // The host wrote it; adopting it back would announce its own change a second time.
            if (IsServer) return;

            if (change.Type is NetworkListEvent<ObjectiveProgress>.EventType.Value
                            or NetworkListEvent<ObjectiveProgress>.EventType.Add
                            or NetworkListEvent<ObjectiveProgress>.EventType.Insert)
                director.Adopt(change.Index, change.Value, live: true);
        }

        private void OnLocalPlayerFinished(ObjectiveRun run, int step)
        {
            if (!IsSpawned) return;

            if (IsServer) director.RecordFinished(NetworkManager.LocalClientId, run.Index, step);
            else ReportFinishedRpc(run.Index, step);
        }

        /// <summary>
        /// A client saying its own player has done their part of run <paramref name="runIndex"/>'s
        /// step <paramref name="step"/>. The player is the sender, never an argument, so no client can
        /// finish a step for somebody else.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportFinishedRpc(int runIndex, int step, RpcParams rpcParams = default) =>
            director.RecordFinished(rpcParams.Receive.SenderClientId, runIndex, step);

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
