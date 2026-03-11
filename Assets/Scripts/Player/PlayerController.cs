using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.LowLevel;

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
    
    [SerializeField] bool isPlayerEnabled = true;
    
    // High level player events
    public event Action OnPlayerDeath;
    

    private void Awake()
    {
        Input = GetComponent<PlayerInputManager>(); 
        
        PlayerInventory = GetComponent<IPlayerInventory>();
        
        DisablePlayer();
        if (isPlayerEnabled)
        {
            EnablePlayer();
        }
    }
     

    public void OnDisablePlayer()
    {
        playerCamera.gameObject.SetActive(false);
        playerHUD.gameObject.SetActive(false);
        playerMovement.enabled = false;
        playerLook.enabled = false;
        damageFeedback.enabled = false;
    }

    public void OnEnablePlayer()
    {
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;
        
        playerHealth.OnDeath += OnDeath;
    }
    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // TODO: ragdoll
    }
    
    public void EnablePlayer() => OnEnablePlayer();
    public void DisablePlayer() => OnDisablePlayer();
    
}
