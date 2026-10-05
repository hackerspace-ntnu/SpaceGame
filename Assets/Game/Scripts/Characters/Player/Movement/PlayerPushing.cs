// Putting a player's hands on a cart, and taking them off again.
//
// The owner presses interact at a free cart, which asks the server; the server decides (the cart must be free, near, and the
// player alive and not already pushing) and writes the cart's id into a NetworkVariable. Every machine reads that id and
// resolves it to its own copy of the cart: CartPusher then poses the cart from the replicated body and reaches the arms for the
// handles, so a pushed cart costs the wire nothing while it moves. The OWNER also takes the weight: its speed is held down and
// jumping is off, because the owner's input is what moves the body (the player's transform is owner-authoritative).
//
// Interact, jump, a hotbar item or death lets go, the same way round: request, server clears the id, every machine lets go.
// The cart is left standing where it is and the server records that in the PushableLedger.
//
// The id, not the cart, is what travels: a cart is scenery with no NetworkObject, so each machine derives its id alike
// (Pushable.Id). A late joiner reads the current id with the spawn. Nothing here is saved: a player loaded mid-push stands
// where they were with empty hands, and the cart stays where the ledger put it.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerController))]
    public sealed class PlayerPushing : NetworkBehaviour
    {
        [Tooltip("Fastest the owner may walk with both hands on a cart, in m/s (the ordinary walk is 6).")]
        [SerializeField, Min(0.5f)] private float pushSpeed = 3.5f;

        /// <summary>The wire value for "not pushing"; no cart derives this id.</summary>
        public const int NoCart = 0;

        private readonly NetworkVariable<int> cartId = new(NoCart, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // The deciding machine's own record: offline there is no spawn to write the variable through.
        private int decidedCart = NoCart;

        private PlayerMovement movement;
        private PlayerController controller;
        private PlayerSeating seating;
        private CartPusher pusher;
        private PlayerInputManager inputs;
        private IPlayerInventory inventory;
        private int grippedAtFrame;

        private bool Mirrors => IsSpawned && !IsServer;

        /// <summary>The id of the cart this player holds; <see cref="NoCart"/> when its hands are free.</summary>
        public int CurrentCart => Mirrors ? cartId.Value : decidedCart;

        public bool IsPushing => CurrentCart != NoCart;

        /// <summary>Whether this machine's player may take hold of a cart: their own body, alive, hands free, not sitting.</summary>
        public bool CanGrip => Network.Owns(this) && !IsPushing && !controller.IsDead && (seating == null || !seating.IsSeated) && pusher.CanReach;

        /// <summary>The pushing of the player an interactor belongs to; null for anything else.</summary>
        public static PlayerPushing Of(Interactor interactor) => interactor != null ? interactor.GetComponentInParent<PlayerPushing>() : null;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<PlayerController>();
            seating = GetComponent<PlayerSeating>();
            pusher = CartPusher.On(gameObject);
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.PushRequest, OnGripRequested);
            this.NetOn(NetMsg.ReleaseRequest, OnReleaseRequested);
            controller.OnPlayerDeath += OnDied;
            pusher.Dropped += OnDropped;
        }

        // Not OnEnable: the controller finds its input and inventory in its own Awake, which may run after this one's OnEnable.
        private void Start()
        {
            inputs = controller.Input;
            if (inputs != null)
            {
                inputs.OnJumpPressed += OnLetGoPressed;
                inputs.OnInteractPressed += OnLetGoPressed;
            }

            inventory = controller.PlayerInventory;
            if (inventory != null) inventory.OnSlotSelected += OnSlotSelected;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.PushRequest, OnGripRequested);
            this.NetOff(NetMsg.ReleaseRequest, OnReleaseRequested);
            controller.OnPlayerDeath -= OnDied;
            pusher.Dropped -= OnDropped;
            pusher.Release();
            movement.StopHauling();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (inputs != null)
            {
                inputs.OnJumpPressed -= OnLetGoPressed;
                inputs.OnInteractPressed -= OnLetGoPressed;
            }

            if (inventory != null) inventory.OnSlotSelected -= OnSlotSelected;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) cartId.OnValueChanged += OnCartChanged;

            // Read once as well as subscribing: a late joiner gets the current value with the spawn.
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            cartId.OnValueChanged -= OnCartChanged;
            pusher.Release();
            movement.StopHauling();
        }

        private void OnCartChanged(int previous, int next) => Apply();

        // A cart in a chunk that had not loaded when the id arrived: look again until it has.
        private void Update()
        {
            if (IsPushing && pusher.Cart == null) Apply();
        }

        /// <summary>Owner only. Asks the server to put this player's hands on <paramref name="target"/>.</summary>
        public void RequestGrip(Pushable target)
        {
            if (target == null || !CanGrip || !target.IsFree) return;

            this.NetToServer(NetMsg.PushRequest, new NetArg { A = target.Id });
        }

        /// <summary>Owner only. Asks the server to take this player's hands off the cart.</summary>
        public void RequestRelease()
        {
            if (Network.Owns(this) && IsPushing) this.NetToServer(NetMsg.ReleaseRequest, default);
        }

        // Jump, interact and a drawn item all let go. Not on the frame the grip began: offline the grip is decided inside the very
        // press that asked for it, and the same press would let go again.
        private void OnLetGoPressed()
        {
            if (Time.frameCount != grippedAtFrame) RequestRelease();
        }

        private void OnSlotSelected(InventorySlot slot)
        {
            if (slot != null && !slot.IsEmpty) RequestRelease();
        }

        // ── the server's decision ────────────────────────────────────────────────────────────────

        private void OnGripRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || !Network.MayActFor(gameObject, sender) || IsPushing || controller.IsDead) return;

            Pushable target = Pushable.Find(arg.A);
            if (target == null || !target.IsAuthored || !target.IsWithinGrip(transform.position) || !target.TryClaim(transform)) return;

            Decide(target.Id);
        }

        private void OnReleaseRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || !Network.MayActFor(gameObject, sender)) return;

            Decide(NoCart);
        }

        private void OnDied()
        {
            if (Network.Simulates(this)) Decide(NoCart);
        }

        // The cart went away from under the hands (its chunk unloaded): the server says so for everyone.
        private void OnDropped(Pushable dropped)
        {
            if (Network.Decides) Decide(NoCart);
        }

        // The server (or the only machine there is) writes the id every machine shows.
        private void Decide(int id)
        {
            if (!Network.Decides) return;

            decidedCart = id;
            if (IsSpawned && IsServer) cartId.Value = id;
            Apply();
        }

        // ── what every machine shows ─────────────────────────────────────────────────────────────

        // Idempotent: it acts only when the held cart differs from the one the id names.
        private void Apply()
        {
            int wanted = CurrentCart;
            Pushable held = pusher.Cart;

            if (wanted == NoCart)
            {
                pusher.Release();
                movement.StopHauling();
                return;
            }

            if (held != null && held.Id == wanted) return;

            Pushable cart = Pushable.Find(wanted);
            if (cart == null || !pusher.Grip(cart)) return;

            grippedAtFrame = Time.frameCount;
            if (!Network.Owns(this)) return;

            // Both hands are on the cart: whatever was in them goes away.
            if (inventory != null && inventory.SelectedSlotIndex >= 0) inventory.SelectSlot(inventory.SelectedSlotIndex);
            movement.StartHauling(pushSpeed);
        }
    }
}
