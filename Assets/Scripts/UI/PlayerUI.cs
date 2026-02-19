using UnityEngine;

public class PlayerUI : MonoBehaviour
{
    public static PlayerUI Instance { get; private set; }

     private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private HealthUI healthUI;
    [SerializeField] private CrosshairUI crosshairUI;

    

    public void BindToPlayer(PlayerReference player)
    {
        inventoryUI.Bind(player.Inventory);
        healthUI.Bind(player.Health);
        crosshairUI.Bind(player.Interactor);
    }

}
