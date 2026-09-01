namespace SpaceGame.Core
{
    /// <summary>What a <see cref="NetMsg.GrappleRope"/> message is saying. Append only.</summary>
    public static class GrappleVerb
    {
        /// <summary>The rope has let go. Stop drawing it.</summary>
        public const int Off = 0;

        /// <summary>
        /// The rope is out, attached where <see cref="NetArg.P"/> says.
        ///
        /// An absolute state rather than an edge — "this rope is attached", not "this rope just
        /// attached" — so re-sending it to a joiner costs everybody else one idempotent no-op.
        /// </summary>
        public const int On = 1;
    }
}
