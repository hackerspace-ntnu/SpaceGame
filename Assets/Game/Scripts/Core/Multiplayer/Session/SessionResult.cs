namespace SpaceGame.Core
{
    /// <summary>The outcome of a connection attempt. Never an exception — see <see cref="SessionLauncher"/>.</summary>
    public readonly struct SessionResult
    {
        public readonly bool Success;

        /// <summary>Ready to show a player verbatim. Empty when <see cref="Success"/>.</summary>
        public readonly string Error;

        /// <summary>Relay join code, when hosting over Relay. Empty otherwise.</summary>
        public readonly string JoinCode;

        private SessionResult(bool success, string error, string joinCode)
        {
            Success = success;
            Error = error ?? string.Empty;
            JoinCode = joinCode ?? string.Empty;
        }

        public static SessionResult Ok(string joinCode = null) => new(true, null, joinCode);
        public static SessionResult Fail(string error) => new(false, error, null);
    }
}
