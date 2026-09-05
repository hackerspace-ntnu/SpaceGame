using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Adapts <see cref="SuitOxygen"/> to <see cref="IVisorGaugeSource"/>.
    ///
    /// <para>
    /// Holds the component rather than its numbers, like <see cref="HealthGaugeSource"/>, so the
    /// gauge reads through to live air and a suit that has not resolved yet reports
    /// <see cref="Available"/> false instead of a confident full bar.
    /// </para>
    /// <para>
    /// The thresholds come from the suit rather than from the gauge, because they are balance
    /// values: the number at which a player should start worrying belongs beside the drain rate it
    /// is tuned against, not in the drawing code.
    /// </para>
    /// </summary>
    public class OxygenGaugeSource : IVisorGaugeSource
    {
        private SuitOxygen suit;

        /// <summary>Points the source at a suit. Safe with null.</summary>
        public void Bind(SuitOxygen next) => suit = next;

        /// <summary>The suit currently read, or null while it has not resolved.</summary>
        public SuitOxygen Suit => suit;

        public float Current => suit != null ? suit.Current : 0f;

        public float Max => suit != null ? suit.Max : 0f;

        public string Label => "O2 SUPPLY";

        public float WarnFraction => suit != null ? suit.WarnFraction : 0.3f;

        public float AlarmFraction => suit != null ? suit.AlarmFraction : 0.1f;

        public bool Available => suit != null && suit.Max > 0f;
    }
}
