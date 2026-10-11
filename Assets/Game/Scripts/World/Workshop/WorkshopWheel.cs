using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// The hand cog of a turning room: a flat metal cog with a grip standing on its rim. Hold right mouse on it
    /// and the room turns one way for as long as the button is down, left mouse the other; let the button up and
    /// the room stays exactly where it is. There are no stops: the room's one entrance faces wherever it was left.
    ///
    /// <para>
    /// <b>One hand on it, decided by the server.</b> The hold is a claim on a <see cref="VehicleStation"/>: whoever
    /// has the grip reports which way they are turning, the deciding machine turns that into the room's angle and
    /// tells everyone, and every other machine swings its copy of the room after it. The holder's own machine does
    /// not wait for that answer: it turns its room from its own button, and is put right when they let go.
    /// </para>
    /// <para>
    /// The cog is fixed and the floor turns under it, so whoever holds it is kept where they stand and the room's
    /// carrier is told to leave them: they watch the entrance come round.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopWheel : VehicleStation, ISecondaryInteractable, IInteractionReadout
    {
        [Tooltip("Names this wheel's room in a save. Written by the prefab builder from the model's own name for it.")]
        [SerializeField] private string id;
        [SerializeField] private WorkshopTurntable room;
        [Tooltip("The room's carrier: told to leave whoever has the wheel where they stand.")]
        [SerializeField] private WalkerPlatformCarrier carrier;

        [Header("Feel")]
        [Tooltip("Degrees a second the room turns with a button held.")]
        [SerializeField, Min(0.1f)] private float degreesPerSecond = 12f;
        [Tooltip("Seconds from still to full speed, and back.")]
        [SerializeField, Min(0.01f)] private float rampSeconds = 0.4f;
        [Tooltip("Metres across the floor from the wheel its steersman may stand to take it; at twice this it lets go of them.")]
        [SerializeField, Min(0.5f)] private float reach = 2f;

        private const string HintId = "workshop-wheel";
        private const float HintSeconds = 3f;

        private readonly StationFooting footing = new();
        private Rigidbody steersman;
        private float angle;
        private float steer;
        private int way;
        private bool heard;

        public string Id => id;

        /// <summary>Degrees the room has turned from the pose it is modelled in. Not wrapped: it counts turns.</summary>
        public float Angle => angle;

        // ── VehicleStation ───────────────────────────────────────────────────

        protected override bool Exclusive => true;

        protected override float ValueDeadband => 0.01f;

        public override bool CanInteract() => room != null;

        /// <summary>Right mouse: turn the room its positive way for as long as the button is down.</summary>
        public override void Interact(Interactor interactor) => Take(interactor, 1);

        public bool CanSecondaryInteract() => CanInteract();

        /// <summary>Left mouse: the other way.</summary>
        public void SecondaryInteract(Interactor interactor) => Take(interactor, -1);

        private void Take(Interactor interactor, int direction)
        {
            if (room == null || IsManned) return;

            // The crosshair reaches further than an arm does, and the cog keeps whoever holds it where they stand.
            GameObject player = ResolvePlayer(interactor);
            if (!Near(player))
            {
                PlayerHints.Show(HintId, "Step up to the hand cog", HintSeconds);
                return;
            }

            way = direction;
            RequestClaim(interactor);
        }

        /// <summary>The deciding machine's own check of the same thing: nobody takes the wheel from across the room.</summary>
        protected override bool CanBeManned(GameObject player) => Near(player);

        private bool Near(GameObject player)
        {
            if (player == null) return false;

            Vector3 away = player.transform.position - transform.position;
            return new Vector2(away.x, away.z).sqrMagnitude <= reach * reach;
        }

        protected override float LocalRequest() => steer;

        protected override float ReadValue() => angle;

        /// <summary>The deciding machine: its own keys when the steersman is local, the reported ones otherwise.</summary>
        protected override float AdvanceOnServer(float wanted, float deltaTime)
        {
            Turn(IsMannedByLocalPlayer ? steer : Mathf.Clamp(wanted, -1f, 1f), deltaTime);
            return angle;
        }

        /// <summary>Everybody but the steersman: the room goes where the deciding machine says it is.</summary>
        protected override void ApplyValue(float position)
        {
            angle = position;
            room.Follow(angle, instant: !heard);
            heard = true;
        }

        protected override bool ShouldRelease(GameObject player) =>
            player == null || !player.activeInHierarchy ||
            (player.transform.position - transform.position).sqrMagnitude > 4f * reach * reach;

        protected override void OnManned(GameObject player)
        {
            steersman = player.GetComponent<Rigidbody>();
            if (carrier != null) carrier.ClaimRider(steersman);

            if (Network.Owns(player.transform)) footing.Begin(player, transform, reach, null);
        }

        protected override void OnUnmanned(GameObject player)
        {
            if (carrier != null) carrier.ReleaseRider(steersman);
            steersman = null;
            steer = 0f;
            way = 0;
            footing.End();
        }

        // ── Turning ──────────────────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();

            // The grip is held by the button that took it. The claim can land after the button is already up
            // again (a click, or a slow answer from the server), and then this lets go at once.
            bool mine = IsMannedByLocalPlayer;
            bool held = mine && way != 0 && (way > 0 ? footing.InteractHeld : footing.UseHeld);
            if (mine && !held && way != 0)
            {
                way = 0;
                RequestRelease(Occupant);
            }

            steer = Mathf.MoveTowards(steer, held ? way : 0f, Time.deltaTime / rampSeconds);

            // A holder who is not the deciding machine turns their own room now and hears the truth later.
            if (mine && !Network.Simulates(this)) Turn(steer, Time.deltaTime);
        }

        private void LateUpdate() => footing.Hold();

        private void Turn(float by, float deltaTime)
        {
            if (room == null || Mathf.Approximately(by, 0f)) return;

            angle += by * degreesPerSecond * deltaTime;
            room.Follow(angle, instant: false);
        }

        // ── Persistence ──────────────────────────────────────────────────────

        /// <summary>
        /// A loaded save: the room lands where it was left. Joiners ask for it; a client already here hears it
        /// with the next claim on this wheel.
        /// </summary>
        public void Restore(float degrees)
        {
            angle = degrees;
            if (room != null) room.Follow(angle, instant: true);
        }

        // ── IInteractionReadout ──────────────────────────────────────────────

        public string Label => "Hand cog";

        public string Prompt => !IsManned
            ? "Hold RMB / LMB: turn the room"
            : IsMannedByLocalPlayer
                ? "Let go to stop"
                : "Somebody else has the grip";

        public float? Value01 => null;

        public string ValueText => string.Empty;
    }
}
