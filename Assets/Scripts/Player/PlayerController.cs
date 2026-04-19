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

    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // TODO: ragdoll
    }
}
