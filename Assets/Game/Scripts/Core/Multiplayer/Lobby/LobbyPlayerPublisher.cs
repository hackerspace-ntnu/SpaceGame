using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using SpaceGame.Characters;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Everything the local player says about themselves to the lobby — suit colour, team, team
    /// colour — each debounced on its own clock.
    ///
    /// Nothing here paints anything. The local figure is repainted by the screen the instant an
    /// arrow is pressed, and this only tells everyone else; that split is what lets the cycler
    /// feel immediate while the service call is allowed to be slow. See
    /// <see cref="DebouncedPublish{T}"/> for why every one of these is debounced at all.
    /// </summary>
    public sealed class LobbyPlayerPublisher
    {
        /// <summary>Long enough to swallow a burst of arrow presses, short enough to feel immediate.</summary>
        private const float Debounce = 0.75f;

        private readonly DebouncedPublish<int> suitColor = new(Debounce);
        private readonly DebouncedPublish<int> team = new(Debounce);
        private readonly DebouncedPublish<int> teamColor = new(Debounce);

        /// <summary>This player's suit colour, in a story lobby.</summary>
        public void RequestSuitColor(int swatch) => suitColor.Request(SuitPalette.Clamp(swatch));

        /// <summary>Which team this player has moved to, in a VS lobby.</summary>
        public void RequestTeam(int newTeam) => team.Request(newTeam);

        /// <summary>This player's opinion of their VS team's colour.</summary>
        public void RequestTeamColor(int swatch) => teamColor.Request(SuitPalette.Clamp(swatch));

        /// <summary>
        /// Forgets everything waiting. For when there is no lobby to publish to — left, or not
        /// yet signed in — because a value that fired anyway would land on whatever lobby this
        /// peer joins next, not the one it was meant for.
        /// </summary>
        public void Cancel()
        {
            suitColor.Cancel();
            team.Cancel();
            teamColor.Cancel();
        }

        /// <summary>Sends whatever is due, through <paramref name="send"/>, once each has stopped being pressed.</summary>
        public void Tick(float deltaTime, Func<UpdatePlayerOptions, Task> send)
        {
            suitColor.Tick(deltaTime, swatch => send(LobbyOptions.SuitColor(swatch)));
            team.Tick(deltaTime, newTeam => send(LobbyOptions.Team(newTeam)));
            teamColor.Tick(deltaTime, swatch => send(LobbyOptions.TeamColor(swatch, NowMs())));
        }

        /// <summary>The stamp on a team-colour opinion — see <see cref="TeamColorOpinion"/>.</summary>
        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
