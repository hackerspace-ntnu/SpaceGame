using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    protected EquipmentController equipmentController;
    protected InputManager inputManager;

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
        }
    }

    protected virtual bool CanUse()
    {
        return equipmentController != null &&
               equipmentController.getCurrentObject() == this.gameObject;
    }

    protected abstract void Use();
}
