using FirstGearGames.SmoothCameraShaker;
using FMODUnity;
using UnityEngine;

public class DamageFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HealthComponent health;
    [SerializeField] private ShakeData shakeData;

    [Header("Audio")]
    [SerializeField] private EventReference damageSound; 

    private void Awake()
    {
        if (health == null)
            health = GetComponent<HealthComponent>();
    }

    private void OnEnable()
    {
        if (health == null) return;
        health.OnDamage += OnDamaged;
    }

    private void OnDisable()
    {
        if (health == null) return;
        health.OnDamage -= OnDamaged;
    }

    private void OnDamaged(int amount)
    {
        CameraShakerHandler.Shake(shakeData);

        // Play sound through your AudioManager
        AudioManager.Instance.PlayEvent(damageSound);
    }
}