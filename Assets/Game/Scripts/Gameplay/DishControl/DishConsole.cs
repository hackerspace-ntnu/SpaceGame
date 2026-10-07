using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;
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
    /// <para>
    /// <b>Dead until the grappling hook has left its board.</b> The drives answer nothing until somebody
    /// has taken the hook off the catwalk board, because turning the dish down is how the hook reaches
    /// the transmitter on its feed horn: a dish turned before anyone holds the hook is a puzzle solved by
    /// accident. The server latches the drives on the first frame the board no longer holds the hook and
    /// never unlatches them — putting the hook back does not relock a dish somebody has already learned
    /// to drive. The latch is replicated (the locked prompt and refusal are on the presser's machine) and
    /// saved (<c>DishConsoleSaveable</c>); a save from before it reads as locked and re-derives it from
    /// the board's own saved contents on the first server frame.
    /// </para>
    /// </summary>
    public sealed class DishConsole : ClaimableConsole, IInteractable, IInteractionMoment, IInteractionReadout
    {
        [Tooltip("The dish this console drives. On the tower's root.")]
        [SerializeField] private DishRig rig;

        [Tooltip("The per-machine take-over this console opens on a press. On the same object.")]
        [SerializeField] private DishControlSession session;

        [Header("Locked until the hook is taken")]
        [Tooltip("The catwalk board the grappling hook hangs on. None = the drives are never locked.")]
        [SerializeField] private WallInventory hookBoard;

        [Tooltip("The item whose leaving the board wakes the drives.")]
        [SerializeField] private InventoryItem hookItem;

        [Tooltip("What the crosshair says while the drives are dead.")]
        [SerializeField] private string lockedPrompt = "Dish drives unresponsive";

        [SerializeField] private SfxId refusedSound = SfxId.InteractDenied;

        [Header("The mission")]
        [Tooltip("The cradle on the feed horn holding the working transmitter. Read by the lander's quest " +
                 "(FitTransmitterStep) for its one marker on the tower; nothing here acts on it.")]
        [SerializeField] private WallInventory transmitterCradle;

        private readonly NetworkVariable<bool> networkUnlocked = new();

        private bool unlocked;

        private static readonly List<DishConsole> live = new();

        /// <summary>Every dish console whose chunk is loaded. The objective chain asks this, not the scene.</summary>
        public static IReadOnlyList<DishConsole> Live => live;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => live.Clear();

        /// <summary>Nothing on the body: the controls take the camera.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public DishRig Rig => rig;

        /// <summary>The drives answer: the hook has left its board at some point in this world, or there is no board.</summary>
        public bool DrivesUnlocked => unlocked || hookBoard == null;

        public WallInventory TransmitterCradle => transmitterCradle;

        // ── The crosshair ────────────────────────────────────────────────────

        public string Label => "Dish control";
        public string Prompt => !DrivesUnlocked ? lockedPrompt : Occupied ? "In use" : "RMB: take the controls";
        public float? Value01 => null;
        public string ValueText => rig != null ? DishReadout.Format(rig.Azimuth, rig.Elevation) : string.Empty;

        /// <summary>Usable whenever it is wired. Per-player refusal is the contextual half in the base.</summary>
        public bool CanInteract() => rig != null && session != null;

        /// <summary>
        /// Runs on the presser's machine only: refuse with a sound while the drives are dead, else take the
        /// camera at once and ask for the claim.
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (interactor == null || !CanInteract() || !CanInteract(interactor)) return;

            if (!DrivesUnlocked)
            {
                Sfx.Play(refusedSound, transform.position, GetInstanceID());
                return;
            }

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

        /// <summary>
        /// Only the operator steers, and only once the drives are awake. A command from anyone else — a
        /// stale one from a client that has just lost the claim — is dropped.
        /// </summary>
        private void SteerAuthoritative(ulong sender, Vector2 command)
        {
            if (rig == null || OperatorId != sender || !DrivesUnlocked) return;
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

        // ── The latch ────────────────────────────────────────────────────────

        private void OnEnable() => live.Add(this);

        private void OnDisable() => live.Remove(this);

        private void Update()
        {
            if (!unlocked && hookBoard != null && Network.Simulates(this) && !BoardHoldsHook())
                SetUnlocked(true);
        }

        /// <summary>Does the board still hold a copy of the hook? Read off its contents, which the save and the wire both keep.</summary>
        public bool BoardHoldsHook()
        {
            if (hookBoard == null || hookItem == null) return false;

            foreach (PackPlacement placement in hookBoard.Layout.Placements)
                if (hookBoard.ItemFor(placement.ItemId) == hookItem) return true;

            return false;
        }

        /// <summary>The save system's way in. Authority only; a record of "locked" lets the board re-derive it.</summary>
        public void RestoreUnlocked(bool value)
        {
            if (Network.Simulates(this)) SetUnlocked(value);
        }

        private void SetUnlocked(bool value)
        {
            unlocked = value;
            if (IsNetSpawned && IsServer) networkUnlocked.Value = value;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkUnlocked.OnValueChanged += OnNetworkUnlockedChanged;

            if (IsServer) networkUnlocked.Value = unlocked;
            else unlocked = networkUnlocked.Value;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkUnlocked.OnValueChanged -= OnNetworkUnlockedChanged;
        }

        private void OnNetworkUnlockedChanged(bool previous, bool current)
        {
            if (!IsServer) unlocked = current;
        }
    }
}
