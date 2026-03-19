using UnityEngine;

/// <summary>
/// ToolItem is for one-time use effects that don't need duration management.
/// Examples: Heal potion, speed boost, teleport, damage, etc.
/// These items execute their effect once and don't need cleanup.
/// </summary>
public abstract class ToolItem : UsableItem
{
    protected AimProvider aimProvider;

    protected void Awake()
    {
        // Item is parented to the player's hand socket when equipped,
        // so we can walk up the hierarchy to find the player's AimProvider.
        aimProvider = GetComponentInParent<AimProvider>();
    }

    // Tool items use the default UsableItem behavior
    // Just override Use() to implement your immediate effect
}
