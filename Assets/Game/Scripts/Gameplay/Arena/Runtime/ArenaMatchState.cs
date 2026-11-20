using System;
using Unity.Netcode;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// Everything a peer needs to draw the arena's scoreboard, replicated whole by
    /// <see cref="ArenaMatch"/>. Small enough that sending all of it on every change is cheaper
    /// than reasoning about which part changed — and a late joiner gets it in the spawn payload.
    /// </summary>
    public struct ArenaMatchState : INetworkSerializable, IEquatable<ArenaMatchState>
    {
        public ArenaPhase Phase;

        /// <summary>Which round is being played or was just played, from 1. 0 before the first.</summary>
        public byte Round;

        public byte ScoreTeam0;
        public byte ScoreTeam1;

        public byte AliveTeam0;
        public byte AliveTeam1;

        /// <summary>
        /// Who took the round just decided: a team index, <see cref="ArenaRules.Draw"/>, or
        /// <see cref="ArenaRules.Undecided"/>. In <see cref="ArenaPhase.MatchOver"/> it is the match winner.
        /// </summary>
        public sbyte RoundWinner;

        /// <summary>Server time the current timed phase (countdown, round over) ends at; 0 when untimed.</summary>
        public double PhaseEndsAt;

        /// <summary>
        /// The round in play is a practice round the host started alone: unscored, ending only when
        /// everyone in it is down, and replaced by a real round as soon as both sides have a player.
        /// </summary>
        public bool Practice;

        public int ScoreOf(int team) => team == 0 ? ScoreTeam0 : ScoreTeam1;

        public int AliveOf(int team) => team == 0 ? AliveTeam0 : AliveTeam1;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref ScoreTeam0);
            serializer.SerializeValue(ref ScoreTeam1);
            serializer.SerializeValue(ref AliveTeam0);
            serializer.SerializeValue(ref AliveTeam1);
            serializer.SerializeValue(ref RoundWinner);
            serializer.SerializeValue(ref PhaseEndsAt);
            serializer.SerializeValue(ref Practice);
        }

        public bool Equals(ArenaMatchState other) =>
            Phase == other.Phase && Round == other.Round &&
            ScoreTeam0 == other.ScoreTeam0 && ScoreTeam1 == other.ScoreTeam1 &&
            AliveTeam0 == other.AliveTeam0 && AliveTeam1 == other.AliveTeam1 &&
            RoundWinner == other.RoundWinner && PhaseEndsAt.Equals(other.PhaseEndsAt) &&
            Practice == other.Practice;

        public override bool Equals(object obj) => obj is ArenaMatchState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(HashCode.Combine(Phase, Round, ScoreTeam0, ScoreTeam1, AliveTeam0, AliveTeam1, RoundWinner),
                             PhaseEndsAt, Practice);
    }
}
