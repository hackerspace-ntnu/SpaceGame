using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.LowLevel;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private GameObject playerCamera;
    [SerializeField] private GameObject playerHUD;
    
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerLook playerLook;
    [SerializeField] private DamageFeedback damageFeedback;
    
    [SerializeField] private HealthComponent playerHealth;
    
    public PlayerEvents PlayerEvents { get; private set; }
    

    private void Awake()
    {
        playerCamera.gameObject.SetActive(false);
        playerHUD.gameObject.SetActive(false);
        playerMovement.enabled = false;
        playerLook.enabled = false;
        damageFeedback.enabled = false;
        
        PlayerEvents = new PlayerEvents();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;
        
        playerHealth.OnDeath += OnPlayerDeath;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        
        if(!IsOwner) return;
        playerHealth.OnDeath -= OnPlayerDeath;
    }
    
    
    private void OnPlayerDeath()
    {
        PlayerEvents.OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        
        playerLook.enabled = false;
        // Turn in to ragdoll
    }
    
}
