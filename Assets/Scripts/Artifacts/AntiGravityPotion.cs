using UnityEngine;

public class AntiGravityPotion : UsableItem
{
    private bool isActive = false;
    private Rigidbody playerRigidbody;

    private float timer = 0f;
    private const float DURATION = 5f; // Duration in seconds
    private const float FLOAT_FORCE = 9.81f; // Upwards force, adjust to taste

    protected override void Use()
    {
        if (playerRigidbody == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerRigidbody = player.GetComponent<Rigidbody>();
            }
        }

        if (playerRigidbody != null && !isActive)
        {
            isActive = true;
            timer = 0f;
        }
    }

    private void Update()
    {
        if (!isActive || playerRigidbody == null) return;

        timer += Time.deltaTime;

        // Apply upward force every frame to float
        playerRigidbody.AddForce(Vector3.up * FLOAT_FORCE, ForceMode.Acceleration);

        if (timer >= DURATION)
        {
            StopEffect();
        }
    }

    private void StopEffect()
    {
        if (!isActive) return;

        isActive = false;
        timer = 0f;
    }
}
