namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// What a <see cref="LobbyBusyScope"/> switches off on the join page.
    ///
    /// A table rather than a chain of conditions at each call site, because the rules are not
    /// uniform and the differences are the interesting part: a query leaves the code field alone
    /// (there is no reason you cannot type a code while the list loads) where a join does not,
    /// and only a join offers Cancel, because signing in and querying have nothing to hand back
    /// if you change your mind — Back already does everything cancelling them would.
    /// </summary>
    public readonly struct LobbyBusyState
    {
        public readonly bool LockCodeColumn;
        public readonly bool LockBrowser;
        public readonly bool LockRefresh;
        public readonly bool OfferCancel;

        private LobbyBusyState(bool codeColumn, bool browser, bool refresh, bool cancel)
        {
            LockCodeColumn = codeColumn;
            LockBrowser = browser;
            LockRefresh = refresh;
            OfferCancel = cancel;
        }

        public static LobbyBusyState For(LobbyBusyScope scope) => scope switch
        {
            LobbyBusyScope.SigningIn     => new LobbyBusyState(true,  true,  true,  false),
            LobbyBusyScope.Querying      => new LobbyBusyState(false, true,  true,  false),
            LobbyBusyScope.JoiningByCode => new LobbyBusyState(true,  true,  true,  true),
            LobbyBusyScope.JoiningRow    => new LobbyBusyState(true,  true,  true,  true),
            _                            => new LobbyBusyState(false, false, false, false)
        };
    }
}
