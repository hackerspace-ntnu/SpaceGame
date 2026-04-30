using System;
using FMODUnity;
using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    [SerializeField] private int maxUses = -1; // -1 means unlimited uses
    [SerializeField] protected EventReference useSound;

    private int currentUses = 0;
    
    protected GameObject owner;
    
    public event Action<UsableItem> OnItemDepleted;

    public void TryUse(GameObject useOwner)
    {
        owner = useOwner;
        if (CanUse())
        {
            
            // Uses world position
            AudioManager.Instance.PlayEvent(useSound, transform.position);
            

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
        
        return true;
    }
    
    /// <summary>
    /// Called when the item reaches its maximum number of uses.
    /// Override in subclasses for custom behavior.
    /// </summary>
    protected virtual void OnMaxUsesReached()
    {
        OnItemDepleted?.Invoke(this);
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
