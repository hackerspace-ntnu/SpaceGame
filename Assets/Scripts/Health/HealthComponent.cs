using System;
using UnityEngine;

public class HealthComponent : MonoBehaviour, IDamageable
{
    public event Action<int> OnDamage;
    public event Action<int> OnHeal;
    public event Action OnDeath;
    public event Action OnRevive;

    [SerializeField] private int maxHealth = 100;
    public int GetMaxHealth => maxHealth;

    [SerializeField] private int currentHealth = 100;
    public int GetHealth => currentHealth;

    public bool Alive => currentHealth > 0;

    public Transform LastDamageSource { get; private set; }

    public void Damage(int amount) => Damage(amount, null);

    public void Damage(int amount, Transform source)
    {
        if (amount <= 0 || !Alive) return;

        LastDamageSource = source;
        currentHealth -= amount;

        OnDamage?.Invoke(amount);

        if (currentHealth <= 0) OnDeath?.Invoke();
    }
    
    // Full restore for respawns. Heal() can't be used for this: overkill damage
    // drives currentHealth below zero, and Heal clamps the applied amount to
    // `amount`, so healing by maxHealth after a -50 hit comes back at half health
    // — or still dead if the overkill exceeded maxHealth. Raises OnHeal so
    // NetworkedHealthComponent replicates the new value like any other change.
    public void ResetToFull()
    {
        int restored = maxHealth - currentHealth;
        if (restored <= 0) return;

        bool wasDead = !Alive;
        currentHealth = maxHealth;

        OnHeal?.Invoke(restored);
        if (wasDead) OnRevive?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || currentHealth == maxHealth) return;
        
        int appliedHealing = Math.Min(maxHealth - currentHealth, amount);
        if (appliedHealing <= 0) return;
        
        bool isDead = !Alive;
        currentHealth += appliedHealing;
        OnHeal?.Invoke(appliedHealing);
        if (isDead && currentHealth > 0) OnRevive?.Invoke();
    }
}
