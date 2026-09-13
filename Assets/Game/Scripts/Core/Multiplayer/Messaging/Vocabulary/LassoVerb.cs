namespace SpaceGame.Core
{
    /// <summary>What a <see cref="NetMsg.LassoRope"/> message is saying. Append only.</summary>
    public static class LassoVerb
    {
        /// <summary>The arc latched onto <see cref="NetArg.Target"/>. Rope it.</summary>
        public const int Caught = 1;

        /// <summary>The thrower is pulling the rope in.</summary>
        public const int ReelOn = 2;

        /// <summary>The thrower stopped pulling.</summary>
        public const int ReelOff = 3;

        /// <summary>
        /// The far end is pulling hard enough to take line. Every machine pays the rope out at the
        /// authored rate from here until <see cref="StrainOff"/>.
        ///
        /// <para>
        /// An edge rather than a measurement, and that is deliberate. Each machine judging the
        /// strain from its own interpolated copy of two moving ends would give every one of them a
        /// different rope length within seconds — permanently, since the length is what both the
        /// constraint and the break verdict are measured against.
        /// </para>
        /// </summary>
        public const int StrainOn = 4;

        /// <summary>The far end has stopped taking line.</summary>
        public const int StrainOff = 5;

        /// <summary>
        /// The rope wore through. Everybody drops it.
        ///
        /// Absolute state like <see cref="Caught"/>, so a machine that has already let go treats it
        /// as a no-op rather than an error.
        /// </summary>
        public const int Snapped = 6;

        /// <summary>
        /// The catch has been tied off to something. <see cref="NetArg.P"/> carries the knot — an
        /// offset in the anchor's own space when <see cref="NetArg.Target"/> resolves, a world point
        /// when it does not. Every machine builds the leash and drops the lasso.
        /// </summary>
        public const int Hitched = 7;
    }
}
