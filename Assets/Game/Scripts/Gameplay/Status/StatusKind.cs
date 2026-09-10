namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// The conditions a body can be in. One value per kind, and a body either carries a kind or it
    /// does not — reapplying refreshes the expiry rather than stacking a second copy.
    ///
    /// <para>
    /// <b>These numbers are on the wire.</b> They travel as <c>NetArg.A</c> under
    /// <c>NetMsg.StatusSet</c>, so they are a contract between builds: append only, never renumber
    /// and never reuse a retired value. A machine on an older build decoding a shifted id would
    /// silently draw the wrong condition.
    /// </para>
    /// </summary>
    public enum StatusKind
    {
        /// <summary>Damage over time on its own clock. Creatures panic and flee. Put out by water.</summary>
        Burning = 0,

        /// <summary>Helpless — no movement, no attacks, no damage of its own.</summary>
        Frozen = 1,

        /// <summary>No ground grip, and nothing thrown will stick to the body either.</summary>
        Slick = 2,

        /// <summary>Scale and mass driven by one signed scalar. Deflates when it stops being pumped.</summary>
        Inflated = 3,

        /// <summary>Held in place. Broken early by damage.</summary>
        Foamed = 4,

        /// <summary>
        /// Swallowed whole: hidden, out of every physics query, and unable to act until whatever
        /// ate it gives it back. Unlike the other five this is not a change to what a body can do,
        /// it is the body not being in the world — see <see cref="SwallowedStatus"/>.
        /// </summary>
        Swallowed = 5,
    }

    /// <summary>
    /// Facts about <see cref="StatusKind"/> that the receiver needs at load time.
    /// </summary>
    public static class StatusKinds
    {
        /// <summary>
        /// How many kinds there are, which is what the receiver's per-kind arrays are sized to.
        ///
        /// Written out rather than taken from <c>Enum.GetValues</c> because it indexes an array on
        /// a hot path and because the enum's values are wire ids: the day a retired kind leaves a
        /// hole in the numbering, this is the count that has to keep covering it, and a reflection
        /// call would quietly return the wrong one.
        /// </summary>
        public const int Count = 6;
    }
}
