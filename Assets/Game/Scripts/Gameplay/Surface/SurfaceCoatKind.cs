namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// The coats a surface can be under. The world-facing twin of <c>StatusKind</c>: a status hangs
    /// on a body, a coat hangs on a patch of ground.
    ///
    /// <para>
    /// <b>These numbers are on the wire.</b> They travel as <c>NetArg.B</c> under
    /// <c>NetMsg.CoatSprayed</c>, so they are a contract between builds: append only, never
    /// renumber and never reuse a retired value. They are also written into save files by the
    /// <c>Ice</c> record, which makes them permanent twice over.
    /// </para>
    /// </summary>
    public enum SurfaceCoatKind
    {
        /// <summary>A frictionless film. Twenty seconds, then it wears off. No collider.</summary>
        Slick = 0,

        /// <summary>
        /// Frozen liquid. Permanent until something breaks it, and the one kind that is GEOMETRY as
        /// well as grip — freezing a pool makes it standable.
        /// </summary>
        Ice = 1,

        /// <summary>Rained on. A little slippery, and what <see cref="Ice"/> can be laid over.</summary>
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
        /// <c>StatusKinds.Count</c> is: the values are wire ids, so the day a retired kind leaves a
        /// hole in the numbering this is the count that has to keep covering it, and a reflection
        /// call would quietly return the wrong one.
        /// </summary>
        public const int Count = 3;

        /// <summary>Is <paramref name="kind"/> one this build has a behaviour for?</summary>
        public static bool IsKind(SurfaceCoatKind kind) => (int)kind >= 0 && (int)kind < Count;
    }
}
