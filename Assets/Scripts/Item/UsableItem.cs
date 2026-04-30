using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    protected EquipmentController equipmentController;
    protected InputManager inputManager;
    
    [SerializeField] private int maxUses = -1; // -1 means unlimited uses
    [SerializeField] protected AudioClip useSound;

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
            if (useSound != null)
            {
                AudioManager.Instance.PlaySFX(useSound);
            }

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

    /// <summary>
    /// Lifecycle hook fired by EquipmentController right after the item prefab is
    /// instantiated and parented to the player's hand. Use for "while held"
    /// effects (animation flags, audio loops, glow, etc). Subclasses overriding
    /// this should call base.OnEquipped() so the shared HoldAnimator wiring
    /// still fires.
    /// </summary>
    public virtual void OnEquipped(GameObject holder)
    {
        var hold = GetComponent<HoldAnimator>();
        if (hold != null) hold.SetHeld(holder, true);
    }

    /// <summary>
    /// Lifecycle hook fired by EquipmentController right before the item prefab
    /// is unparented/destroyed. Mirror of OnEquipped — clean up here.
    /// </summary>
    public virtual void OnUnequipped(GameObject holder)
    {
        var hold = GetComponent<HoldAnimator>();
        if (hold != null) hold.SetHeld(holder, false);
    }

    protected abstract void Use();
}
