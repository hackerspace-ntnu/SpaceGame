using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The standing terminal's shared state: which page is up, and who is at the keyboard.
    ///
    /// <para>
    /// Both are server-decided and replicated, because the screen is a thing in the world that
    /// everybody can see: a crewmate looking over the operator's shoulder must see the page the
    /// operator picked, and a second player must not be able to walk up and flip it under them.
    /// The zoomed-in camera, by contrast, is local to the presser's machine —
    /// <see cref="TerminalFocusSession"/> — and nothing about it goes on the wire.
    /// </para>
    /// <para>
    /// A ship fixture: it carries no NetworkObject of its own and inherits the hull's, which is what makes the NetworkVariables and RPCs replicate.
    /// Offline, and before spawn, the same calls write the local mirrors directly. Neither value
    /// is saved — a page selection is session state, not world state.
    /// </para>
    /// </summary>
    public class TerminalConsole : NetworkBehaviour, IInteractable, IContextualInteractable, IInteractionReadout
    {
        public const int PageCount = 3;
        public static readonly string[] PageNames = { "SHIP", "STATUS", "GPS" };

        /// <summary>Nobody is at the terminal.</summary>
        public const ulong NoOperator = ulong.MaxValue;

        [Tooltip("The per-machine zoom-in this console opens on a press. On the same prefab; wired by the builder.")]
        [SerializeField] private TerminalFocusSession session;

        private readonly NetworkVariable<int> networkPage = new(0);
        private readonly NetworkVariable<ulong> networkOperator = new(NoOperator);

        private int page;
        private ulong operatorId = NoOperator;
        private bool spawned;

        /// <summary>The shown page changed, on this machine. The screen redraws off it.</summary>
        public event Action<int> PageChanged;

        public int Page => page;

        public bool Occupied => operatorId != NoOperator;

        // ── The crosshair ────────────────────────────────────────────────────

        public string Label => "Terminal";
        public string Prompt => Occupied ? "In use" : "RMB: use terminal";
        public float? Value01 => null;
        public string ValueText => PageNames[Mathf.Clamp(page, 0, PageCount - 1)];

        /// <summary>Usable whenever it is wired. Per-player refusal is the contextual half below.</summary>
        public bool CanInteract() => session != null;

        /// <summary>One operator at a time. The operator can always re-press their own terminal.</summary>
        public bool CanInteract(Interactor interactor) => !Occupied || operatorId == ClientIdOf(interactor);

        // ── Pressing it ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs on the presser's machine only. The zoom-in is opened here, at once, and the claim
        /// is sent to the server; a claim the server refuses (somebody else got there first, over
        /// a slower link) shows as the other player's page changes still winning.
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (interactor == null || !CanInteract() || !CanInteract(interactor)) return;

            var player = interactor.GetComponentInParent<PlayerController>();
            if (!session.Enter(this, player, interactor)) return;

            Network.Execute(
                local: () => Claim(ClientIdOf(interactor)),
                client: () => InteractorRelay.RequestFrom(interactor, ClaimServerRpc));
        }

        /// <summary>Ask for a page. The server decides; every machine applies the answer.</summary>
        public void RequestPage(int index)
        {
            Network.Execute(
                local: () => SetPageAuthoritative(index),
                client: () => SetPageServerRpc(index));
        }

        /// <summary>The operator has stepped away. Only their own claim is released.</summary>
        public void Release(Interactor interactor)
        {
            if (interactor == null) return;

            Network.Execute(
                local: () => Vacate(ClientIdOf(interactor)),
                client: () => InteractorRelay.RequestFrom(interactor, ReleaseServerRpc));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ClaimServerRpc(NetworkObjectReference interactorRef)
        {
            if (InteractorRelay.TryResolve(interactorRef, out Interactor interactor))
                Claim(ClientIdOf(interactor));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetPageServerRpc(int index) => SetPageAuthoritative(index);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReleaseServerRpc(NetworkObjectReference interactorRef)
        {
            if (InteractorRelay.TryResolve(interactorRef, out Interactor interactor))
                Vacate(ClientIdOf(interactor));
        }

        // ── Authority ────────────────────────────────────────────────────────

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
            if (spawned && IsServer)
            {
                // The NetworkVariable callback mirrors it on the host as well.
                networkOperator.Value = value;
                return;
            }

            operatorId = value;
        }

        private void SetPageAuthoritative(int value)
        {
            value = Mathf.Clamp(value, 0, PageCount - 1);

            if (spawned && IsServer)
            {
                networkPage.Value = value;
                return;
            }

            SetPageLocal(value);
        }

        private void SetPageLocal(int value)
        {
            if (value == page) return;
            page = value;
            PageChanged?.Invoke(page);
        }

        /// <summary>
        /// Whose press this is. Offline there is exactly one player and no NetworkObject, and
        /// the server's own id is what that player gets — the same answer the host would.
        /// </summary>
        private static ulong ClientIdOf(Interactor interactor)
        {
            NetworkObject body = interactor != null ? interactor.GetComponentInParent<NetworkObject>() : null;
            return body != null ? body.OwnerClientId : NetworkManager.ServerClientId;
        }

        // ── Netcode lifecycle ────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            spawned = true;

            networkPage.OnValueChanged += OnNetworkPageChanged;
            networkOperator.OnValueChanged += OnNetworkOperatorChanged;
            SetPageLocal(networkPage.Value);
            operatorId = networkOperator.Value;

            // An operator who drops mid-session would otherwise hold the terminal for good.
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;

            networkPage.OnValueChanged -= OnNetworkPageChanged;
            networkOperator.OnValueChanged -= OnNetworkOperatorChanged;

            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
        }

        private void OnNetworkPageChanged(int previous, int current) => SetPageLocal(current);

        private void OnNetworkOperatorChanged(ulong previous, ulong current) => operatorId = current;

        private void OnClientLeft(ulong clientId)
        {
            if (operatorId == clientId) SetOperatorAuthoritative(NoOperator);
        }
    }
}
