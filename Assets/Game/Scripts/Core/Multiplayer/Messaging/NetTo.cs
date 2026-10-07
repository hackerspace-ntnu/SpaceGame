namespace SpaceGame.Core
{
    /// <summary>Where a message goes. Offline every direction collapses to "run it here".</summary>
    public enum NetTo
    {
        /// <summary>The server, which is the only machine allowed to change shared state.</summary>
        Server,
        /// <summary>Every machine including this one. Server-only — a client's send is dropped.</summary>
        All,
        /// <summary>Every machine except this one, for when the caller already acted locally.</summary>
        Others,
    }
}
