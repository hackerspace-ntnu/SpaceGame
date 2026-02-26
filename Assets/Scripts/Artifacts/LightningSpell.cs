using UnityEngine;

public class LightningSpell : ToolItem
{
    protected override void Use()
    {
        Debug.Log("LightningSpell used! Spawning lightning...");
        // The actual spawning of the lightning is handled by the LightningSpawner component on the player
        if (useSound == null) {
            Debug.LogWarning("Use sound not assigned for LightningSpell!");
            return;
        }
        // Removal from inventory and destruction is now handled by base class when maxUses is reached
    }
}