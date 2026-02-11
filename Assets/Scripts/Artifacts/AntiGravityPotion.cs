using UnityEngine;
using UnityEngine.InputSystem;

public class AntiGravityPotion : UsableItem
{
    private bool isActive = false;
    private float originalGravityScale;
    private Rigidbody2D playerRigidbody;

    private float timer = 0f;
    private const float DURATION = 5f; // Duration in seconds

    protected override void Use()
    {
        if (playerRigidbody == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerRigidbody = player.GetComponent<Rigidbody2D>();
                originalGravityScale = playerRigidbody.gravityScale;
            }
        }

        if (playerRigidbody != null && !isActive)
        {
            isActive = true;
            timer = 0f;
            playerRigidbody.gravityScale = 0f;
        }
    }

    public void Update()
    {
        if (isActive)
        {
            timer += Time.deltaTime;
            
            if (timer >= DURATION)
            {
                stopEffect();
            }
        }
    }

    public void stopEffect()
    {
        isActive = false;
        timer = 0f;
        
        if (playerRigidbody != null)
        {
            playerRigidbody.gravityScale = originalGravityScale;
        }
    }
}