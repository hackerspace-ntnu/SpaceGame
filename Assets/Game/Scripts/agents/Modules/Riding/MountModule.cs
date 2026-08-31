// Single-component mount lifecycle owner. Absorbs what used to be three classes:
// MountController (rider attach/detach + rider state), MountInteractor (IInteractable surface),
// and MountSuppressorModule (disables other modules while mounted).
//
// A fully mountable entity now needs just two modules:
//   • MountModule (this — lifecycle, state, interaction surface, AI suppression)
//   • SteerModule (rider input → movement, camera, jump, leap)
// Plus the usual AgentController + IMovementMotor if you want AI in the downtime between inputs.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Agents
{
    // Late execution order so our LateUpdate runs after any other LateUpdate in the scene —
    // guarantees the mounted third-person camera transform isn't overwritten afterwards.
    //
    // IPersistentEntity: a mount is a world object even when nothing else about it qualifies. The
    // Ostrich has a kinematic Rigidbody, no NavMeshAgent and no HealthComponent, so before this it
    // was invisible to the save system entirely. MountSaveable is added from here by SaveablePolicy.
    [DefaultExecutionOrder(1000)]
    public partial class MountModule : BehaviourModuleBase, IInteractable, IPersistentEntity
    {
        public enum CameraPerspective
        {
            FirstPerson,
            ThirdPerson
        }

        [Header("Mount Points")]
        [SerializeField] private Transform seatPoint;
        [Tooltip("How deep the rider sits into the seat point, in the seat point's local space. " +
                 "A player's transform origin is at their FEET, so with the default zero the feet " +
                 "land on the seat and the body stands above it — fine for a deck, wrong for a " +
                 "saddle. Push this down by roughly the rider's leg length to seat them by the " +
                 "pelvis instead. Kept separate from the seat point's own position so the marker " +
                 "can stay where the saddle actually is.")]
        [SerializeField] private Vector3 seatOffset = Vector3.zero;
        [SerializeField] private Transform dismountPoint;

        [Header("Interaction")]
        [Tooltip("Allow mounting by interacting with this entity's own colliders. Turn off for large " +
                 "vehicles that should only be boarded from a dedicated control — Interactor resolves " +
                 "IInteractable by walking up from the collider it hit, so otherwise every hull collider " +
                 "becomes a mount point. Use a MountStation on the cockpit control instead.")]
        [SerializeField] private bool mountableByDirectInteraction = true;

        [Header("Player Components To Toggle")]
        [SerializeField] private bool disablePlayerMovement = true;
        [SerializeField] private bool disablePlayerLook = true;
        [SerializeField] private bool disablePlayerInteractor = true;

        [Header("Dismount")]
        [SerializeField] private float mountCooldown = 0.25f;
        [SerializeField] private float fallbackDismountDistance = 1.6f;

        [Header("Mounted Camera")]
        [SerializeField] private CameraPerspective defaultPerspective = CameraPerspective.ThirdPerson;
        [Tooltip("Prefab spawned and parented to the mount when entering third-person view. Falls back " +
                 "to a clone of Camera.main if null. Leave default unless this vehicle needs custom render settings.")]
        [SerializeField] private Camera thirdPersonCameraPrefab;
        [SerializeField] private Transform thirdPersonPivot;
        [Tooltip("Boom offset from the pivot. X biases the camera off the mount's centreline " +
                 "(over-the-shoulder), Y raises it, Z only picks which side of the pivot it sits " +
                 "on — the length comes from thirdPersonDistance. Defaults are a riding framing: " +
                 "close, roughly level with the rider's head, a few degrees down.")]
        [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0.35f, 1.7f, -1f);
        [SerializeField] private float thirdPersonDistance = 4.5f;
        [SerializeField] private float thirdPersonFollowLerp = 14f;
        [Tooltip("How fast the camera's aim catches up. Slightly below thirdPersonFollowLerp so the " +
                 "framing settles after the position does rather than fighting it.")]
        [SerializeField] private float thirdPersonAimLerp = 18f;
        [Tooltip("How fast the camera's orbit yaw follows the vehicle's heading. Lower = the vehicle " +
                 "can rotate a little within frame before the camera swings round behind it.")]
        [SerializeField] private float thirdPersonYawLerp = 16f;
        [Tooltip("Meters ahead of the pivot the camera aims at. The downward angle works out as " +
                 "atan(offset.y / (distance + this)), so RAISING this FLATTENS the view — it does " +
                 "not tilt further down.")]
        [SerializeField] private float thirdPersonLookAhead = 7f;
        [Tooltip("Orbit the camera on the mount's FULL rotation rather than its yaw alone. Off for " +
                 "ground vehicles, which stay level and want a level horizon however the hull tilts. " +
                 "On for anything that pitches or rolls in flight — otherwise the camera stays level " +
                 "through a dive and the manoeuvre reads as the ground rising rather than the pilot " +
                 "pitching over.")]
        [SerializeField] private bool followMountPitch = false;

        [Header("Mounted Look")]
        [SerializeField] private string lookActionName = "Look";
        [Tooltip("Match PlayerLook's sensitivity on the player prefab (20) unless this mount wants " +
                 "a deliberately heavier or lighter view. This sat at 1 against PlayerLook's 20, " +
                 "which made the mounted camera twenty times slower than the on-foot one — the " +
                 "whole reason looking around while riding felt like dragging something heavy.")]
        [SerializeField] private float lookSensitivity = 20f;
        [Tooltip("Pitch limit for the FIRST-PERSON head. The third-person boom has its own, below.")]
        [SerializeField] private float lookPitchClamp = 75f;
        [SerializeField] private float defaultMountedPitch = -15f;
        [Tooltip("Lowest the third-person boom swings, in degrees. Negative drops the camera and " +
                 "looks up at the mount; going much past this digs it into the ground.")]
        [SerializeField] private float orbitPitchMin = -25f;
        [Tooltip("Highest the third-person boom swings. Positive lifts the camera into a view " +
                 "down over the mount.")]
        [SerializeField] private float orbitPitchMax = 60f;
        [Tooltip("Degrees per second the view drifts back behind the mount once the rider has " +
                 "stopped looking around. Deliberately slow: a rider who parks the camera out on " +
                 "the mount's flank to watch it run should keep that view for as long as they " +
                 "want it, and get it back to normal without having to steer the camera home.")]
        [SerializeField] private float cameraAutoAlignSpeed = 8f;
        [Tooltip("Seconds of no look input before that drift starts.")]
        [SerializeField] private float cameraAutoAlignDelay = 3f;

        [Header("While Mounted")]
        [Tooltip("If true, the mount keeps running its own AI modules (wander, patrol, etc.) between rider inputs. " +
                 "If false, all non-mount modules are disabled while mounted — the mount stands still when the rider isn't steering.")]
        [SerializeField] private bool allowAISelfMovementWhenMounted = false;

        // Camera / look runtime state
        private InputAction lookAction;
        private bool forcedLookActionEnabled;
        private Camera runtimeThirdPersonCamera;
        private bool thirdPersonCameraNeedsSnap;
        private float mountedPitch;
        // Third-person boom elevation. Separate from mountedPitch because the two have different
        // neutrals (the head rests at defaultMountedPitch, the boom rests at whatever
        // thirdPersonOffset already describes) and different limits.
        private float orbitPitch;
        private float cameraYaw;
        private float cameraYawOffset;
        // Aim target is smoothed in world space and persists between frames, so the camera's
        // rotation is driven by a filtered point rather than recomputed from raw vehicle pose.
        private Vector3 smoothedAimPoint;
        private float timeSinceLastLookInput;
        private CameraPerspective activePerspective;

        // Rider state
        private Transform mountedPlayer;
        private PlayerMovement mountedPlayerMovement;
        private PlayerLook mountedPlayerLook;
        private Interactor mountedInteractor;
        private Rigidbody mountedPlayerRigidbody;

        // Held so the death subscription can be undone on the exact instance it was made against,
        // even once mountedPlayer has been cleared.
        private SpaceGame.Gameplay.HealthComponent mountedRiderHealth;

        /// <summary>Set for the duration of a dismount, so a listener calling back in is ignored.</summary>
        private bool dismounting;

        /// <summary>
        /// Where the next dismount should put the rider, when something other than the mount has
        /// worked it out. Set by <see cref="DismountAt"/> and cleared by the dismount that consumes
        /// it. Null on every ordinary dismount, which is the dismount point's business as before.
        /// </summary>
        private Vector3? dismountPositionOverride;

        /// <summary>
        /// Where the last dismount actually put the rider, and whether there has been one.
        ///
        /// Read by <see cref="MountNetworkSync"/> so the position can travel with the announcement
        /// instead of every peer guessing at it. Survives the dismount that set it — the peers are
        /// told a tick later, once the seat is observed to be empty.
        /// </summary>
        private Vector3 lastDismountPosition;
        private bool hasLastDismountPosition;
        private bool playerRigidbodyWasKinematic;
        private bool playerRigidbodyHadGravity;
        private RigidbodyInterpolation playerRigidbodyInterpolation;

        // The rider's own control components as they were the moment they sat down, so the dismount
        // hands back what it took rather than switching everything on.
        //
        // The difference is the whole multiplayer story of this class. MountNetworkSync replays a
        // peer's mount and dismount here, so a client runs this against SOMEBODY ELSE'S player —
        // a body whose PlayerMovement, PlayerLook and Interactor are off because
        // PlayerController.DisablePlayer switched them off on every machine that does not own it.
        // Restoring to `true` woke them up on the wrong machine: PlayerLook.LateUpdate then re-locks
        // this machine's cursor every frame (the death screen loses its pointer, so Respawn cannot
        // be clicked) and PlayerMovement.FixedUpdate writes velocity into a remote body netcode
        // keeps kinematic, once per physics step, for the rest of the session.
        private bool riderMovementWasEnabled;
        private bool riderLookWasEnabled;
        private bool riderInteractorWasEnabled;
        private float lastMountChangeTime;

        private Transform activeSeatPoint;
        private Camera mountedFirstPersonCamera;
        private Transform mountedFirstPersonCameraRoot;

        private MonoBehaviour[] suppressibleModules;

        // Animator state captured at mount time so root-motion-driven drift is suppressed while
        // ridden and restored on dismount.
        private Animator[] suppressibleAnimators;
        private bool[] suppressibleAnimatorRootMotion;

        // Rigidbody constraints captured at mount time so physics can't spin the mount via
        // contact forces (notably the rider's own collider overlapping the seat point).
        private Rigidbody ownRigidbody;
        private RigidbodyConstraints ownRigidbodyConstraints;
        private bool ownRigidbodyConstraintsCaptured;

        // Rider/mount collider pairs ignored while mounted so the rider's kinematic collider
        // doesn't push the mount around. Restored on dismount. The pairs live in a shared helper
        // because NpcPassenger seats a different kind of rider in this same saddle and needs the
        // identical suspension — and, just as importantly, must not reach for the other tool that
        // stops a rider shoving its mount, which is switching the rider's colliders off.
        private readonly RiderCollisionIgnore riderCollisions = new RiderCollisionIgnore();

        public event Action<PlayerMovement> Mounted;
        public event Action<PlayerMovement> Dismounted;

        // ─────────── Public API ───────────
        public bool IsMounted => mountedPlayer != null;

        /// <summary>
        /// Is the rider in this seat THIS machine's player?
        ///
        /// <para>
        /// The line between what a mount does for everybody — seating the rider, suppressing its
        /// own AI, ignoring the collision pairs, posing the legs — and the much smaller set it does
        /// for exactly one person: the cameras, the audio listener, the look input, the visor
        /// shader flag, and switching the rider's own control scripts off. Mounting replicates to
        /// every peer, so before this the second set ran on every machine in the session at once,
        /// which is how one player climbing onto an ostrich put a third-person camera on everybody
        /// else's screen.
        /// </para>
        /// <para>
        /// Asked of the RIDER, not of the mount. Mount ownership is handed to the rider as part of
        /// seating, and a peer can apply the mount before that transfer has reached it; a rider's
        /// ownership never changes at all. <see cref="Network.Owns"/> answers true offline and for
        /// an unnetworked rider, so single-player and un-networked mounts behave as they always did.
        /// </para>
        /// </summary>
        public bool RiderIsLocal => mountedPlayer != null && Network.Owns(mountedPlayer);
        public bool IsAvailableForMount => !IsMounted && Time.time >= lastMountChangeTime + mountCooldown;
        public bool AllowAISelfMovementWhenMounted => allowAISelfMovementWhenMounted;
        public bool MountableByDirectInteraction => mountableByDirectInteraction;
        public Transform ActiveSeatPoint => activeSeatPoint != null ? activeSeatPoint : seatPoint;
        public Transform MountedPlayerTransform => mountedPlayer;
        public PlayerMovement MountedPlayerMovement => mountedPlayerMovement;
        public PlayerLook MountedPlayerLook => mountedPlayerLook;
        public Interactor MountedInteractor => mountedInteractor;
        public Rigidbody MountedPlayerRigidbody => mountedPlayerRigidbody;
        public Camera MountedFirstPersonCamera => mountedFirstPersonCamera;
        public Transform MountedFirstPersonCameraRoot => mountedFirstPersonCameraRoot;
        public Camera MountedThirdPersonCamera => runtimeThirdPersonCamera;
        public CameraPerspective ActivePerspective => activePerspective;
        public float CameraYaw => cameraYaw;
        public float CameraYawOffset => cameraYawOffset;
        public float MountedPitch => mountedPitch;
        public float OrbitPitch => orbitPitch;
        public Vector3 SeatOffset => seatOffset;

        /// <summary>Where the last dismount left the rider. Only meaningful with <see cref="HasLastDismountPosition"/>.</summary>
        public Vector3 LastDismountPosition => lastDismountPosition;

        /// <summary>Whether a rider has ever been put down by this mount. False after AbandonRider, which places nobody.</summary>
        public bool HasLastDismountPosition => hasLastDismountPosition;

        public override string ModuleDescription =>
            "Mount lifecycle + interaction surface + AI suppression. Drop this + SteerModule to make anything mountable.\n\n" +
            "• Implements IInteractable — players mount by interacting.\n" +
            "• Fires Mounted/Dismounted events.\n" +
            "• When allowAISelfMovementWhenMounted = false, disables non-mount IBehaviourModules for the duration.";

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

        // ─────────── Lifecycle ───────────
        private void Awake()
        {
            if (!seatPoint)
                seatPoint = transform;
            activeSeatPoint = seatPoint;
            CacheSuppressibleModules();
        }

        private void OnEnable()
        {
            ResolveCameraInputActions();
        }

        private void OnDisable()
        {
            // OnDisable arrives for three different reasons and only one of them may dismount.
            // Dismount reparents the rider, and Unity refuses Transform.SetParent while the parent
            // GameObject is mid-activation-change — "Cannot set the parent of the GameObject 'X'
            // while activating or deactivating the parent GameObject 'Y'". Measured: the mount reads
            // activeInHierarchy = false both when it is SetActive(false) and when its scene is
            // unloading, and true when only this component was switched off. That flag IS the
            // condition Unity guards on, so it is the whole test — see MountTeardownTests.
            //
            // Returning to the main menu hits the scene-unload case: NetworkManager.Shutdown deliberately
            // skips deparenting children while shutting down (NetworkSpawnManager.OnDespawnObject),
            // so the rider is still parented and no longer spawned when the scene tears down — the
            // networked detach in UnparentRider is unavailable and the raw SetParent is illegal.
            if (IsMounted)
            {
                if (gameObject.activeInHierarchy)
                    Dismount();
                else
                    AbandonRider();
            }

            if (forcedLookActionEnabled && lookAction != null)
            {
                lookAction.Disable();
                forcedLookActionEnabled = false;
            }
        }

        private void Update()
        {
            // The look stick belongs to the person in the saddle. Read on a machine whose player is
            // standing somewhere else, it aims a camera they are not looking through, force-enables
            // their Look action while they are in a menu, and writes pitch onto the head of a
            // remote player whose aim already replicates through PlayerViewNetwork.
            if (!IsMounted || !RiderIsLocal)
                return;

            EnsureLookActionEnabled();
            HandleLookInput(Time.deltaTime);

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                RequestDismount();
        }

        /// <summary>
        /// Get up out of the seat.
        ///
        /// <para>
        /// Here rather than in SteerModule, where it used to live, because standing up belongs to
        /// the SEAT and not to the controls: only the helm has a SteerModule, so a passenger chair
        /// — every non-helm chair on the PlayerShip is its own MountModule with no steering — had
        /// nothing reading the key at all. It went unnoticed while a mount request reached every
        /// module on the hull, because sitting anywhere also mounted the helm, which then answered
        /// for everybody. See MountNetworkSync.MountIndex for the half of that this is the other
        /// side of.
        /// </para>
        /// <para>
        /// Through the sync when this mount is networked, or the rider stands up on their own
        /// screen and stays welded to the saddle on everyone else's.
        /// </para>
        /// </summary>
        private void RequestDismount()
        {
            if (TryGetComponent(out MountNetworkSync sync))
            {
                sync.RequestDismount();
                return;
            }

            Dismount();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            mountCooldown = Mathf.Max(0f, mountCooldown);
            fallbackDismountDistance = Mathf.Max(0.1f, fallbackDismountDistance);
            lookSensitivity = Mathf.Max(0f, lookSensitivity);
            lookPitchClamp = Mathf.Clamp(lookPitchClamp, 0f, 89f);
            orbitPitchMin = Mathf.Clamp(orbitPitchMin, -89f, 0f);
            orbitPitchMax = Mathf.Clamp(orbitPitchMax, 0f, 89f);
            thirdPersonDistance = Mathf.Max(0.1f, thirdPersonDistance);
            thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
            thirdPersonAimLerp = Mathf.Max(0.01f, thirdPersonAimLerp);
            thirdPersonYawLerp = Mathf.Max(0.01f, thirdPersonYawLerp);
            thirdPersonLookAhead = Mathf.Max(0.1f, thirdPersonLookAhead);
            cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
            cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
        }

        // MountModule never produces movement. Null → AgentController falls through to other modules
        // (or to MoveIntent.Idle() if none matched).
        public override MoveIntent? Tick(in AgentContext context, float deltaTime) => null;

        // ─────────── IInteractable ───────────
        // MountStation calls TryMount directly, so switching this off closes the "look at any part
        // of the hull and press E" path without disabling dedicated cockpit controls.
        public bool CanInteract() => mountableByDirectInteraction && IsAvailableForMount;

        public void Interact(Interactor interactor)
        {
            // Route through the network sync when this mount has one, so the server decides who gets
            // the seat. Falls back to a direct mount for un-networked mounts and offline play.
            if (TryGetComponent(out MountNetworkSync sync))
            {
                sync.RequestMount(interactor);
                return;
            }

            TryMount(interactor, transform);
        }

        // ─────────── Suppressor ───────────
        public void RefreshModuleCache() => CacheSuppressibleModules();

        private void CacheSuppressibleModules()
        {
            List<MonoBehaviour> list = new List<MonoBehaviour>();
            MonoBehaviour[] all = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour mb in all)
            {
                // Suppress anything that could produce movement or a MoveIntent while mounted:
                // IBehaviourModule (except Mount/Steer themselves) and legacy IAgentBrain fallbacks.
                // Without this, e.g. a legacy NpcBrain/EnemyBrain would keep feeding intents to the
                // motor and make the mount drift/circle while the rider is idle.
                if ((mb is IBehaviourModule || mb is IAgentBrain) && !IsMountAware(mb))
                    list.Add(mb);
            }
            suppressibleModules = list.ToArray();
        }

        // Modules that must keep running while mounted so the rider can actually drive.
        private static bool IsMountAware(MonoBehaviour mb)
        {
            return mb is MountModule || mb is SteerModule;
        }

        private void ApplyModuleSuppression()
        {
            if (allowAISelfMovementWhenMounted || suppressibleModules == null)
                return;
            foreach (MonoBehaviour mb in suppressibleModules)
                if (mb) mb.enabled = false;
        }

        private void RestoreModuleSuppression()
        {
            if (suppressibleModules == null)
                return;
            foreach (MonoBehaviour mb in suppressibleModules)
                if (mb) mb.enabled = true;
        }
    }
}
