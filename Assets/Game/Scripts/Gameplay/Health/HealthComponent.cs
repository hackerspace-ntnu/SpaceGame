using System;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public class HealthComponent : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// Any health at all being hurt, on whichever machine actually applied the hit — read
        /// <see cref="LastDamageSource"/> on the victim to find out who did it.
        /// <para>
        /// Static because the listener is one screen-wide overlay rather than something living on
        /// each victim, and the victims are every animal, NPC, player, crate and test cube in a
        /// streamed world. Subscribing per instance would mean a component on every damageable
        /// prefab, and the ones that get forgotten are exactly the ones that then silently show no
        /// feedback.
        /// </para>
        /// <para>
        /// This is deliberately NOT the same signal as <c>NetworkedHealthComponent.DamageAnnounced</c>.
        /// This one fires where the damage was decided and needs nothing replicated; that one
        /// carries the news to a client that cannot see its own hits, because
        /// <c>Weapon.Use()</c> runs on the authority alone. Between them every case is covered
        /// once, and the broadcast deliberately excludes the authority so neither doubles up.
        /// </para>
        /// </summary>
        public static event Action<HealthComponent, int> AnyDamaged;

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

        /// <summary>
        /// True only while <see cref="RestoreHealth"/> is applying a saved value, so a listener can
        /// tell "this just died" from "this was already dead when the world loaded".
        ///
        /// It has to be askable, because <see cref="OnDeath"/> fires in both cases and the
        /// consequences of death are not repeatable: <c>HealthReactionModule</c> plays the death
        /// sound and starts a despawn timer, and <c>EntityLootTable</c> drops the loot table. Without
        /// this flag, killing one creature and reloading five times dropped five sets of loot.
        /// </summary>
        public bool IsRestoring { get; private set; }

        public Transform LastDamageSource { get; private set; }

        public void Damage(int amount) => Damage(amount, null);

        public void Damage(int amount, Transform source)
        {
            if (amount <= 0 || !Alive) return;

            LastDamageSource = source;
            currentHealth -= amount;

            OnDamage?.Invoke(amount);

            // After OnDamage and before the death check, so a killing blow still shows its number.
            AnyDamaged?.Invoke(this, amount);

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
        ///
        /// Listeners that act on death rather than merely observing it must check
        /// <see cref="IsRestoring"/> — see that property.
        /// </summary>
        public void RestoreHealth(int value)
        {
            int clamped = Math.Clamp(value, 0, maxHealth);
            bool wasAlive = Alive;
            bool changed = clamped != currentHealth;

            IsRestoring = true;

            try
            {
                if (changed)
                {
                    currentHealth = clamped;
                    OnRestored?.Invoke();
                }

                // Announced whenever the restored value is lethal, not only when it crosses zero. A
                // restore's job is to leave the object matching its record, and the two cases the old
                // crossing test missed both leave a corpse standing up: an entity whose prefab already
                // reads 0, and one killed by overkill damage whose live value is negative. Repeating
                // the announcement is safe precisely because IsRestoring suppresses the consequences.
                if (clamped <= 0) OnDeath?.Invoke();
                else if (!wasAlive) OnRevive?.Invoke();
            }
            finally
            {
                // In a finally block because a listener throwing must not leave every later death in
                // the session looking like a restore — which would silently stop all loot dropping.
                IsRestoring = false;
            }
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
