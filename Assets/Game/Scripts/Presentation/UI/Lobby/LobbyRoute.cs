namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>Which door into the lobby the player took: host or join, story or VS.</summary>
    public enum LobbyRoute { StoryHost, StoryJoin, VersusHost, VersusJoin }

    /// <summary>
    /// What a <see cref="LobbyRoute"/> means, in the terms the rest of the screen needs.
    ///
    /// Small on purpose: the route is a value from a fixed set of four, and every question the
    /// lobby asks of it is answerable by a switch rather than by state stashed somewhere else.
    /// It is carried explicitly rather than inferred from whether a world is staged, because a VS
    /// host stages no world at all and that inference sent every VS host to the browser to look
    /// for their own session.
    /// </summary>
    public static class LobbyRouteExtensions
    {
        public static bool IsHosting(this LobbyRoute route) =>
            route == LobbyRoute.StoryHost || route == LobbyRoute.VersusHost;

        public static bool IsVersus(this LobbyRoute route) =>
            route == LobbyRoute.VersusHost || route == LobbyRoute.VersusJoin;

        /// <summary>Whether a lobby found in the browser belongs on a route's list.</summary>
        public static bool Accepts(this LobbyRoute route, bool lobbyIsVersus) =>
            route.IsVersus() == lobbyIsVersus;
    }
}
