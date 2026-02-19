using Unity.Netcode;
using UnityEngine;

public class PlayerReference : NetworkBehaviour
{
    public HealthComponent Health { get; private set; }
    public PlayerInventory Inventory { get; private set; }
    public Interactor Interactor { get; private set; }

    private void Awake()
    {
      Health = GetComponent<HealthComponent>();
      Inventory = GetComponent<PlayerInventory>();
      Interactor = GetComponent<Interactor>();
    }

    private void Start()
    {
        if(!IsOwner) return;
        
        PlayerUI.Instance.BindToPlayer(this);
    }
}
