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
    }
}
