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

    private void Awake()
    {
        playerCamera.gameObject.SetActive(false);
        playerHUD.gameObject.SetActive(false);
        playerMovement.enabled = false;
        playerLook.enabled = false;
        damageFeedback.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;
    }
    
}
