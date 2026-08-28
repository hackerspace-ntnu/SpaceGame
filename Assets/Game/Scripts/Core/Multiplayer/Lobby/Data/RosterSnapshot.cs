using System;

namespace SpaceGame.Core
{
    /// <summary>
    /// Everything a lobby view needs to draw the roster, taken off a <c>Lobby</c> once per poll.
    ///
    /// <para>
    /// Deliberately free of <c>Unity.Services.Lobbies.Models.Lobby</c> and everything else the SDK
    /// carries. Building one is <see cref="LobbySession"/>'s job — see its <c>Snapshot</c> method —
    /// and keeping this struct itself off the SDK is what lets the views that read it be exercised
    /// without a network, an authentication service, or UGS at all: construct one by hand in a test
    /// or an editor preview, and every view built against it runs unchanged.
    /// </para>
    ///
    /// <para>
    /// Every array defaults to <see cref="Array.Empty{T}"/> rather than null when the constructor is
    /// handed null, and every accessor is index-guarded — the same defensiveness
    /// <c>LobbySessionOptions</c>'s readers already need, for the same reason: a lobby mid-poll, a
    /// peer on an older build, or a team index nobody has claimed yet must never turn into a thrown
    /// exception that kills the roster.
    /// </para>
    /// </summary>
    public readonly struct RosterSnapshot
    {
        /// <summary>Names, in lobby order.</summary>
        public readonly string[] Names;

        /// <summary>Suit colours, index-aligned with <see cref="Names"/>.</summary>
        public readonly int[] SuitColors;

        /// <summary>Which team each player stands on, index-aligned with <see cref="Names"/>.</summary>
        public readonly int[] Teams;

        /// <summary>One swatch per team, index-aligned with team number.</summary>
        public readonly int[] TeamColors;

        /// <summary>Heads per team, index-aligned with team number.</summary>
        public readonly int[] Occupancy;

        public readonly int TeamCount;
        public readonly int TeamSize;

        /// <summary>Which row of the roster is us, or -1.</summary>
        public readonly int LocalSlot;

        /// <summary>Which row of the roster is the host, or -1.</summary>
        public readonly int HostSlot;

        public readonly bool IsVersus;

        public RosterSnapshot(string[] names, int[] suitColors, int[] teams, int[] teamColors,
            int[] occupancy, int teamCount, int teamSize, int localSlot, int hostSlot, bool isVersus)
        {
            Names = names ?? Array.Empty<string>();
            SuitColors = suitColors ?? Array.Empty<int>();
            Teams = teams ?? Array.Empty<int>();
            TeamColors = teamColors ?? Array.Empty<int>();
            Occupancy = occupancy ?? Array.Empty<int>();
            TeamCount = teamCount;
            TeamSize = teamSize;
            LocalSlot = localSlot;
            HostSlot = hostSlot;
            IsVersus = isVersus;
        }

        /// <summary>Which team we stand on, or -1 when <see cref="LocalSlot"/> cannot be answered.</summary>
        public int LocalTeam => TeamOf(LocalSlot);

        /// <summary>The swatch a team wears, or 0 for a team number this snapshot has no opinion on.</summary>
        public int ColorOfTeam(int team) =>
            team >= 0 && team < TeamColors.Length ? TeamColors[team] : 0;

        /// <summary>Heads standing on a team, or 0 for a team number this snapshot has no opinion on.</summary>
        public int HeadsOn(int team) =>
            team >= 0 && team < Occupancy.Length ? Occupancy[team] : 0;

        /// <summary>Whether a team has a free seat under this lobby's team size.</summary>
        public bool HasRoomOn(int team) => HeadsOn(team) < TeamSize;

        private int TeamOf(int slot) => slot >= 0 && slot < Teams.Length ? Teams[slot] : -1;
    }
}
