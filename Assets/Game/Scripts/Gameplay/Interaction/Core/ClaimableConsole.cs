using Unity.Netcode;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A console one player at a time takes over: the standing terminal, the satellite dish's controls.
    ///
    /// <para>
    /// Owns the claim and nothing else. Who is at the controls is server-decided and replicated in one
    /// <see cref="NetworkVariable{T}"/>, because a second player must see the console as taken and must
    /// not be able to walk up and drive it from under the first. The claim is released on every exit
    /// path the subclass routes through <see cref="Release"/>, and by the server when the operator's
    /// client disconnects — an operator who drops mid-session would otherwise hold it for good.
    /// </para>
    /// <para>
    /// A fixture: it carries no NetworkObject of its own and inherits its building's or hull's, which is
    /// what makes the variable and the RPCs replicate. Offline, and before spawn, the same calls write
    /// the local mirror directly.
    /// </para>
    /// </summary>
    public abstract class ClaimableConsole : NetworkBehaviour, IContextualInteractable
    {
        /// <summary>Nobody is at the console.</summary>
        public const ulong NoOperator = ulong.MaxValue;

        private readonly NetworkVariable<ulong> networkOperator = new(NoOperator);

        private ulong operatorId = NoOperator;

        /// <summary>True between this console's network spawn and despawn: the variables are live.</summary>
        protected bool IsNetSpawned { get; private set; }

        public bool Occupied => operatorId != NoOperator;

        /// <summary>The client id at the controls, or <see cref="NoOperator"/>.</summary>
        public ulong OperatorId => operatorId;

        /// <summary>One operator at a time. The operator can always re-press their own console.</summary>
        public bool CanInteract(Interactor interactor) => !Occupied || operatorId == ClientIdOf(interactor);

        /// <summary>Ask the server for the controls on the presser's behalf. Refused if somebody else holds them.</summary>
        protected void RequestClaim(Interactor interactor)
        {
            if (interactor == null) return;

            Network.Execute(
                local: () => Claim(ClientIdOf(interactor)),
                client: () => InteractorRelay.RequestFrom(interactor, ClaimServerRpc));
        }

        /// <summary>The operator has stepped away. Only their own claim is released.</summary>
        public void Release(Interactor interactor)
        {
            if (interactor == null) return;

            Network.Execute(
                local: () => Vacate(ClientIdOf(interactor)),
                client: () => InteractorRelay.RequestFrom(interactor, ReleaseServerRpc));
        }

        /// <summary>
        /// The operator changed, on this machine. Runs on every machine, for every change — a claim, a
        /// release, a disconnect, and the value a late joiner adopts on spawn.
        /// </summary>
        protected virtual void OnOperatorChanged(ulong previous, ulong current) { }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ClaimServerRpc(NetworkObjectReference interactorRef)
        {
            if (InteractorRelay.TryResolve(interactorRef, out Interactor interactor))
                Claim(ClientIdOf(interactor));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReleaseServerRpc(NetworkObjectReference interactorRef)
        {
            if (InteractorRelay.TryResolve(interactorRef, out Interactor interactor))
                Vacate(ClientIdOf(interactor));
        }

        private void Claim(ulong clientId)
        {
            if (Occupied && operatorId != clientId) return;
            SetOperatorAuthoritative(clientId);
        }

        private void Vacate(ulong clientId)
        {
            if (operatorId != clientId) return;
            SetOperatorAuthoritative(NoOperator);
        }

        private void SetOperatorAuthoritative(ulong value)
        {
            if (IsNetSpawned && IsServer)
            {
                // The NetworkVariable callback mirrors it on the host as well.
                networkOperator.Value = value;
                return;
            }

            SetOperatorLocal(value);
        }

        private void SetOperatorLocal(ulong value)
        {
            if (value == operatorId) return;

            ulong previous = operatorId;
            operatorId = value;
            OnOperatorChanged(previous, value);
        }

        /// <summary>
        /// Whose press this is. Offline there is exactly one player and no NetworkObject, and the
        /// server's own id is what that player gets — the same answer the host would.
        /// </summary>
        public static ulong ClientIdOf(Interactor interactor)
        {
            NetworkObject body = interactor != null ? interactor.GetComponentInParent<NetworkObject>() : null;
            return body != null ? body.OwnerClientId : NetworkManager.ServerClientId;
        }

        public override void OnNetworkSpawn()
        {
            IsNetSpawned = true;

            networkOperator.OnValueChanged += OnNetworkOperatorChanged;
            SetOperatorLocal(networkOperator.Value);

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
        }

        public override void OnNetworkDespawn()
        {
            IsNetSpawned = false;

            networkOperator.OnValueChanged -= OnNetworkOperatorChanged;

            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
        }

        private void OnNetworkOperatorChanged(ulong previous, ulong current) => SetOperatorLocal(current);

        private void OnClientLeft(ulong clientId)
        {
            if (operatorId == clientId) SetOperatorAuthoritative(NoOperator);
        }
    }
}
