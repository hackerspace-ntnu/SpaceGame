namespace SpaceGame.Core
{
    /// <summary>Sentinels for the client-id arguments.</summary>
    public static class NetTarget
    {
        /// <summary>"This machine" — resolved at send time, since offline there is no client id yet.</summary>
        public const ulong Self = ulong.MaxValue;
    }
}
