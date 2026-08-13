using System;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public class HealthComponent : MonoBehaviour, IDamageable
    {
        public event Action<int> OnDamage;
        public event Action<int> OnHeal;
        public event Action OnDeath;
        public event Action OnRevive;

        /// <summary>
        /// Raised when health is assigned rather than changed by gameplay — currently only by a
        /// save being loaded. Replication listens to this; damage feedback deliberately does not,
        /// because loading at half health should not flash the screen red as though you were just
        /// hit.
        /// </summary>
        public event Action OnRestored;

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

        /// <summary>
        /// Assigns health directly, as a load does. Clamped to the prefab's current maxHealth, so a
        /// save written when the prefab allowed 200 does not leave an entity above a ceiling that
        /// has since dropped to 100.
        ///
        /// Not expressible as Damage/Heal: those model events with consequences — Damage records a
        /// damage source and fires the feedback flash, Heal refuses to raise the dead. Restoring is
        /// neither. It raises <see cref="OnRestored"/>, plus OnDeath or OnRevive when the assignment
        /// crosses zero, since a listener that tracks alive/dead must not be left holding the wrong
        /// answer.
        /// </summary>
        public void RestoreHealth(int value)
        {
            int clamped = Math.Clamp(value, 0, maxHealth);
            if (clamped == currentHealth) return;

            bool wasAlive = Alive;
            currentHealth = clamped;

            OnRestored?.Invoke();

            if (wasAlive && !Alive) OnDeath?.Invoke();
            else if (!wasAlive && Alive) OnRevive?.Invoke();
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
}
