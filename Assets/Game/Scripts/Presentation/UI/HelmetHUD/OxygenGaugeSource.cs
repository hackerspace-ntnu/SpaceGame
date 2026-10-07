using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Adapts <see cref="SuitOxygen"/> to <see cref="IVisorGaugeSource"/>.
    ///
    /// <para>
    /// <b>It draws ONE of two reservoirs, and relabels itself.</b> With a tank in the pack's socket
    /// it is the tank; with none, or a dry one, it is the suit's 60-second reserve. Two gauges side
    /// by side was the alternative and it is worse: the reserve is not a number the player manages,
    /// it is a deadline, and a permanent second bar sitting at 100% for an entire session teaches
    /// them to stop looking at exactly the readout that matters when it finally moves.
    /// </para>
    /// <para>
    /// <b>Everything is a percentage</b>, which is why <see cref="Max"/> is a flat 100 rather than
    /// the reservoir's own capacity. It keeps one number in the player's head across a 30-minute
    /// tank, a 60-second reserve and every future tank type — see <see cref="Items.SupplyCharge.Describe"/>,
    /// which is the same decision for the item's own gauge.
    /// </para>
    /// <para>
    /// Holds the component rather than its numbers, like <see cref="HealthGaugeSource"/>, so the
    /// gauge reads through to live air and a suit that has not resolved yet reports
    /// <see cref="Available"/> false instead of a confident full bar.
    /// </para>
    /// </summary>
    public class OxygenGaugeSource : IVisorGaugeSource
    {
        /// <summary>
        /// A threshold no fraction can reach, so the gauge is pinned to one state.
        ///
        /// Used for the reserve, where "how far through it are you" is not the question: being on
        /// the reserve at all is the critical state, at 100% of it just as much as at 5%.
        /// </summary>
        private const float Always = 2f;

        /// <summary>A threshold no fraction can fall below. The opposite of <see cref="Always"/>.</summary>
        private const float Never = 0f;

        private SuitOxygen suit;

        /// <summary>Points the source at a suit. Safe with null.</summary>
        public void Bind(SuitOxygen next) => suit = next;

        /// <summary>The suit currently read, or null while it has not resolved.</summary>
        public SuitOxygen Suit => suit;

        /// <summary>
        /// Is the tank the thing being drawn? False means the reserve is, which is either an empty
        /// socket or a tank that has run out.
        /// </summary>
        private bool ShowingTank => suit != null && suit.TankConnected && suit.TankFraction > 0f;

        public float Current =>
            suit == null ? 0f : 100f * (ShowingTank ? suit.TankFraction : suit.SuitFraction);

        public float Max => suit != null ? 100f : 0f;

        public string Label => ShowingTank ? "O2 TANK" : "O2 RESERVE";

        public float WarnFraction => ShowingTank && suit != null ? suit.WarnFraction : Always;

        /// <summary>
        /// Critical for the whole of the reserve, and never for the tank — a tank at 0% is not a
        /// tank any more, it is the reserve, and this source has already relabelled itself by then.
        /// </summary>
        public float AlarmFraction => ShowingTank ? Never : Always;

        public bool Available => suit != null;
    }
}
