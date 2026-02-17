using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HealthComponent health;
    [SerializeField] private Image healthBar;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI maxHealthText;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<HealthComponent>();
    }

    private void OnEnable()
    {
        if (health == null) return;

        health.OnDamage += HandleHealthChanged;
        health.OnHeal += HandleHealthChanged;
        health.OnDeath += HandleHealthChanged;
        health.OnRevive += HandleHealthChanged;

        RefreshUI();
    }

    private void OnDisable()
    {
        if (health == null) return;

        health.OnDamage -= HandleHealthChanged;
        health.OnHeal -= HandleHealthChanged;
        health.OnDeath -= HandleHealthChanged;
        health.OnRevive -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int _ = 0)
    {
        RefreshUI();
    }

    private void HandleHealthChanged()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        int current = health.GetHealth;
        int max = health.GetMaxHealth;

        float percent = max > 0 ? (float)current / max : 0f;
        if (healthBar != null)
            healthBar.fillAmount = percent;

        if (healthText != null)
            healthText.text = $"{current}";
        if (maxHealthText != null)
            maxHealthText.text = $"{max}";
    }
}