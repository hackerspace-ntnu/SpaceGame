using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Puts the crew's place in the objective chain on the wire: one server-written value that
    /// every peer adopts into its own <see cref="ObjectiveDirector"/>, and one report the other way
    /// — "my player has done their part of this step".
    ///
    /// <para>
    /// A <see cref="NetworkVariable{T}"/> rather than a message, for the reason the module rack
    /// replicates a mask: a message answers whoever was listening, and a player joining halfway
    /// through the salvage was not. The value is simply true when they arrive.
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
        private readonly NetworkVariable<ObjectiveProgress> progress = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

            progress.OnValueChanged += OnReplicated;
            director.LocalPlayerFinished += OnLocalPlayerFinished;

            if (IsServer)
            {
                director.Changed += Publish;
                Publish(live: true);
                return;
            }

            // The read-on-spawn. Netcode hands a joiner the current value in the spawn payload and
            // never replays OnValueChanged for it, so this is the only moment a late joiner learns
            // where the crew are — and it is not "live": they missed the briefing, by definition.
            director.Adopt(progress.Value, live: false);
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
            if (director == null) return;

            director.Changed -= Publish;
            director.LocalPlayerFinished -= OnLocalPlayerFinished;
        }

        private void Publish(bool live)
        {
            if (IsServer && IsSpawned) progress.Value = director.Progress;
        }

        private void OnReplicated(ObjectiveProgress previous, ObjectiveProgress current)
        {
            // The host wrote it; adopting it back would announce its own change a second time.
            if (IsServer) return;

            director.Adopt(current, live: true);
        }

        private void OnLocalPlayerFinished(int step)
        {
            if (!IsSpawned) return;

            if (IsServer) director.RecordFinished(NetworkManager.LocalClientId, step);
            else ReportFinishedRpc(step);
        }

        /// <summary>
        /// A client saying its own player has done their part of step <paramref name="step"/>. The
        /// player is the sender, never an argument, so no client can finish a step for somebody else.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportFinishedRpc(int step, RpcParams rpcParams = default) =>
            director.RecordFinished(rpcParams.Receive.SenderClientId, step);
    }
}
