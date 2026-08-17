using System;
using System.Collections;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;

namespace SpaceGame.Characters
{
    public class PlayerController : MonoBehaviour
    {
        public PlayerInputManager Input   { get; private set; }
        public IPlayerInventory PlayerInventory {get; private set; }
    
        [SerializeField] private GameObject playerCamera;
        [SerializeField] private GameObject playerHUD;
    
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerLook playerLook;
        [SerializeField] private DamageFeedback damageFeedback;
    
        [SerializeField] private HealthComponent playerHealth;
    
        // High level player events
        public event Action OnPlayerDeath;

        // Death outranks every other control owner. Cutscene mode, mounting and the spectator
        // camera all restore component-enabled flags they captured earlier, so a dead player whose
        // freeze lived only in those flags gets control handed back the moment one of them exits.
        // This flag is the authority; the enabled flags are just its current expression.
        private bool isDead;
        public bool IsDead => isDead;


        private void Awake()
        {
            Input = GetComponent<PlayerInputManager>();
            PlayerInventory = GetComponent<IPlayerInventory>();

            DisablePlayer();
            if (!Network.IsNetworked)
            {
                EnablePlayer();
            }
        }

        private void Start()
        {
            var streamer = FindFirstObjectByType<WorldStreamer>();
            if (streamer == null) return;

            streamer.RegisterTrackedTransform(transform);

            if (!Network.IsNetworked)
                StartCoroutine(WaitForStreamerThenPreload(streamer));
        }

        private IEnumerator WaitForStreamerThenPreload(WorldStreamer streamer)
        {
            while (!streamer.IsReady)
                yield return null;

            streamer.PreloadChunksAroundPosition(transform.position);
        }

        private void OnDestroy()
        {
            var streamer = FindFirstObjectByType<WorldStreamer>();
            if (streamer != null)
                streamer.UnregisterTrackedTransform(transform);
        }
        public void EnablePlayer()
        {
            playerCamera.gameObject.SetActive(true);
            playerHUD.gameObject.SetActive(true);
            damageFeedback.enabled = true;

            // Subscribe exactly once. Awake calls DisablePlayer then EnablePlayer, and the network
            // path enables again on ownership, so a bare += lands OnDeath on the delegate twice and
            // runs the whole death sequence per hit.
            playerHealth.OnDeath -= OnDeath;
            playerHealth.OnRevive -= OnRevive;
            playerHealth.OnDeath += OnDeath;
            playerHealth.OnRevive += OnRevive;

            // Enabling a player who is still dead (network ownership arriving after death, a scene
            // handover mid-death-screen) must not undo the freeze.
            if (isDead)
            {
                ApplyDeathFreeze();
                return;
            }

            Input.enabled = true;
            playerMovement.enabled = true;
            playerLook.enabled = true;
        }

        public void DisablePlayer()
        {
            Input.enabled = false;
            playerCamera.gameObject.SetActive(false);
            playerHUD.gameObject.SetActive(false);
            playerMovement.enabled = false;
            playerLook.enabled = false;
            damageFeedback.enabled = false;

            playerHealth.OnDeath -= OnDeath;
            playerHealth.OnRevive -= OnRevive;
        }

        public Camera PlayerCamera => playerCamera != null ? playerCamera.GetComponent<Camera>() : null;
        public Transform PlayerCameraTransform => playerCamera != null ? playerCamera.transform : null;

        // Cutscene handover: lock input/look/movement but keep the camera GameObject active so a
        // cutscene can drive its transform. Prior enabled-state is captured so the same call is
        // safe whether the player is on foot, mounted, or already had something disabled.
        private bool inCutsceneMode;
        private bool savedInputEnabled;
        private bool savedMovementEnabled;
        private bool savedLookEnabled;
        private bool savedDamageFeedbackEnabled;
        private bool savedHudActive;

        public bool InCutsceneMode => inCutsceneMode;

        public void EnterCutsceneMode()
        {
            if (inCutsceneMode) return;
            inCutsceneMode = true;

            savedInputEnabled = Input.enabled;
            savedMovementEnabled = playerMovement.enabled;
            savedLookEnabled = playerLook.enabled;
            savedDamageFeedbackEnabled = damageFeedback.enabled;
            savedHudActive = playerHUD.activeSelf;

            Input.enabled = false;
            playerMovement.enabled = false;
            playerLook.enabled = false;
            damageFeedback.enabled = false;
            playerHUD.SetActive(false);
        }

        public void ExitCutsceneMode()
        {
            if (!inCutsceneMode) return;
            inCutsceneMode = false;

            damageFeedback.enabled = savedDamageFeedbackEnabled;
            playerHUD.SetActive(savedHudActive);

            // Dying during a cutscene captures a pre-death snapshot that says movement and look
            // were enabled. Restoring it verbatim is what hands a corpse its controls back and
            // re-locks the cursor away from the respawn button; re-assert the freeze instead.
            if (isDead)
            {
                ApplyDeathFreeze();
                return;
            }

            Input.enabled = savedInputEnabled;
            playerMovement.enabled = savedMovementEnabled;
            playerLook.enabled = savedLookEnabled;
        }

        private void OnDeath()
        {
            if (isDead) return;
            isDead = true;

            ApplyDeathFreeze();
            OnPlayerDeath?.Invoke();

            // TODO: ragdoll
        }

        // Input.enabled is part of the freeze, not an extra: jump and dash are delivered as input
        // EVENTS that PlayerMovement subscribes to in Start and never unsubscribes, so disabling
        // that component stops its FixedUpdate but leaves a dead player still able to jump and
        // dash. Killing input at the source is what actually stops them moving.
        //
        // The cursor is released here rather than left to PlayerLook.OnDisable because the death
        // screen needs a clickable cursor even when PlayerLook was already disabled (mounted, mid
        // cutscene) and so raises no OnDisable at all.
        private void ApplyDeathFreeze()
        {
            Input.enabled = false;
            playerMovement.enabled = false;
            playerLook.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Paired with OnDeath: without this a respawned player keeps the frozen
        // movement/look from the death that preceded it. HealthComponent raises
        // OnRevive when health is restored from zero, which is what MatchManager's
        // respawn does, so this is the natural place to hand control back.
        private void OnRevive()
        {
            isDead = false;
            ExitSpectatorMode();

            if (!inCutsceneMode)
            {
                Input.enabled = true;
                playerMovement.enabled = true;
                playerLook.enabled = true;

                // PlayerLook re-locks the cursor from LateUpdate once enabled, but only from the
                // next frame. Doing it here too closes the one-frame window where a click meant for
                // the world lands on whatever UI is still up.
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private SpectatorCamera spectator;

        // Called (via MatchManager, on the owning client) only when this player is
        // out of the match for good — not on a death they'll respawn from, which
        // would swap the player camera out and never hand it back.
        public void EnterSpectatorMode()
        {
            if (spectator != null) return;

            var spectatorGo = new GameObject("SpectatorCamera");
            if (PlayerCameraTransform != null)
            {
                spectatorGo.transform.SetPositionAndRotation(
                    PlayerCameraTransform.position, PlayerCameraTransform.rotation);
            }

            spectatorGo.AddComponent<Camera>();
            spectator = spectatorGo.AddComponent<SpectatorCamera>();

            if (playerCamera != null)
                playerCamera.SetActive(false);
        }

        public void ExitSpectatorMode()
        {
            if (spectator == null) return;

            Destroy(spectator.gameObject);
            spectator = null;

            if (playerCamera != null)
                playerCamera.SetActive(true);
        }
    }
}
