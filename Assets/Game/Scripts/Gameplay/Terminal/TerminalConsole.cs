using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;

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
    /// is saved — a page selection is session state, not world state. Who is at the keyboard is
    /// <see cref="ClaimableConsole"/>'s, shared with the satellite dish's controls.
    /// </para>
    /// </summary>
    public class TerminalConsole : ClaimableConsole, IInteractable, IInteractionMoment, IInteractionReadout
    {
        /// <summary>Nothing on the body: the terminal takes the camera.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public const int PageCount = 4;
        public static readonly string[] PageNames = { "SHIP", "STATUS", "GPS", "COMMS" };

        [Tooltip("The per-machine zoom-in this console opens on a press. On the same prefab; wired by the builder.")]
        [SerializeField] private TerminalFocusSession session;

        private readonly NetworkVariable<int> networkPage = new(0);

        private int page;

        /// <summary>The shown page changed, on this machine. The screen redraws off it.</summary>
        public event Action<int> PageChanged;

        public int Page => page;

        // ── The crosshair ────────────────────────────────────────────────────

        public string Label => "Terminal";
        public string Prompt => Occupied ? "In use" : "RMB: use terminal";
        public float? Value01 => null;
        public string ValueText => PageNames[Mathf.Clamp(page, 0, PageCount - 1)];

        /// <summary>Usable whenever it is wired. Per-player refusal is the contextual half in the base.</summary>
        public bool CanInteract() => session != null;

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

            RequestClaim(interactor);
        }

        /// <summary>Ask for a page. The server decides; every machine applies the answer.</summary>
        public void RequestPage(int index)
        {
            Network.Execute(
                local: () => SetPageAuthoritative(index),
                client: () => SetPageServerRpc(index));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetPageServerRpc(int index) => SetPageAuthoritative(index);

        // ── Authority ────────────────────────────────────────────────────────

        private void SetPageAuthoritative(int value)
        {
            value = Mathf.Clamp(value, 0, PageCount - 1);

            if (IsNetSpawned && IsServer)
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

        // ── Netcode lifecycle ────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkPage.OnValueChanged += OnNetworkPageChanged;
            SetPageLocal(networkPage.Value);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            networkPage.OnValueChanged -= OnNetworkPageChanged;
        }

        private void OnNetworkPageChanged(int previous, int current) => SetPageLocal(current);
    }
}
