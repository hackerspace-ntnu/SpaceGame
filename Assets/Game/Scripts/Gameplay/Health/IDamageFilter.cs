namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Something on a body that gets a say in a hit before it lands — a guard, a dodge, armour.
    /// Registered with <see cref="HealthComponent.AddFilter"/>; runs only where the damage is
    /// decided, so a filter never needs its own authority check and never sees a replicated hit.
    ///
    /// <para>
    /// A filter may lower <see cref="DamageHit.Amount"/> (to 0 for a hit that never lands) and
    /// stamp <see cref="DamageHit.Defense"/>. It must not change health or raise damage events
    /// itself: the component does that once, with the filtered result, so every listener agrees
    /// on what happened.
    /// </para>
    /// </summary>
    public interface IDamageFilter
    {
        void Filter(HealthComponent victim, ref DamageHit hit);
    }
}
