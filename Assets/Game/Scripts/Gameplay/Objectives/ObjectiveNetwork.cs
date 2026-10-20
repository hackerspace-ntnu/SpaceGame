using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Puts the crew's quests on the wire: one server-written list, one entry per begun quest, that every peer
    /// adopts into its own <see cref="ObjectiveDirector"/>; and two requests the other way — "my player has done
    /// their part of this step" and "give us this quest".
    ///
    /// <para>
    /// A <see cref="NetworkList{T}"/> rather than a message, for the reason the module rack
    /// replicates a mask: a message answers whoever was listening, and a player joining halfway
    /// through the salvage was not. The list is simply true when they arrive.
    /// </para>
    /// <para>
    /// On the NetworkGameManager prefab in <c>persistentScene</c>, beside <c>ChatNetwork</c> and for
    /// its reasons: it already carries a NetworkObject, it spawns on every peer before any player
    /// does, and the director it speaks for is plain scene state with no NetworkObject of its own.
    /// It is not a second source of truth — the director holds the quests; this only carries them.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class ObjectiveNetwork : NetworkBehaviour
    {
        private readonly NetworkList<QuestState> quests = new(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly List<QuestState> received = new();

        private ObjectiveDirector director;

        public override void OnNetworkSpawn()
        {
            director = ObjectiveDirector.Instance;
            if (director == null)
            {
                Debug.LogError("[Objectives] No ObjectiveDirector in the scene, so the crew's " +
                               "objectives cannot be shared.", this);
                return;
            }

            quests.OnListChanged += OnReplicated;
            director.LocalPlayerFinished += OnLocalPlayerFinished;
            director.GiveRequested += OnGiveRequested;

            if (IsServer)
            {
                director.Changed += Publish;
                director.Started += Publish;
                Publish();
                return;
            }

            // The read-on-spawn. Netcode hands a joiner the current list in the spawn payload and
            // never raises OnListChanged for it, so this is the only moment a late joiner learns
            // where the crew are — and it is not "live": they missed the briefings, by definition.
            Adopt(live: false);
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        public override void OnDestroy()
        {
            Unsubscribe();

            // Disposes the NetworkList's native storage.
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            quests.OnListChanged -= OnReplicated;
            if (director == null) return;

            director.Changed -= Publish;
            director.Started -= Publish;
            director.LocalPlayerFinished -= OnLocalPlayerFinished;
            director.GiveRequested -= OnGiveRequested;
        }

        private void Publish(Quest quest, bool live) => Publish();

        /// <summary>SERVER: makes the list match the director's quests, writing only the entries that differ.</summary>
        private void Publish()
        {
            if (!IsServer || !IsSpawned) return;

            IReadOnlyList<Quest> begun = director.Quests;
            for (int i = 0; i < begun.Count; i++)
            {
                QuestState state = QuestState.Of(begun[i]);
                if (i >= quests.Count) quests.Add(state);
                else if (!quests[i].Equals(state)) quests[i] = state;
            }

            while (quests.Count > begun.Count) quests.RemoveAt(quests.Count - 1);
        }

        private void OnReplicated(NetworkListEvent<QuestState> change)
        {
            // The host wrote it; adopting it back would announce its own change a second time.
            if (IsServer) return;

            Adopt(live: true);
        }

        private void Adopt(bool live)
        {
            received.Clear();
            foreach (QuestState state in quests) received.Add(state);
            director.Adopt(received, live);
        }

        private void OnLocalPlayerFinished(Quest quest, int step)
        {
            if (!IsSpawned) return;

            if (IsServer) director.RecordFinished(NetworkManager.LocalClientId, quest.Id, step);
            else ReportFinishedRpc(new FixedString64Bytes(quest.Id), step);
        }

        private void OnGiveRequested(ObjectiveChain quest)
        {
            if (IsSpawned && !IsServer) GiveQuestRpc(new FixedString64Bytes(quest.Id));
        }

        /// <summary>
        /// A client saying its own player has done their part of step <paramref name="step"/> of a quest. The
        /// player is the sender, never an argument, so no client can finish a step for somebody else.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportFinishedRpc(FixedString64Bytes quest, int step, RpcParams rpcParams = default) =>
            director.RecordFinished(rpcParams.Receive.SenderClientId, quest.Value, step);

        /// <summary>A client's <see cref="QuestGiver"/> fired: the server begins the quest, for the whole crew.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void GiveQuestRpc(FixedString64Bytes quest) => director.StartQuest(quest.Value);
    }
}
