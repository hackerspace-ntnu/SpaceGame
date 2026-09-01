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
    }
}
