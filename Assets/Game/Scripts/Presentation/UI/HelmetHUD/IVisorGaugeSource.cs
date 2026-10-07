namespace SpaceGame.Presentation
{
    /// <summary>
    /// What a <see cref="VisorGauge"/> reads.
    ///
    /// <para>
    /// One interface so the gauge is written once and the suit's two survival numbers — integrity
    /// and oxygen — are two instances of it rather than two copies of the same drawing code.
    /// </para>
    /// </summary>
    public interface IVisorGaugeSource
    {
        /// <summary>The value now.</summary>
        float Current { get; }

        /// <summary>
        /// The value at full. May legitimately be 0 before the underlying component has spawned,
        /// which is why <see cref="VisorGauge.FractionOf"/> guards it.
        /// </summary>
        float Max { get; }

        /// <summary>Uppercase field label, e.g. "SUIT INTEGRITY".</summary>
        string Label { get; }

        /// <summary>Fraction below which the gauge reads as a warning.</summary>
        float WarnFraction { get; }

        /// <summary>Fraction below which the gauge reads as critical.</summary>
        float AlarmFraction { get; }

        /// <summary>
        /// False while the underlying component has not resolved yet. A gauge hides itself rather
        /// than drawing a confident zero — a full or empty bar that means "not loaded yet" is the
        /// worst possible lie to tell about a survival number.
        /// </summary>
        bool Available { get; }
    }
}
