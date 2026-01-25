using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    private InventorySlotUI[] slotUIs;
    public PlayerInventory Inventory;
    
    [SerializeField] private Transform slotPrefab;
    [SerializeField] private Transform inventoryGrid; 

    private void Start()
    {
        InitializeUI();
    }

    public void InitializeUI()
    {
        int inventorySize = Inventory.Inventory.InventorySize;
        slotUIs = new InventorySlotUI[inventorySize];
        for (int i = 0; i < inventorySize; i++)
        {
            Transform slotTransform= Instantiate(slotPrefab, inventoryGrid);
            InventorySlotUI slotUI =  slotTransform.GetComponent<InventorySlotUI>();
            slotUIs[i] = slotUI;
        }
        RefreshAll();
    }
    

    public void RefreshAll()
    {
        for (int i = 0; i < slotUIs.Length; i++)
        {
            slotUIs[i].Refresh(Inventory.Inventory.GetSlot(i));
        }
    }
}

