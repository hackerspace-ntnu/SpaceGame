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
    }
}
