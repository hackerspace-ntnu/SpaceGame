using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// The hoist's control console on the third-floor balcony. Interact to take it, and Interact or Escape to let go;
    /// while at it the operator sees the whole pit floor from a still camera over the balcony
    /// (<see cref="WorkshopCraneCamera"/>), the movement keys run the hook across the pit the way that view faces, Jump
    /// winds it up (and then lifts the whole gantry up its towers), Sprint lets it down, and Use works the grab lever:
    /// take hold of the core lying under the hook, or let go of the one on it — over an empty mount on the ship, that
    /// fits it.
    ///
    /// <para>
    /// <b>One driver, decided by the server.</b> The console is a <see cref="VehicleStation"/>: the claim says who
    /// drives, and the lever positions ride the claim's request, packed (<see cref="WorkshopCraneCommand"/>). The
    /// hoist's place is the <see cref="WorkshopCrane"/>'s to announce. The driver's machine moves its own hoist at once
    /// and is put right when they let go.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopCraneConsole : VehicleStation, IInteractionReadout
    {
        private const string HintId = "workshop-crane";

        [SerializeField] private WorkshopCrane crane;

        [Tooltip("Where the operator stands, facing the console and the pit: the movement keys run the hook the way it faces.")]
        [SerializeField] private Transform stance;

        [Tooltip("Metres across the floor from the stance the operator may stand to take the console; at twice this it lets go of them.")]
        [SerializeField, Min(0.5f)] private float reach = 2f;

        [Tooltip("How far a movement key must be pushed along a run to work it.")]
        [SerializeField, Range(0.05f, 1f)] private float leverDeadzone = 0.35f;

        [Tooltip("Seconds the controls are shown when the console is taken.")]
        [SerializeField, Min(0.5f)] private float hintSeconds = 8f;

        [Tooltip("The operator's view while at the console: a still camera over the balcony, looking down on the pit floor.")]
        [SerializeField] private WorkshopCraneCamera view = new();

        private readonly StationFooting footing = new();
        private readonly WorkshopCraneMountGhost ghost = new();
        private WorkshopCraneCommand command;
        private bool useWasHeld;

#if UNITY_EDITOR
        /// <summary>Editor, for the builder.</summary>
        public void Author(WorkshopCrane hoist, Transform operatorStance, float operatorReach)
        {
            crane = hoist;
            stance = operatorStance;
            reach = operatorReach;
        }
#endif

        // ── VehicleStation ───────────────────────────────────────────────────

        protected override bool Exclusive => true;

        public override bool CanInteract() => crane != null && !IsManned;

        public override void Interact(Interactor interactor)
        {
            if (crane == null || IsManned) return;

            GameObject player = ResolvePlayer(interactor);
            if (!Near(player))
            {
                PlayerHints.Show(HintId, "Step up to the crane console", 3f);
                return;
            }
            RequestClaim(interactor);
        }

        protected override bool CanBeManned(GameObject player) => Near(player);

        protected override bool ShouldRelease(GameObject player) =>
            player == null || !player.activeInHierarchy ||
            (player.transform.position - StandAt.position).sqrMagnitude > 4f * reach * reach;

        protected override float LocalRequest() => command.Pack();

        /// <summary>The deciding machine moves the hoist: by its own keys when the driver is here, by the report otherwise.</summary>
        protected override float AdvanceOnServer(float wanted, float deltaTime)
        {
            crane.Drive(IsMannedByLocalPlayer ? command : WorkshopCraneCommand.Unpack(wanted), deltaTime);
            return 0f;
        }

        protected override void OnManned(GameObject player)
        {
            // The first hand on it stops the round for good, on every machine alike: they all keep the same clock.
            crane.TakeOver();
            if (!Network.Owns(player.transform)) return;

            crane.DrivenHere = true;
            footing.Begin(player, StandAt, reach, () => RequestRelease(player));
            view.Begin(player, StandAt, FloorCentre);
            useWasHeld = true;
            PlayerHints.Show(HintId, "WASD: move the hook · Space: up · Shift: down · LMB: grab / let go · Esc or RMB: step away",
                             hintSeconds);
        }

        protected override void OnUnmanned(GameObject player)
        {
            command = default;
            if (crane != null) crane.DrivenHere = false;
            ghost.Clear();
            view.End();
            footing.End();
        }

        // ── Driving ──────────────────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsMannedByLocalPlayer || crane == null) return;

            // Escape is the key that gets you off every mount; gated so closing the chat box does not also let go.
            if (GameplayMenuScope.AcceptsGameplayInput && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                RequestRelease(Occupant);
                return;
            }

            command = Read();

            // A use press is the grab lever. The press that took the console must come up first.
            bool use = footing.UseHeld;
            if (use && !useWasHeld) Latch();
            useWasHeld = use;

            // A driver who is not the deciding machine moves their own hoist now and hears the truth later.
            if (!Network.Simulates(this)) crane.Drive(command, Time.deltaTime);

            ghost.Show(crane);
        }

        private void LateUpdate() => footing.Hold();

        /// <summary>The keys as levers: across and along the way the operator looks, onto the bridge's and trolley's runs.</summary>
        private WorkshopCraneCommand Read()
        {
            Vector2 keys = footing.Keys;
            Vector3 forward = view.IsShowing ? view.Facing : Vector3.ProjectOnPlane(StandAt.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 wanted = forward * keys.y + right * keys.x;

            int vertical = (footing.JumpHeld ? 1 : 0) - (footing.SprintHeld ? 1 : 0);
            return new WorkshopCraneCommand(Lever(wanted, crane.BridgeDirection), Lever(wanted, crane.TrolleyDirection), vertical);
        }

        private int Lever(Vector3 wanted, Vector3 run)
        {
            float along = Vector3.Dot(wanted, run);
            return Mathf.Abs(along) < leverDeadzone ? 0 : (int)Mathf.Sign(along);
        }

        private void Latch()
        {
            if (!crane.IsLoaded && !crane.TryFindLooseLoad(out _))
            {
                PlayerHints.Show(HintId, "Nothing under the hook to take hold of. Let it down onto the core, or hang the core on it.", 4f);
                return;
            }
            crane.RequestLatch(Occupant);
        }

        private bool Near(GameObject player)
        {
            if (player == null) return false;
            Vector3 away = player.transform.position - StandAt.position;
            return new Vector2(away.x, away.z).sqrMagnitude <= reach * reach;
        }

        private Transform StandAt => stance != null ? stance : transform;

        /// <summary>The middle of the main room's floor: the workshop's own origin.</summary>
        private Vector3 FloorCentre
        {
            get
            {
                Workshop workshop = GetComponentInParent<Workshop>();
                return workshop != null ? workshop.transform.position : crane.transform.position;
            }
        }

        // ── IInteractionReadout ──────────────────────────────────────────────

        public string Label => "Crane console";

        public string Prompt => !IsManned
            ? "RMB: take the controls"
            : IsMannedByLocalPlayer
                ? "RMB: step away"
                : "Somebody else is at the controls";

        public float? Value01 => null;

        public string ValueText => crane != null && crane.IsLoaded ? "Core on the hook" : string.Empty;
    }
}
