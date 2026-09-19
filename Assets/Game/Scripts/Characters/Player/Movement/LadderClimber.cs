// Climbing a ladder, on the player's own body.
//
// Taking hold reads intent, not a dedicated button (GDC-L1-FEEL-0003): walk into a ladder's volume
// toward the rungs, or press Jump while standing in it. At the top, walk off the exit floor into the
// gap the rails leave -- or drop into it -- and the ladder catches you to climb down. On the ladder,
// forward or Jump held climbs; back climbs down; nothing held and the player slides slowly down, so letting go of the keys never
// strands anyone half-way up. Reaching the top steps the player off onto the ladder's exit floor
// without a separate "climb over" input -- and so does the head meeting the underside of that floor
// first, which on a 3 m body happens a full body-height below the step-off. A hard sideways push
// lets go.
//
// Owner only, like the wingsuit and the jetpack. The player's NetworkTransform is owner-authoritative,
// so the machine climbing is the machine whose pose is the truth, and peers see the climb as that
// pose moving; nothing about the climb is on the wire in its own right. Nothing is saved either: a
// player loaded mid-ladder is standing wherever they were and simply takes hold again.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Teleporting;
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Climbs the player up and down any <see cref="Ladder"/> they are at. Takes the body off
    /// <see cref="PlayerMovement"/> with <see cref="PlayerMovement.SetClimbing"/> for the duration and
    /// hands it back on every way off the ladder.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMovement))]
    public class LadderClimber : MonoBehaviour, ITeleportAware
    {
        [Header("Speeds, m/s")]
        [Tooltip("Climbing up, with forward or Jump held.")]
        [SerializeField, Min(0.1f)] private float climbSpeed = 3f;

        [Tooltip("Climbing down, with back held.")]
        [SerializeField, Min(0.1f)] private float descendSpeed = 3.5f;

        [Tooltip("Sliding down with nothing held. Slow enough to be a choice to wait, not a fall.")]
        [SerializeField, Min(0f)] private float slideSpeed = 0.8f;

        [Header("Taking hold")]
        [Tooltip("How squarely the move input must point at the rungs to take hold by walking in, " +
                 "as a dot product. 0.5 is within 60 degrees.")]
        [SerializeField, Range(0f, 1f)] private float grabAlignment = 0.5f;

        [Tooltip("Sideways input, 0 to 1, that lets go of the ladder when nothing forward or back is held.")]
        [SerializeField, Range(0f, 1f)] private float letGoStrafe = 0.8f;

        [Tooltip("Input below this is treated as nothing held.")]
        [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.1f;

        [Header("On the ladder")]
        [Tooltip("Metres from the rung line to the body's centre while climbing. At least the capsule " +
                 "radius, or the body rubs the rails.")]
        [SerializeField, Min(0f)] private float standoff = 0.65f;

        [Tooltip("How hard the body is pulled onto the climbing line, per second. High is snappy.")]
        [SerializeField, Min(0f)] private float lineSnap = 12f;

        [Tooltip("Metres above the exit floor the feet are set down, so the capsule does not start " +
                 "the step-off inside the deck.")]
        [SerializeField, Min(0f)] private float stepOffLift = 0.05f;

        [Tooltip("Metres above the ladder's foot at which a descent counts as having reached the bottom.")]
        [SerializeField, Min(0f)] private float bottomTolerance = 0.15f;

        [Tooltip("Metres below the step-off height the feet are put when taking hold from the top, so " +
                 "the climb does not step straight back off.")]
        [SerializeField, Min(0f)] private float topEntryDrop = 0.5f;

        [Tooltip("How thick a floor at the top of a ladder may be, in metres. A climber whose head is " +
                 "blocked while the step-off is within a body height plus this steps off over it " +
                 "rather than stalling under its lip.")]
        [SerializeField, Min(0f)] private float mantleReach = 0.5f;

        [Tooltip("Extra metres the upward check looks past this step's climb, so contact is caught " +
                 "before the body is pressed into the underside.")]
        [SerializeField, Min(0f)] private float headSkin = 0.05f;

        private PlayerMovement movement;
        private Rigidbody body;
        private PlayerInputManager inputs;
        private Ladder ladder;
        private bool gravityBeforeClimb;
        private bool jumpPressed;

        /// <summary>The ladder being climbed, or null.</summary>
        public Ladder Climbing => ladder;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            body = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            inputs.OnJumpPressed += OnJumpPressed;
        }

        private void OnDestroy()
        {
            if (inputs != null) inputs.OnJumpPressed -= OnJumpPressed;
        }

        private void OnDisable() => LetGo();

        // Latched until the next physics step: a press between two steps must not be missed.
        private void OnJumpPressed() => jumpPressed = true;

        private void FixedUpdate()
        {
            bool jumped = jumpPressed;
            jumpPressed = false;

            if (inputs == null || movement.BodyCapsule == null || !Network.Owns(this)) return;

            Vector3 feet = Feet();
            if (ladder == null)
            {
                TryTakeHold(feet, jumped);
                return;
            }

            Climb(feet);
        }

        private void TryTakeHold(Vector3 feet, bool jumped)
        {
            if (movement.IsGliding || movement.IsTethered) return;

            Ladder at = Ladder.At(feet);
            if (at != null)
            {
                bool walkingIn = inputs.MoveInput.y > inputDeadZone &&
                                 Vector3.Dot(movement.WishDirection, -at.TowardClimber) >= grabAlignment;
                if (walkingIn || jumped) TakeHold(at);
                return;
            }

            Ladder below = Ladder.AtTop(feet);
            if (below == null) return;

            // From the top the intent is walking toward the climber's side, over the gap -- or
            // having already stepped into it, which is a fall the ladder should catch.
            bool walkingOver = inputs.MoveInput.y > inputDeadZone &&
                               Vector3.Dot(movement.WishDirection, below.TowardClimber) >= grabAlignment;
            // Only once the feet are past the rung line, over the gap: a jump on the exit floor beside
            // the ladder comes down inside the band too, and must land where it was aimed.
            bool overGap = Vector3.Dot(feet - below.Foot, below.TowardClimber) > 0f;
            bool dropping = overGap && !movement.IsOnGround && body.linearVelocity.y < 0f;
            if (!walkingOver && !dropping) return;

            // Just under the top, unless the floor there is over the body's head -- then a body height
            // down, the way the climb-over came up.
            Vector3 onLine = below.Foot + below.TowardClimber * standoff;
            onLine.y = below.TopHeight - topEntryDrop;
            if (BodyBlockedAt(onLine))
                onLine.y = Mathf.Max(below.Foot.y, below.TopHeight - BodyHeight - mantleReach);
            body.position += onLine - feet;
            body.linearVelocity = Vector3.zero;
            TakeHold(below);
        }

        private void TakeHold(Ladder at)
        {
            ladder = at;
            gravityBeforeClimb = body.useGravity;
            body.useGravity = false;
            movement.SetClimbing(true);
        }

        private void Climb(Vector3 feet)
        {
            Vector2 move = inputs.MoveInput;
            bool up = move.y > inputDeadZone || inputs.JumpHeld;
            bool down = move.y < -inputDeadZone;

            if (!up && !down && Mathf.Abs(move.x) >= letGoStrafe)
            {
                LetGo();
                return;
            }

            // Whatever is held: a climber who lets go at the very top belongs on the floor there,
            // not sliding back down a shaft they had already climbed.
            if (feet.y >= ladder.TopHeight)
            {
                StepOff(feet);
                return;
            }

            if (up && ladder.TopHeight - feet.y <= BodyHeight + mantleReach && BlockedAbove())
            {
                StepOff(feet);
                return;
            }

            float vertical = up ? climbSpeed : down ? -descendSpeed : -slideSpeed;
            if (vertical < 0f && feet.y <= ladder.Foot.y + bottomTolerance)
            {
                LetGo();
                return;
            }

            // Anything that shoves the climber clear of the ladder ends the climb rather than
            // dragging them back to it through whatever did the shoving.
            if (!ladder.Contains(feet))
            {
                LetGo();
                return;
            }

            Vector3 line = ladder.Foot + ladder.TowardClimber * standoff;
            Vector3 toLine = line - feet;
            toLine.y = 0f;
            body.linearVelocity = toLine * lineSnap + Vector3.up * vertical;
        }

        private void StepOff(Vector3 feet)
        {
            body.position += ladder.ExitPoint + Vector3.up * stepOffLift - feet;
            body.linearVelocity = Vector3.zero;
            LetGo();
        }

        /// <summary>Hand the body back. Safe to call when not climbing.</summary>
        public void LetGo()
        {
            if (ladder == null) return;

            ladder = null;
            if (body != null) body.useGravity = gravityBeforeClimb;
            if (movement != null) movement.SetClimbing(false);
        }

        /// <summary>A teleport is never a climb: whatever moved the player took them off the ladder.</summary>
        public void OnTeleported(in TeleportMove move) => LetGo();

        /// <summary>The body's world height: the capsule is authored 2 m on a transform stretched to 3.</summary>
        private float BodyHeight => movement.BodyCapsule.height * movement.BodyCapsule.transform.lossyScale.y;

        /// <summary>
        /// Whether this step's climb would put the head into something. The player's own colliders
        /// (hitboxes, the ragdoll) ride the same rigidbody and are skipped.
        /// </summary>
        private bool BlockedAbove()
        {
            BodyAt(Feet(), out Vector3 low, out Vector3 high, out float radius);
            float distance = climbSpeed * Time.fixedDeltaTime + headSkin;

            foreach (RaycastHit hit in Physics.CapsuleCastAll(low, high, radius, Vector3.up, distance,
                                                             Physics.DefaultRaycastLayers,
                                                             QueryTriggerInteraction.Ignore))
                // Distance 0 is a collider the body already touches -- the deck under the feet at the
                // bottom, a rail brushed on the way up -- not something the head is climbing into.
                if (hit.rigidbody != body && hit.distance > 0f && hit.point.y >= high.y)
                    return true;
            return false;
        }

        /// <summary>Whether the body standing with its feet at <paramref name="feet"/> would be inside something.</summary>
        private bool BodyBlockedAt(Vector3 feet)
        {
            BodyAt(feet, out Vector3 low, out Vector3 high, out float radius);
            foreach (Collider c in Physics.OverlapCapsule(low, high, radius, Physics.DefaultRaycastLayers,
                                                          QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != body)
                    return true;
            return false;
        }

        /// <summary>The body's capsule, in world space, with its feet at <paramref name="feet"/>.</summary>
        private void BodyAt(Vector3 feet, out Vector3 low, out Vector3 high, out float radius)
        {
            CapsuleCollider capsule = movement.BodyCapsule;
            Vector3 scale = capsule.transform.lossyScale;
            radius = capsule.radius * Mathf.Max(scale.x, scale.z);
            low = feet + Vector3.up * radius;
            high = feet + Vector3.up * Mathf.Max(radius, BodyHeight - radius);
        }

        private Vector3 Feet()
        {
            Bounds bounds = movement.BodyCapsule.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
    }
}
