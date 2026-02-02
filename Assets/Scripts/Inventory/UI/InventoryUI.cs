using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    private InventorySlotUI[] slotUIs;
    [SerializeField] private PlayerInventory inventory;
    
    [SerializeField] private Transform slotPrefab;
    [SerializeField] private Transform inventoryGrid; 
    
    private int selectedIndex = -1;
    private int hoveredIndex = -1;
    
    private void Awake()
    {
        inventory.OnSlotSelected += OnSlotSelected;
        inventory.OnInventoryChanged += OnInventoryChanged;
    }


    private void OnDestroy()
    {
        inventory.OnSlotSelected -= OnSlotSelected;
    }

    private void Start()
    {
        InitializeUI();
    }

    public void InitializeUI()
    {
        int inventorySize = inventory.InventorySize;
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
    
    private void OnInventoryChanged()
    {
        RefreshAll();
    }
    

    public void RefreshAll()
    {
        for (int i = 0; i < slotUIs.Length; i++)
        {
            slotUIs[i].Refresh(inventory.GetSlot(i),
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

