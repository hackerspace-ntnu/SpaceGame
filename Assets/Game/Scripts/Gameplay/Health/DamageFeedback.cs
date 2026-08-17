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
        [SerializeField] private SfxId damageId = SfxId.PlayerHurt;
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

            // Was AudioManager.Instance.PlayEvent(...), which threw whenever this ran in a scene
            // entered without passing through Bootstrap — the manager only exists there. Sfx has no
            // such dependency, and it supplies a default when damageSound was never assigned.
            Sfx.Play(damageId, transform.position, damageSound, GetInstanceID());
        }
    }
}
