using System;
using System.Collections;
using UnityEngine;

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
        Input.enabled = true;
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;

        playerHealth.OnDeath += OnDeath;
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

        Input.enabled = savedInputEnabled;
        playerMovement.enabled = savedMovementEnabled;
        playerLook.enabled = savedLookEnabled;
        damageFeedback.enabled = savedDamageFeedbackEnabled;
        playerHUD.SetActive(savedHudActive);
    }

    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // TODO: ragdoll
    }
}
