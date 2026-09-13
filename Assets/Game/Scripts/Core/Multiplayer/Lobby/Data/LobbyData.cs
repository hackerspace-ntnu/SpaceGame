using System.Globalization;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Reads one value off a lobby or a player without ever throwing.
    ///
    /// Every reader in this folder needs the same guard: a player object written by an older
    /// build, or one still mid-join, may not carry a key at all, and an unguarded indexer threw
    /// KeyNotFoundException every poll and killed the roster. Numbers have a second way to go
    /// wrong — the value has to parse — so they take a fallback and hand it back for anything
    /// unreadable. Invariant culture throughout: the value crosses machines.
    /// </summary>
    internal static class LobbyData
    {
        public static string Text(Lobby lobby, string key) =>
            lobby?.Data != null && lobby.Data.TryGetValue(key, out DataObject value)
                ? value.Value
                : null;

        public static string Text(Player player, string key) =>
            player?.Data != null && player.Data.TryGetValue(key, out PlayerDataObject value)
                ? value.Value
                : null;

        public static int Int(Lobby lobby, string key, int fallback) => Parse(Text(lobby, key), fallback);

        public static int Int(Player player, string key, int fallback) => Parse(Text(player, key), fallback);

        public static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static int Parse(string text, int fallback) =>
            text != null
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
    }
}
