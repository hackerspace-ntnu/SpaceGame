using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    private InventorySlotUI[] slotUIs;
    
    [SerializeField] private PlayerInventory playerInventory;
    
    [SerializeField] private Transform slotPrefab;
    [SerializeField] private Transform inventoryGrid; 
    
    private int selectedIndex = -1;
    private int hoveredIndex = -1;

    public void OnEnable()
    {
        playerInventory.OnSlotSelected += OnSlotSelected;
        playerInventory.OnInventoryChanged += OnPlayerInventoryChanged;
        InitializeUI();
    }


    private void OnDestroy()
    {
        if(!playerInventory) return;
        playerInventory.OnSlotSelected -= OnSlotSelected;
    }

    public void InitializeUI()
    {
        int inventorySize = playerInventory.GetInventorySize();
        slotUIs = new InventorySlotUI[inventorySize];
        
        for (int i = 0; i < inventorySize; i++)
        {
            Transform slotTransform= Instantiate(slotPrefab, inventoryGrid);
            InventorySlotUI slotUI =  slotTransform.GetComponent<InventorySlotUI>();
            
            slotUI.Init(i, this);
            slotUIs[i] = slotUI;
        }
        
        RefreshAll();
    }
    
    private void OnSlotSelected(int index)
    {
        selectedIndex = index;
        RefreshAll();
    }
    
    private void OnPlayerInventoryChanged()
    {
        Debug.Log("Inventory changed, refreshing UI");
        RefreshAll();
    }
    

    public void RefreshAll()
    {
        for (int i = 0; i < slotUIs.Length; i++)
        {
            slotUIs[i].Refresh(playerInventory.GetSlot(i),
                i == selectedIndex,
                i == hoveredIndex);
        }
    }
    
    public void OnSlotHovered(int index)
    {
        hoveredIndex = index;
        RefreshAll();
    }

    public void OnSlotUnhovered(int index)
    {
        if (hoveredIndex == index)
            hoveredIndex = -1;

        RefreshAll();
    }
}

