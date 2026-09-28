using FirstGearGames.SmoothCameraShaker;
using FMODUnity;
using SpaceGame.Audio;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
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
            Sfx.Play(damageSound, transform.position);
        }
    }
}
