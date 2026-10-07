// Which team a voice belongs to, and that team's colour — for everything that draws a speaker.
//
// Two sources, and which one is true depends on where you are:
//
// - In the world: PlayerIdentity.Team, which the server assigns, and VersusSession's resolved team
//   colours.
// - In the lobby: neither exists yet. persistentScene has not loaded, and VersusSession is only
//   filled in as the match begins. The lobby's own roster knows every team and colour, indexed by
//   lobby slot, and VoiceSession.AccountOf is what turns a netcode client id into that slot.
//
// Story mode has no teams, so everything here answers "no team" and callers fall back to the accent.
//
// A team is shown by its colour AND its tag ("T3", from TagOf) — GDC-L1-UX-0006: never encode
// information in colour alone. With up to eight teams, some swatches are hard to tell apart for a
// colour-blind player, and the tag is what still reads for them.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;
using SpaceGame.Voice;

namespace SpaceGame.Presentation
{
    /// <summary>Team membership and colour for a netcode client id, lobby or world.</summary>
    internal static class SpeakerTeams
    {
        public const int NoTeam = -1;

        // Built once per team: TagOf is asked for every speaking row every frame, and concatenating
        // a fresh string each time would be garbage for no reason.
        private static readonly string[] tags = new string[VersusRules.MaxTeams];

        // The lobby snapshot allocates on every call, and a speaking list asks about each speaker
        // every frame — so it is taken once per frame and reused.
        private static int snapshotFrame = -1;
        private static bool snapshotValid;
        private static RosterSnapshot snapshot;

        /// <summary>The local player's team, or <see cref="NoTeam"/> outside a versus match.</summary>
        public static int LocalTeam
        {
            get
            {
                if (VersusSession.IsActive) return VersusSession.LocalTeam;
                return TryLobby(out RosterSnapshot lobby) && lobby.IsVersus ? lobby.LocalTeam : NoTeam;
            }
        }

        /// <summary><paramref name="clientId"/>'s team, or <see cref="NoTeam"/>.</summary>
        public static int TeamOf(ulong clientId)
        {
            if (VersusSession.IsActive) return WorldTeamOf(clientId);
            if (!TryLobby(out RosterSnapshot lobby) || !lobby.IsVersus) return NoTeam;

            int slot = LobbyRoster.SlotOf(LobbySession.Existing.Current, VoiceSession.AccountOf(clientId));
            return slot >= 0 && slot < lobby.Teams.Length ? lobby.Teams[slot] : NoTeam;
        }

        /// <summary>
        /// "T3" for team index 2 — the same numbering <see cref="VersusRules.TeamName"/> uses
        /// ("TEAM 3"), in the room a speaking-list row has. Empty for no team.
        /// </summary>
        public static string TagOf(int team)
        {
            if (team < 0) return string.Empty;
            if (team >= tags.Length) return "T" + (team + 1);

            return tags[team] ??= "T" + (team + 1);
        }

        /// <summary>The colour <paramref name="team"/> plays in. False when it has none.</summary>
        public static bool TryColorOf(int team, out Color color)
        {
            color = default;
            if (team < 0) return false;

            if (VersusSession.IsActive)
            {
                color = SuitPalette.ColorOf(VersusSession.ColorOf(team));
                return true;
            }

            if (!TryLobby(out RosterSnapshot lobby) || !lobby.IsVersus) return false;

            color = SuitPalette.ColorOf(lobby.ColorOfTeam(team));
            return true;
        }

        private static int WorldTeamOf(ulong clientId)
        {
            var players = PlayerIdentity.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerIdentity player = players[i];
                if (player != null && player.IsSpawned && player.OwnerClientId == clientId)
                    return player.Team;
            }

            return NoTeam;
        }

        private static bool TryLobby(out RosterSnapshot lobby)
        {
            if (snapshotFrame != Time.frameCount)
            {
                snapshotFrame = Time.frameCount;

                // Existing, never Instance: Instance creates a lobby session on touch, and merely
                // asking which team someone is on must not conjure one in singleplayer.
                LobbySession session = LobbySession.Existing;
                snapshotValid = session != null && session.Current != null;
                if (snapshotValid) snapshot = session.CurrentSnapshot();
            }

            lobby = snapshot;
            return snapshotValid;
        }
    }
}
