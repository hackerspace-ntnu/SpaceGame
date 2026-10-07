namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// The coats a surface can be under. The world-facing twin of <c>StatusKind</c>: a status hangs
    /// on a body, a coat hangs on a patch of ground.
    ///
    /// <para>
    /// <b>These numbers are on the wire.</b> They travel as <c>NetArg.B</c> under
    /// <c>NetMsg.CoatSprayed</c>, so they are a contract between builds: append only, never
    /// renumber and never reuse a retired value — a peer on an older build reads whatever number
    /// arrives as the kind that number meant when it was built.
    /// </para>
    /// </summary>
    public enum SurfaceCoatKind
    {
        // 0 was Slick, a film of frost, and 1 was Ice, a standable sheet over water. Both were the
        // cryo sprayer's, and the sprayer stopped coating the ground at all — the plume is a thing
        // that happens to bodies now. RETIRED, never reused: the values are on the wire, so a peer
        // on an older build still means those two by them.

        /// <summary>Rained on. A little slippery, and the only coat anything lays.</summary>
        Wet = 2,
    }

    /// <summary>
    /// Facts about <see cref="SurfaceCoatKind"/> that the field needs at load time.
    /// </summary>
    public static class SurfaceCoatKinds
    {
        /// <summary>
        /// How many kinds there are, which is what the field's per-kind array is sized to.
        ///
        /// Written out rather than taken from <c>Enum.GetValues</c> for the reason
        /// <c>StatusKinds.Count</c> is: the values are wire ids, and two retired kinds have left a
        /// hole at 0 and 1 that this count has to keep covering — a reflection call would return
        /// one, which is the size of an array the only live kind does not fit in.
        /// </summary>
        public const int Count = 3;

        /// <summary>Is <paramref name="kind"/> one this build has a behaviour for?</summary>
        public static bool IsKind(SurfaceCoatKind kind) => (int)kind >= 0 && (int)kind < Count;
    }
}
