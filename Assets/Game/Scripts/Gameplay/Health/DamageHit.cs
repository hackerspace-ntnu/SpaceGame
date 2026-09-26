using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What delivered a hit, as far as a defence cares. Travels in <c>NetMsg.Damage</c>'s B, so
    /// append only.
    ///
    /// <para>
    /// A value exists only where something deals it. Everything that does not say is
    /// <see cref="Unspecified"/> — bullets, blasts, falls, fire — and no filter may treat that as
    /// any particular kind.
    /// </para>
    /// </summary>
    public enum DamageKind
    {
        Unspecified = 0,

        /// <summary>A blow at arm's length that the victim could see coming: the player's gauntlet punch and wrist blade.</summary>
        Melee = 1
    }

    /// <summary>
    /// How the struck body met a hit, decided by an <see cref="IDamageFilter"/> where the damage is
    /// decided. Travels in <c>NetMsg.Damaged</c>'s B, so append only.
    /// </summary>
    public enum DamageDefense
    {
        /// <summary>It took the hit.</summary>
        None = 0,

        /// <summary>It caught the blow on its guard; some of it may still have got through.</summary>
        Blocked = 1,

        /// <summary>It got out of the way; nothing landed.</summary>
        Dodged = 2
    }

    /// <summary>
    /// One hit on its way into a <see cref="HealthComponent"/>, handed to each
    /// <see cref="IDamageFilter"/> in turn before any health changes.
    /// </summary>
    public struct DamageHit
    {
        /// <summary>What will be taken off. A filter may lower it, down to 0 for a hit that never lands.</summary>
        public int Amount;

        /// <summary>
        /// What the blow was worth before any filter touched it — how hard the attacker MEANT to
        /// hit, which is what a body takes offence at even when its guard stopped every point.
        /// </summary>
        public readonly int Attempted;

        /// <summary>Who dealt it — the attacker's root, as <c>NetDamage.Apply</c> was told. Null for a fall or a cactus.</summary>
        public Transform Source;

        public DamageKind Kind;

        /// <summary>
        /// Set by the filter that defended against the hit. Anything but <see cref="DamageDefense.None"/>
        /// also means the body has already shown the hit its own way, so the flinch stays out of it.
        /// </summary>
        public DamageDefense Defense;

        public DamageHit(int amount, Transform source, DamageKind kind)
        {
            Amount = amount;
            Attempted = amount;
            Source = source;
            Kind = kind;
            Defense = DamageDefense.None;
        }
    }
}
