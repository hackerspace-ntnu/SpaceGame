
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;

public class PlayerInventory : MonoBehaviour, IInventoryComponent

{
    private InputAction hotkeyAction;
    public Inventory Inventory{ get; set; }
    private void Awake()
    {
        Inventory inventory = new Inventory(4);
    }
    private void Update()
    {
        if (hotkeyAction.WasPressedThisFrame()) { 
            int value = hotkeyAction.ReadValue<int>();
            string itemName = Inventory.GetSlot(value-1).Item.itemName;
            Debug.Log(itemName);
        }
    }
}
