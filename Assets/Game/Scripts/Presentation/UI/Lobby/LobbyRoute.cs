namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>Which door into the lobby the player took: host or join; story, VS or arena.</summary>
    public enum LobbyRoute { StoryHost, StoryJoin, VersusHost, VersusJoin, ArenaHost, ArenaJoin }

    /// <summary>
    /// What a <see cref="LobbyRoute"/> means, in the terms the rest of the screen needs.
    ///
    /// Small on purpose: the route is a value from a fixed set, and every question the
    /// lobby asks of it is answerable by a switch rather than by state stashed somewhere else.
    /// It is carried explicitly rather than inferred from whether a world is staged, because a VS
    /// host stages no world at all and that inference sent every VS host to the browser to look
    /// for their own session.
    /// </summary>
    public static class LobbyRouteExtensions
    {
        public static bool IsHosting(this LobbyRoute route) =>
            route == LobbyRoute.StoryHost || route == LobbyRoute.VersusHost || route == LobbyRoute.ArenaHost;

        /// <summary>Whether the route has teams. An arena is a versus lobby locked to two of them, so it counts.</summary>
        public static bool IsVersus(this LobbyRoute route) =>
            route == LobbyRoute.VersusHost || route == LobbyRoute.VersusJoin || route.IsArena();

        public static bool IsArena(this LobbyRoute route) =>
            route == LobbyRoute.ArenaHost || route == LobbyRoute.ArenaJoin;

        /// <summary>Whether a lobby found in the browser belongs on a route's list.</summary>
        public static bool Accepts(this LobbyRoute route, bool lobbyIsVersus, bool lobbyIsArena) =>
            route.IsVersus() == lobbyIsVersus && route.IsArena() == lobbyIsArena;
    }
}
