using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Adapts a <see cref="HealthComponent"/> to <see cref="IVisorGaugeSource"/>, so the player's
    /// health is drawn by the same gauge as everything else on the visor.
    ///
    /// <para>
    /// Holds the component rather than a copy of its numbers, so there is nothing to keep in step:
    /// the gauge reads through to live health. It also means a null health — which is legitimate
    /// for a frame or more while Netcode publishes the local player object — reports
    /// <see cref="Available"/> false rather than a confident zero.
    /// </para>
    /// </summary>
    public class HealthGaugeSource : IVisorGaugeSource
    {
        private HealthComponent health;

        /// <summary>Points the source at a health component. Safe with null.</summary>
        public void Bind(HealthComponent next) => health = next;

        /// <summary>The component currently read, or null while it has not resolved.</summary>
        public HealthComponent Health => health;

        public float Current => health != null ? health.GetHealth : 0f;

        public float Max => health != null ? health.GetMaxHealth : 0f;

        public string Label => "SUIT INTEGRITY";

        public float WarnFraction => 0.35f;

        public float AlarmFraction => 0.15f;

        public bool Available => health != null && health.GetMaxHealth > 0;
    }
}
