using UnityEngine;

public class AntiGravityPotion : EffectItem
{
    private const float DURATION = 5f; // Duration in seconds
    private const float FLOAT_FORCE = 1f; // Upwards force, adjust to taste

    protected override void Use()
    {
        RegisterEffect(
            duration: DURATION,
            onApply: (rb) =>
            {
                rb.useGravity = false;
            },
            onTick: (rb) =>
            {
                // Apply upward force every frame to float
                rb.AddForce(Vector3.up * FLOAT_FORCE, ForceMode.Acceleration);
            },
            onStop: (rb) =>
            {
                rb.useGravity = true;
            }
        );
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
        
        Destroy(this.gameObject);
    }
}
