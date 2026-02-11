using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    protected EquipmentController equipmentController;

    protected virtual void OnEnable()
    {
        InputManager.Instance.OnUsePressed += TryUse;
    }

    protected virtual void OnDisable()
    {
        InputManager.Instance.OnUsePressed -= TryUse;
    }

    private void TryUse()
    {
        if (CanUse())
        {
            Use();
        }
    }

    protected virtual bool CanUse()
    {
        return equipmentController != null &&
               equipmentController.getCurrentObject() == this.gameObject;
    }

    protected abstract void Use();
}
