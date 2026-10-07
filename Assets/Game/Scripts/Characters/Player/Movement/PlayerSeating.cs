// Sitting a player on a Seat, and getting them up again.
//
// The owner presses interact at a free seat, which asks the server; the server decides (the seat must be free and
// the player not already sitting somewhere) and writes the seat's id into a NetworkVariable. Every machine reads that
// id: each records the claim on the seat it resolves it to, so no machine offers a taken seat or seats a resident in
// it, and the OWNER moves its own body onto the seat (the player's transform is owner-authoritative, so every other
// machine sees the pose arrive over the wire, and the seated animation over the replicated Animator). Jump or
// interact stands the player up the same way round: request, server clears the id, the owner stands itself up.
//
// The id, not the seat, is what travels: a seat is scenery with no NetworkObject, so each machine derives its id alike
// (Seat.Id). A late joiner reads the current id with the spawn. Nothing here is saved: a player loaded mid-sit stands
// where they were, like a player loaded mid-ladder.
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerController))]
    public sealed class PlayerSeating : NetworkBehaviour
    {
        private readonly NetworkVariable<int> seatId = new(NoSeat, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>The wire value for "not sitting"; no seat derives this id.</summary>
        public const int NoSeat = 0;

        // The deciding machine's own record: offline there is no spawn to write the variable through.
        private int decidedSeat = NoSeat;

        private PlayerMovement movement;
        private PlayerController controller;
        private PlayerInputManager inputs;
        private Seat seat;
        private int shownSeat = NoSeat;
        private bool movementSuspended;
        private int satAtFrame;

        private bool Mirrors => IsSpawned && !IsServer;

        /// <summary>The id of the seat this player sits on; <see cref="NoSeat"/> when standing.</summary>
        public int CurrentSeat => Mirrors ? seatId.Value : decidedSeat;

        public bool IsSeated => CurrentSeat != NoSeat;

        /// <summary>Whether this machine's player may start sitting: their own body, alive, not already on a seat.</summary>
        public bool CanSit => Network.Owns(this) && !IsSeated && !controller.IsDead;

        /// <summary>The seating of the player an interactor belongs to; null for anything else.</summary>
        public static PlayerSeating Of(Interactor interactor) => interactor != null ? interactor.GetComponentInParent<PlayerSeating>() : null;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.SitRequest, OnSitRequested);
            this.NetOn(NetMsg.StandRequest, OnLeaveRequested);
            controller.OnPlayerDeath += OnDied;
        }

        // Not OnEnable: the controller finds its input component in its own Awake, which may run after this one's OnEnable.
        private void Start()
        {
            inputs = controller.Input;
            if (inputs == null) return;

            inputs.OnJumpPressed += OnLeavePressed;
            inputs.OnInteractPressed += OnLeavePressed;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.SitRequest, OnSitRequested);
            this.NetOff(NetMsg.StandRequest, OnLeaveRequested);
            controller.OnPlayerDeath -= OnDied;
            Show(NoSeat);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (inputs == null) return;

            inputs.OnJumpPressed -= OnLeavePressed;
            inputs.OnInteractPressed -= OnLeavePressed;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) seatId.OnValueChanged += OnSeatChanged;

            // Read once as well as subscribing: a late joiner gets the current value with the spawn.
            Show(CurrentSeat);
        }

        public override void OnNetworkDespawn()
        {
            seatId.OnValueChanged -= OnSeatChanged;
            Show(NoSeat);
        }

        private void OnSeatChanged(int previous, int next) => Show(next);

        /// <summary>Owner only. Asks the server to sit this player on <paramref name="target"/>.</summary>
        public void RequestSit(Seat target)
        {
            if (target == null || !CanSit || !target.IsFree) return;

            this.NetToServer(NetMsg.SitRequest, new NetArg { A = target.Id });
        }

        /// <summary>Owner only. Asks the server to stand this player up.</summary>
        public void RequestStand()
        {
            if (Network.Owns(this) && IsSeated) this.NetToServer(NetMsg.StandRequest, default);
        }

        // Jump and interact both stand the player up. Not on the frame they sat: offline the sit is decided inside the very
        // press that asked for it, and the same press would stand them straight back up.
        private void OnLeavePressed()
        {
            if (Time.frameCount != satAtFrame) RequestStand();
        }

        // ── the server's decision ────────────────────────────────────────────────────────────────

        private void OnSitRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || !Network.MayActFor(gameObject, sender) || IsSeated || controller.IsDead) return;

            Seat target = Seat.Find(arg.A);
            if (target == null || !target.TryClaim(transform)) return;

            Decide(target.Id);
        }

        private void OnLeaveRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || !Network.MayActFor(gameObject, sender)) return;

            Decide(NoSeat);
        }

        private void OnDied()
        {
            if (Network.Simulates(this)) Decide(NoSeat);
        }

        // The server (or the only machine there is) writes the id every machine shows.
        private void Decide(int id)
        {
            if (!Network.Decides) return;

            decidedSeat = id;
            if (IsSpawned && IsServer) seatId.Value = id;
            Show(id);
        }

        // ── what every machine shows ─────────────────────────────────────────────────────────────

        // Idempotent: it acts only when the shown seat differs.
        private void Show(int id)
        {
            if (id == shownSeat) return;

            Seat previous = seat;
            int previousId = shownSeat;
            shownSeat = id;
            seat = Seat.Find(id);

            if (previous != null)
            {
                previous.Vacated -= OnSeatVacated;
                previous.Release(transform);
            }

            if (seat != null)
            {
                seat.TryClaim(transform);
                seat.Vacated += OnSeatVacated;
            }

            if (!Network.Owns(this)) return;

            if (seat != null && previousId == NoSeat) SitDown(seat);
            else if (seat == null) StandUp(previous);
        }

        // The seat moved, or was switched off: the sitter is thrown off, and the server says so for everyone.
        private void OnSeatVacated(Seat vacated, Transform who)
        {
            if (who == transform && Network.Decides) Decide(NoSeat);
        }

        // ── the owner's body ─────────────────────────────────────────────────────────────────────

        private void SitDown(Seat on)
        {
            satAtFrame = Time.frameCount;
            NetworkedTeleport.Move(gameObject, RootFor(on.FeetPosition), on.Facing);

            if (movement.enabled)
            {
                movement.enabled = false;
                movementSuspended = true;
            }
            CarriedBody.Hold(gameObject, this);
            if (on.TryGetComponent(out ChairPose pose)) pose.PoseRider(transform);
        }

        // Up out of the seat, onto the floor in front of it, BEFORE the body gets its weight back.
        private void StandUp(Seat from)
        {
            if (from != null)
            {
                if (from.TryGetComponent(out ChairPose pose)) pose.ReleaseRider(transform);
                NetworkedTeleport.Move(gameObject, RootFor(from.StandUpPosition), from.Facing);
            }

            CarriedBody.Release(gameObject, this);
            if (movementSuspended)
            {
                movementSuspended = false;
                // A player who died in the seat keeps the freeze death applied: PlayerController owns it.
                if (!controller.IsDead) movement.enabled = true;
            }
        }

        // The body root that puts the capsule's feet at a floor point: the root is not at the soles.
        private Vector3 RootFor(Vector3 feetPoint)
        {
            Bounds capsule = movement.BodyCapsule.bounds;
            return transform.position + (feetPoint - new Vector3(capsule.center.x, capsule.min.y, capsule.center.z));
        }
    }
}
