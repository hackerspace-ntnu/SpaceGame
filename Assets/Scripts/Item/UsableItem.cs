using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public abstract class UsableItem : NetworkBehaviour
{
    protected EquipmentController equipmentController;
    protected InputManager inputManager;
    
    [SerializeField] private int maxUses = -1; // -1 means unlimited uses
    [SerializeField] protected AudioClip useSound;

    private int currentUses = 0;
    private bool isSubscribedToInput;
    private InputAction useAction;

    protected virtual void Awake()
    {
        ResolveDependencies();
        useAction = InputSystem.actions.FindAction("Attack");
    }

    protected virtual void OnEnable()
    {
        TrySubscribeToInput();
    }

    protected virtual void OnDisable()
    {
        UnsubscribeFromInput();
    }

    protected virtual void Update()
    {
        ResolveDependencies();
        TrySubscribeToInput();

        if (useAction == null)
        {
            useAction = InputSystem.actions.FindAction("Attack");
        }

        if (!isSubscribedToInput && useAction != null && useAction.WasPressedThisFrame())
        {
            TryUse();
        }
    }

    private void TryUse()
    {
        Debug.Log($"[UsableItem] Use input received for '{name}'.");

        if (!CanUse())
        {
            Debug.Log($"[UsableItem] Use blocked for '{name}'.");
            return;
        }

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

    protected virtual bool CanUse()
    {
        ResolveDependencies();

        // Prevent use if max uses reached
        if (maxUses >= 0 && currentUses >= maxUses)
        {
            return false;
        }
        
        return equipmentController != null &&
               equipmentController.getCurrentObject() == this.gameObject;
    }

    private void ResolveDependencies()
    {
        if (equipmentController == null)
        {
            equipmentController = GetComponentInParent<EquipmentController>();
            if (equipmentController == null)
            {
                equipmentController = FindFirstObjectByType<EquipmentController>();
            }
        }

        if (inputManager == null)
        {
            inputManager = GetComponentInParent<InputManager>();
            if (inputManager == null)
            {
                inputManager = FindFirstObjectByType<InputManager>();
            }
        }
    }

    private void TrySubscribeToInput()
    {
        if (isSubscribedToInput || inputManager == null)
        {
            return;
        }

        inputManager.OnUsePressed += TryUse;
        isSubscribedToInput = true;
    }

    private void UnsubscribeFromInput()
    {
        if (!isSubscribedToInput || inputManager == null)
        {
            return;
        }

        inputManager.OnUsePressed -= TryUse;
        isSubscribedToInput = false;
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
