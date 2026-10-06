// Climbing a ledge, on the player's own body.
//
// Jump at a wall too tall to jump onto climbs it (GDC-L1-FEEL-0003: the intent, not a button): onto the
// top when there is one past the edge, over it when there is not. A grapple rope that is holding the
// player offers its own climb (see OfferRopeClimb), and LedgeAirGrab feeds mid-air grabs in through
// TryClimbFrom. What is climbable is LedgeProbe's call, so every way in agrees.
//
// The climb is short and committed (GDC-L1-FEEL-0008): move keys are ignored until it ends, look stays
// free. The movement sets the pace (pullSpeed, overSpeed) and the animation keeps up: the action is played
// fast or slow enough that its Grab mark lands as the body reaches the lip, so on every screen the hands
// meet the edge when the body does. The other way round — the clip's clock driving the body — made every
// climb as slow as its clip.
//
// Owner only, like LadderClimber. The player's NetworkTransform is owner-authoritative, so peers see the
// climb as the pose moving, and the action rides the network animator. Nothing is saved: a climb lasts
// about a second, and a player loaded mid-climb simply drops or climbs again.
using System;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Presentation;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Climbs the player onto or over a ledge in front of them. Takes the body off
    /// <see cref="PlayerMovement"/> with <see cref="PlayerMovement.SetClimbing"/> for the duration and
    /// hands it back on every way the climb ends, through <see cref="LetGo"/>.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMovement))]
    public class LedgeClimber : MonoBehaviour, ITeleportAware
    {
        [Header("Reach, metres above the feet")]
        [Tooltip("Lowest lip Jump climbs from the ground. Anything lower is a plain jump.")]
        [SerializeField, Min(0f)] private float minClimbHeight = 1f;

        [Tooltip("Highest lip Jump climbs from the ground: a jump plus arms' reach. A rope's edge is found " +
                 "at its hook instead (LedgeProbe's hook settings).")]
        [SerializeField, Min(0.1f)] private float maxReach = 4.5f;

        [SerializeField] private LedgeProbe probe = new LedgeProbe();

        [Header("Animation — each action's Grab mark is where the pull-up ends")]
        [Tooltip("Climb onto a top no higher than midEdgeHeight above the feet.")]
        [SerializeField] private CharacterAction lowClimbUpAction;

        [Tooltip("Climb onto a top between midEdgeHeight and highEdgeHeight above the feet.")]
        [SerializeField] private CharacterAction midClimbUpAction;

        [Tooltip("Climb onto a top higher than highEdgeHeight, or off a rope: hang, then pull up.")]
        [SerializeField] private CharacterAction highClimbUpAction;

        [Tooltip("Climb onto a top at a sprint, from the ground: run up the wall.")]
        [SerializeField] private CharacterAction runningClimbUpAction;

        [Tooltip("Vault over an edge no higher than midEdgeHeight above the feet.")]
        [SerializeField] private CharacterAction lowVaultAction;

        [Tooltip("Vault over a higher edge, or off a rope.")]
        [SerializeField] private CharacterAction highVaultAction;

        [Tooltip("Looped while a grapple holds the player hanging below an edge.")]
        [SerializeField] private CharacterAction hangAction;

        [Tooltip("Metres above the feet at which an edge stops using the low clips.")]
        [SerializeField, Min(0f)] private float midEdgeHeight = 2f;

        [Tooltip("Metres above the feet at which a climb up becomes a hang-and-pull-up.")]
        [SerializeField, Min(0f)] private float highEdgeHeight = 3.3f;

        [Tooltip("Slowest and fastest an action is played to keep up with the climb, as a multiple of its own speed.")]
        [SerializeField] private Vector2 animationSpeedRange = new Vector2(0.5f, 3f);

        [Header("Motion — the movement sets the pace, the animation keeps up")]
        [Tooltip("How fast the body rises to the lip, m/s.")]
        [SerializeField, Min(0.1f)] private float pullSpeed = 7f;

        [Tooltip("How fast the body goes over the lip, m/s.")]
        [SerializeField, Min(0.1f)] private float overSpeed = 5f;

        [Tooltip("Shortest either phase may be, seconds, so a tiny step still reads as a climb.")]
        [SerializeField, Min(0.01f)] private float minPhaseSeconds = 0.15f;

        [Tooltip("How hard the body is held on the axis it is not travelling along, per second.")]
        [SerializeField, Min(0f)] private float lineSnap = 12f;

        [Tooltip("Forward speed a vault lets go with, m/s.")]
        [SerializeField, Min(0f)] private float vaultExitSpeed = 4f;

        [Tooltip("Fastest the body is moved during a climb, m/s. Above pullSpeed and overSpeed, so it only " +
                 "bounds the catch-up of a phase that fell behind.")]
        [SerializeField, Min(0.1f)] private float maxClimbSpeed = 12f;

        [Tooltip("Metres from a phase's target that count as there.")]
        [SerializeField, Min(0.001f)] private float arriveTolerance = 0.05f;

        [Tooltip("Slowest progress toward the target, m/s, that is not a stall.")]
        [SerializeField, Min(0f)] private float stallSpeed = 0.25f;

        [Tooltip("Seconds of stalled progress before the climb gives up and hands the body back.")]
        [SerializeField, Min(0.05f)] private float stallTimeout = 0.4f;

        private enum Phase { None, Pull, Over }

        private PlayerMovement movement;
        private PlayerController controller;
        private PlayerStance stance;
        private PlayerRagdoll ragdoll;
        private CharacterActions actions;
        private Rigidbody body;

        private Phase phase;
        private Ledge ledge;
        private float phaseTime;
        private float phaseDuration;
        private float overDuration;
        private float lastDistance;
        private float stallTime;
        private CharacterAction playing;
        private bool warnedMissingAction;

        private bool ropeOffered;
        private Vector3 ropeAnchor;
        private Vector3 ropeFacing;
        private Action onRopeClimb;

        public bool IsClimbing => phase != Phase.None;

        private Vector3 Feet => Shape.Feet;

        private PlayerBodyShape Shape => new PlayerBodyShape(movement.BodyCapsule, body);

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<PlayerController>();
            stance = GetComponent<PlayerStance>();
            ragdoll = GetComponent<PlayerRagdoll>();
            actions = GetComponent<CharacterActions>();
            body = GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            LetGo();
            WithdrawRopeClimb();
        }

        /// <summary>A teleport is never a climb: whatever moved the player took them off the ledge.</summary>
        public void OnTeleported(in TeleportMove move) => LetGo();

        // ── Ways in ────────────────────────────────────────────────────────────

        /// <summary>
        /// Jump's question, asked by <see cref="PlayerMovement.OnJump"/> before it jumps. Climbs from the
        /// rope when one is offering, from the ground otherwise. False leaves the press to the jump.
        /// </summary>
        public bool TryClimb()
        {
            if (ropeOffered)
            {
                if (!CanStart()) return false;
                return TryClimb(probe.FindAtHook(Shape, ropeAnchor, ropeFacing));
            }
            if (movement.IsTethered || !movement.IsOnGround) return false;
            return TryClimbFrom(transform.forward, minClimbHeight, maxReach);
        }

        /// <summary>
        /// Climb a ledge toward <paramref name="facing"/> whose lip is between <paramref name="minHeight"/>
        /// and <paramref name="reach"/> metres above the feet, if there is one and the body is free to.
        /// </summary>
        public bool TryClimbFrom(Vector3 facing, float minHeight, float reach)
        {
            // Before the feet are read: they come from the capsule, which CanStart checks is there.
            if (!CanStart()) return false;

            return TryClimb(probe.Find(Shape, Feet, facing, minHeight, reach));
        }

        private bool TryClimb(Ledge found)
        {
            if (!found.Found) return false;

            Begin(found);
            return true;
        }

        /// <summary>
        /// A rope is holding the player and may be climbed off, toward <paramref name="facing"/>, onto the
        /// edge it is hooked at <paramref name="anchor"/>. <paramref name="onClimb"/> is called once when such
        /// a climb starts, for the rope to let go. Call every physics step the rope holds; withdraw when it
        /// stops.
        /// </summary>
        public void OfferRopeClimb(Vector3 anchor, Vector3 facing, Action onClimb)
        {
            ropeOffered = true;
            ropeAnchor = anchor;
            ropeFacing = facing;
            onRopeClimb = onClimb;
        }

        public void WithdrawRopeClimb()
        {
            ropeOffered = false;
            onRopeClimb = null;
        }

        /// <summary>
        /// Whether a rope hooked at <paramref name="anchor"/> is hooked at an edge the hands can take,
        /// climbing toward <paramref name="facing"/>.
        /// </summary>
        public bool RopeLedgeInReach(Vector3 anchor, Vector3 facing) =>
            CanStart() && probe.FindAtHook(Shape, anchor, facing).Found;

        private bool CanStart()
        {
            if (!isActiveAndEnabled || phase != Phase.None || !Network.Owns(this)) return false;
            if (movement.BodyCapsule == null || !BodyIsOurs()) return false;
            if (movement.IsClimbing || movement.IsGliding || movement.IsBouncing) return false;

            // A crouched capsule is short, so "a standing body fits on top" would be asked of the wrong body.
            if (stance != null && stance.IsCrouching) return false;

            // The ladder owns Jump inside its volume.
            return Ladder.At(Feet) == null;
        }

        private bool BodyIsOurs() =>
            movement.isActiveAndEnabled && !body.isKinematic &&
            (controller == null || !controller.IsDead) &&
            (ragdoll == null || !ragdoll.IsHeldOrDown);

        // ── The climb ──────────────────────────────────────────────────────────

        private void Begin(Ledge found)
        {
            ledge = found;
            // Through CarriedBody, not a private flag: a mount taken mid-climb banks the weightless state
            // as the body's normal one if two owners each remember useGravity themselves.
            CarriedBody.SuspendGravity(gameObject, this);
            body.linearVelocity = Vector3.zero;
            movement.SetClimbing(true);

            Vector3 feet = Feet;
            float pull = Mathf.Max(minPhaseSeconds, Vector3.Distance(feet, found.Hang) / pullSpeed);
            float overFlat = new Vector2(found.Landing.x - found.Hang.x, found.Landing.z - found.Hang.z).magnitude;
            overDuration = Mathf.Max(minPhaseSeconds, overFlat / overSpeed);
            PlayAction(ActionFor(found.Kind, found.Hang.y - feet.y, fromRope: onRopeClimb != null), pull);
            StartPhase(Phase.Pull, pull);

            // Last, so the rope lets go of a body that is already climbing.
            Action letGoOfRope = onRopeClimb;
            WithdrawRopeClimb();
            letGoOfRope?.Invoke();
        }

        /// <summary>
        /// The action for a climb of <paramref name="kind"/> whose lip is <paramref name="rise"/> above the
        /// feet. Off a rope the body is already hanging, so it is the hang-and-pull-up whatever the height;
        /// at a sprint from the ground it runs up the wall.
        /// </summary>
        private CharacterAction ActionFor(LedgeKind kind, float rise, bool fromRope)
        {
            if (kind == LedgeKind.Vault) return fromRope || rise > midEdgeHeight ? highVaultAction : lowVaultAction;
            if (fromRope || rise > highEdgeHeight) return highClimbUpAction;
            if (stance != null && stance.IsSprinting && runningClimbUpAction != null) return runningClimbUpAction;
            return rise > midEdgeHeight ? midClimbUpAction : lowClimbUpAction;
        }

        /// <summary>
        /// Play <paramref name="action"/> sped or slowed so its Grab mark lands as the pull-up of
        /// <paramref name="pull"/> seconds reaches the lip: the movement sets the pace and the animation
        /// keeps up, or a long clip would make every climb as slow as the clip.
        /// </summary>
        private void PlayAction(CharacterAction action, float pull)
        {
            if (action == null || actions == null)
            {
                WarnMissingAction(action == null ? "no action is assigned for it" : "the player has no CharacterActions");
                return;
            }

            int variant = actions.PickVariant(action);
            float toGrab = action.SecondsTo(CharacterAction.Mark.Grab, variant, actions.PlaybackSpeed(action));
            if (toGrab <= 0f) WarnMissingAction($"'{action.name}' has no Grab mark");

            float scale = toGrab > 0f
                ? Mathf.Clamp(toGrab / pull, animationSpeedRange.x, animationSpeedRange.y)
                : 1f;
            if (actions.Play(action, null, variant, scale)) playing = action;
        }

        private void WarnMissingAction(string why)
        {
            if (warnedMissingAction) return;
            warnedMissingAction = true;
            Debug.LogWarning($"[LedgeClimber] Climbing without a matching animation because {why}. Assign the " +
                             "climb, vault and hang actions on the player prefab's LedgeClimber.", this);
        }

        /// <summary>
        /// A grapple has the player hanging below an edge, or has stopped holding them there: loops the hang
        /// action while it does. A climb's own action replaces it on the same slot.
        /// </summary>
        public void SetHanging(bool hanging)
        {
            if (actions == null || hangAction == null || !Network.Owns(this)) return;

            if (hanging) actions.Play(hangAction);
            else actions.Stop(hangAction);
        }

        private void StartPhase(Phase next, float duration)
        {
            phase = next;
            phaseTime = 0f;
            phaseDuration = duration;
            stallTime = 0f;
            lastDistance = float.PositiveInfinity;
        }

        private void FixedUpdate()
        {
            if (phase == Phase.None) return;

            if (!BodyIsOurs())
            {
                LetGo();
                return;
            }

            float dt = Time.fixedDeltaTime;
            phaseTime += dt;

            Vector3 feet = Feet;
            Vector3 target = phase == Phase.Pull ? ledge.Hang : ledge.Landing;
            Vector3 to = target - feet;
            Vector3 flat = new Vector3(to.x, 0f, to.z);

            bool arrived = phase == Phase.Pull ? to.y <= arriveTolerance : flat.magnitude <= arriveTolerance;
            if (arrived)
            {
                if (phase == Phase.Pull) StartPhase(Phase.Over, overDuration);
                else Finish();
                return;
            }

            float distance = to.magnitude;
            stallTime = lastDistance - distance >= stallSpeed * dt ? 0f : stallTime + dt;
            lastDistance = distance;
            if (stallTime > stallTimeout)
            {
                LetGo();
                return;
            }

            // Travel along one axis on the phase's clock, hold the other one on its line.
            float remaining = Mathf.Max(phaseDuration - phaseTime, dt);
            Vector3 steer = phase == Phase.Pull
                ? flat * lineSnap + Vector3.up * (to.y / remaining)
                : flat / remaining + Vector3.up * (to.y * lineSnap);
            body.linearVelocity = Vector3.ClampMagnitude(steer, maxClimbSpeed);
        }

        private void Finish()
        {
            bool vault = ledge.Kind == LedgeKind.Vault;
            Vector3 exit = ledge.Facing * vaultExitSpeed;

            // Played to its end: nothing to cut short.
            playing = null;
            LetGo();

            // After the hand-back, which gives back only gravity and leaves the climb's steering speed
            // on the body: a climb up ends standing still, a vault goes on over.
            if (!vault)
            {
                body.linearVelocity = Vector3.zero;
                return;
            }
            body.linearVelocity = exit;
            // Otherwise air control confiscates the exit inside a fifth of a second.
            movement.CarryMomentum();
        }

        /// <summary>Hand the body back. Safe to call when not climbing.</summary>
        public void LetGo()
        {
            if (phase == Phase.None) return;

            phase = Phase.None;
            CarriedBody.Release(gameObject, this);
            if (movement != null) movement.SetClimbing(false);

            if (playing != null && actions != null) actions.Stop(playing);
            playing = null;
        }
    }
}
