using System;
using System.Collections.Generic;
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

        /// <summary>
        /// As <see cref="AnyDamaged"/>, but carrying WHERE the hit came from, so a listener can
        /// point at it. The bool says whether there was a source at all: a fall, suffocation and
        /// sand all arrive with none, and a position of zero is a real place in the world rather
        /// than a usable "nowhere".
        /// <para>
        /// The position rather than the Transform, because this is also raised from a replicated
        /// message on a machine where the attacker may not exist as an object at all.
        /// </para>
        /// </summary>
        public static event Action<HealthComponent, int, Vector3, bool> AnyDamagedFrom;

        /// <summary>
        /// Any health anywhere defending against a hit (a block, a dodge), on the machine that
        /// decided it — the static twin of <see cref="OnDefended"/>, for the one screen-wide overlay
        /// that tells an attacker their blow was stopped. Static for the reason
        /// <see cref="AnyDamaged"/> is.
        /// </summary>
        public static event Action<HealthComponent, DamageHit> AnyDefended;

        public event Action<int> OnDamage;

        /// <summary>
        /// A filter defended against a hit, raised where the damage was decided, before any health
        /// changes. <see cref="DamageHit.Amount"/> is what still lands — 0 for a hit stopped whole,
        /// which raises this and nothing else: no <see cref="OnDamage"/>, so no flinch, no
        /// provocation and no hurt noise — the body was not hurt.
        /// </summary>
        public event Action<DamageHit> OnDefended;

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

        /// <summary>
        /// How the last hit decided here was met. Readable from inside <see cref="OnDamage"/> and
        /// <see cref="AnyDamaged"/>, the way <see cref="LastDamageSource"/> is: a block that let
        /// part of a blow through still raises them, and a listener that shows the hit its own way
        /// (the flinch) must know the guard already did.
        /// </summary>
        public DamageDefense LastDefense { get; private set; }

        /// <summary>
        /// The filters that get a say in every hit before it lands, in the order they registered.
        /// A list on the victim rather than a lookup per hit: a hit is frequent, a guard being
        /// switched on or off is not.
        /// </summary>
        private readonly List<IDamageFilter> filters = new List<IDamageFilter>();

        /// <summary>Give <paramref name="filter"/> a say in every hit. Call from OnEnable; adding twice is harmless.</summary>
        public void AddFilter(IDamageFilter filter)
        {
            if (filter != null && !filters.Contains(filter)) filters.Add(filter);
        }

        /// <summary>Take back what <see cref="AddFilter"/> gave. Call from OnDisable.</summary>
        public void RemoveFilter(IDamageFilter filter) => filters.Remove(filter);

        /// <summary>
        /// Announces a hit this machine did NOT resolve, for
        /// <see cref="AnyDamagedFrom"/>'s listeners only.
        ///
        /// <para>
        /// The replication layer's seam. A client never runs <see cref="Damage"/> — the server
        /// does — so without this its own visor could never learn which way to point. It changes
        /// no health: the value arrives separately through the health NetworkVariable, and
        /// applying it here as well would subtract the same hit twice.
        /// </para>
        /// </summary>
        public void ReportDamageDirection(int amount, Vector3 sourcePosition, bool hasSource)
        {
            if (amount <= 0) return;

            AnyDamagedFrom?.Invoke(this, amount, sourcePosition, hasSource);
        }

        public void Damage(int amount) => Damage(amount, null);

        /// <summary>
        /// Hurt this body, after every <see cref="IDamageFilter"/> has had its say. How the hit was
        /// met comes back — <see cref="DamageDefense.None"/> for one taken in full — so a weapon
        /// whose blow also shoves can leave a body that blocked or dodged it standing.
        /// </summary>
        public DamageDefense Damage(int amount, Transform source, DamageKind kind = DamageKind.Unspecified)
        {
            if (amount <= 0 || !Alive) return DamageDefense.None;

            var hit = new DamageHit(amount, source, kind);
            // By index, not foreach: a filter whose reaction disables a component that unregisters
            // must not invalidate an enumerator halfway through a hit.
            for (int i = 0; i < filters.Count; i++) filters[i].Filter(this, ref hit);

            LastDefense = hit.Defense;
            if (hit.Defense != DamageDefense.None)
            {
                OnDefended?.Invoke(hit);
                AnyDefended?.Invoke(this, hit);
            }

            if (hit.Amount <= 0) return hit.Defense;

            LastDamageSource = source;
            currentHealth -= hit.Amount;

            OnDamage?.Invoke(hit.Amount);

            // After OnDamage and before the death check, so a killing blow still shows its number.
            AnyDamaged?.Invoke(this, hit.Amount);
            AnyDamagedFrom?.Invoke(this, hit.Amount, source != null ? source.position : Vector3.zero,
                                   source != null);

            if (currentHealth <= 0) OnDeath?.Invoke();
            return hit.Defense;
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
