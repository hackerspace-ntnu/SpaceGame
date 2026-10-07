using System.Collections.Generic;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Which team each client is on, decided by the server.
    ///
    /// <para>
    /// The seam between "who chose what in the lobby" and "where the spawner puts them". Today it
    /// fills teams evenly in arrival order, which is enough to play a versus match and no more.
    /// The lobby already knows real choices — <c>LobbyTeams.Occupancy</c> reads them off the Unity
    /// Lobby — and carrying those into the session is its own piece of work; when it lands, this is
    /// the only thing that changes.
    /// </para>
    ///
    /// <para>
    /// Server-side. Nothing here checks that, because it is plain arithmetic with no way to know —
    /// the caller is <c>NetworkGameManager</c>'s spawn flow, which already runs only on the server.
    /// What every peer sees is the team published on <c>PlayerIdentity</c> afterwards.
    /// </para>
    /// </summary>
    public static class VersusTeamRoster
    {
        private static readonly Dictionary<ulong, int> teamByClient = new();

        /// <summary>
        /// Records the team a client actually chose in the lobby, which outranks anything
        /// <see cref="Assign"/> would have picked for them.
        ///
        /// <para>
        /// This is the difference between a versus match and a shuffle. Players pick their side on
        /// the lobby roster and expect to start beside the people they picked it with; balancing
        /// them onto whichever team is emptiest would silently split a party up. Round-robin is the
        /// fallback for someone who never said, not the rule.
        /// </para>
        ///
        /// <para>
        /// The claim arrives from the client that it is about, so it is checked rather than
        /// believed: a team outside the match's own count is dropped and that client falls back to
        /// being assigned. It picks which side someone starts on and nothing else — there is no
        /// authority here worth stealing — but a nonsense index would index a real array.
        /// </para>
        /// </summary>
        public static void Claim(ulong clientId, int team, int teamCount)
        {
            if (team < 0 || team >= teamCount) return;

            teamByClient[clientId] = team;
        }

        /// <summary>
        /// The team this client belongs to, choosing one on first ask and answering the same way
        /// every time after — including for a client whose own choice was recorded by
        /// <see cref="Claim"/>, which is stored in the same place and so is simply returned.
        ///
        /// <para>
        /// Idempotent on purpose. A client's spawn flow can run more than once across a session —
        /// a reconnect comes back on the same client id, since Netcode hands out the lowest free
        /// one — and a player who changed sides on rejoining would be spawning inside the enemy's
        /// ship.
        /// </para>
        ///
        /// <para>
        /// Fills the emptiest team rather than counting arrivals, so the sides stay even when
        /// somebody leaves. Ties go to the lowest team number, which makes a fresh lobby fill
        /// 0, 1, 0, 1 — the round robin anyone would expect — without that being a separate rule.
        /// </para>
        /// </summary>
        public static int Assign(ulong clientId, int teamCount)
        {
            if (teamCount <= 0) teamCount = VersusRules.MinTeams;

            if (teamByClient.TryGetValue(clientId, out int existing))
                return existing;

            var occupancy = new int[teamCount];

            foreach (int team in teamByClient.Values)
                if (team >= 0 && team < teamCount) occupancy[team]++;

            int smallest = 0;

            for (int team = 1; team < teamCount; team++)
                if (occupancy[team] < occupancy[smallest]) smallest = team;

            teamByClient[clientId] = smallest;
            return smallest;
        }

        public static bool TryGet(ulong clientId, out int team) =>
            teamByClient.TryGetValue(clientId, out team);

        /// <summary>
        /// Forgets a client that has left, so its seat on a team frees up for whoever joins next.
        /// </summary>
        public static void Release(ulong clientId) => teamByClient.Remove(clientId);

        /// <summary>
        /// Empties the roster between matches. A static outlives the session that filled it, and a
        /// roster left standing would seed the next match's balance with the last one's players.
        /// </summary>
        public static void Clear() => teamByClient.Clear();
    }
}
