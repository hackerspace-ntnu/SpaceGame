using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The satellite tower's control lectern: right-click it to take the dish's controls.
    ///
    /// <para>
    /// The same shape as the standing terminal, and on the same base: who is at the controls is
    /// <see cref="ClaimableConsole"/>'s, server-decided and replicated, and the camera and key reads are
    /// <see cref="DishControlSession"/>'s, local to the operator's machine. What is new is a steady
    /// stream of input — the operator's slew command — which goes to the server on every change and is
    /// accepted there only from the client that holds the claim. The server's <see cref="DishRig"/> runs
    /// the motor; nobody predicts it, because the motor's own spool-up is longer than any round trip
    /// and the operator's readout acknowledges the press at once.
    /// </para>
    /// </summary>
    public sealed class DishConsole : ClaimableConsole, IInteractable, IInteractionMoment, IInteractionReadout
    {
        [Tooltip("The dish this console drives. On the tower's root.")]
        [SerializeField] private DishRig rig;

        [Tooltip("The per-machine take-over this console opens on a press. On the same object.")]
        [SerializeField] private DishControlSession session;

        /// <summary>Nothing on the body: the controls take the camera.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public DishRig Rig => rig;

        // ── The crosshair ────────────────────────────────────────────────────

        public string Label => "Dish control";
        public string Prompt => Occupied ? "In use" : "RMB: take the controls";
        public float? Value01 => null;
        public string ValueText => rig != null ? DishReadout.Format(rig.Azimuth, rig.Elevation) : string.Empty;

        /// <summary>Usable whenever it is wired. Per-player refusal is the contextual half in the base.</summary>
        public bool CanInteract() => rig != null && session != null;

        /// <summary>Runs on the presser's machine only: take the camera at once, then ask for the claim.</summary>
        public void Interact(Interactor interactor)
        {
            if (interactor == null || !CanInteract() || !CanInteract(interactor)) return;

            var player = interactor.GetComponentInParent<PlayerController>();
            if (!session.Enter(this, player, interactor)) return;

            RequestClaim(interactor);
        }

        /// <summary>The operator's slew command, x = azimuth, y = elevation. Sent only when it changes.</summary>
        public void Steer(Vector2 command)
        {
            Network.Execute(
                local: () => SteerAuthoritative(Network.LocalClientId, command),
                client: () => SteerServerRpc(command));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SteerServerRpc(Vector2 command, RpcParams rpcParams = default) =>
            SteerAuthoritative(rpcParams.Receive.SenderClientId, command);

        /// <summary>Only the operator steers. A command from anyone else — a stale one from a client that has just lost the claim — is dropped.</summary>
        private void SteerAuthoritative(ulong sender, Vector2 command)
        {
            if (rig == null || OperatorId != sender) return;
            rig.Drive(command);
        }

        /// <summary>
        /// The controls changed hands. Whoever left — on purpose, by dying, by disconnecting — leaves
        /// the motor stopped rather than running on their last command.
        /// </summary>
        protected override void OnOperatorChanged(ulong previous, ulong current)
        {
            if (rig != null && Network.Simulates(this)) rig.Drive(Vector2.zero);
        }
    }
}
