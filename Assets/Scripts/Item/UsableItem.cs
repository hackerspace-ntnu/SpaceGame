using System;
using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    [SerializeField] private int maxUses = -1; // -1 means unlimited uses
    [SerializeField] protected AudioClip useSound;

    private int currentUses = 0;
    
    public event Action<UsableItem> OnItemDepleted;

    public void TryUse()
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

    protected abstract void Use();
}
