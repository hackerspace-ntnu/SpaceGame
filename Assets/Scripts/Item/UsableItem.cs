using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    protected EquipmentController equipmentController;
    protected InputManager inputManager;
    
    [SerializeField] private int maxUses = -1; // -1 means unlimited uses
    private int currentUses = 0;

    protected virtual void Awake()
    {
        equipmentController = FindFirstObjectByType<EquipmentController>();
        inputManager = FindFirstObjectByType<InputManager>();
    }
    protected virtual void OnEnable()
    {
        inputManager.OnUsePressed += TryUse;
    }

    protected virtual void OnDisable()
    {
        inputManager.OnUsePressed -= TryUse;
    }

    private void TryUse()
    {
        if (CanUse())
        {
            Use();
            currentUses++;
            
            // Check if we've reached max uses
            if (maxUses >= 0 && currentUses >= maxUses)
            {
                OnMaxUsesReached();
            }
        }
    }

    protected virtual bool CanUse()
    {
        // Prevent use if max uses reached
        if (maxUses >= 0 && currentUses >= maxUses)
        {
            return false;
        }
        
        return equipmentController != null &&
               equipmentController.getCurrentObject() == this.gameObject;
    }
    
    /// <summary>
    /// Called when the item reaches its maximum number of uses.
    /// Override in subclasses for custom behavior.
    /// </summary>
    protected virtual void OnMaxUsesReached()
    {
        // Remove from inventory
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            PlayerInventory playerInventory = player.GetComponent<PlayerInventory>();
            if (playerInventory != null)
            {
                playerInventory.TryRemoveItem(playerInventory.selectedSlotIndex);
            }
        }
        
        // Unequip and destroy the item
        if (equipmentController != null)
        {
            equipmentController.Unequip();
        }
    }

    protected abstract void Use();
}
