using System;
using UnityEngine;

public class HealthComponent : MonoBehaviour
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
