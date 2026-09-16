namespace SpaceGame.Core
{
    /// <summary>
    /// What an agent did, for <see cref="NetMsg.AgentActed"/>'s <see cref="NetArg.A"/>.
    ///
    /// A small enum rather than bare ints so a peer that receives a kind it does not recognise —
    /// an older build talking to a newer one — can be made to ignore it rather than index into
    /// something. Append only, like the ids themselves.
    /// </summary>
    public static class AgentAction
    {
        public const int Melee  = 0;
        public const int Ranged = 1;

        /// <summary>
        /// This agent's aggression band changed, and the new band is carried in
        /// <see cref="NetArg.B"/> as an <c>AggressionBand</c>.
        ///
        /// A posture and a bark rather than a swing, but it goes down the same channel for the
        /// same reason: the aggression meter is server state, so without this a nomad drawing his
        /// gun on a client stands there idle and the fight starts with no warning at all — which is
        /// precisely the thing the meter exists to prevent.
        /// </summary>
        public const int Band = 2;

        /// <summary>
        /// A war party's first sight of the player it is hunting: a shout from the tribe's roster
        /// <c>hostileLines</c>, index in <see cref="NetArg.B"/>. The index, not the text, so the
        /// message stays small and every machine reads the line from its own copy of the roster.
        /// </summary>
        public const int WarCry = 3;
    }
}
