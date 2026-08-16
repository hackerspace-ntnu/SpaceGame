// One row of the match leaderboard.
//
// Serializable so the server can broadcast the whole table to clients in a single RPC. Scores
// only change when something dies, so sending the table wholesale is cheaper in both bandwidth
// and complexity than a NetworkList with per-element deltas.
using Unity.Collections;
using Unity.Netcode;

namespace SpaceGame.Gameplay
{
    public struct MatchScoreEntry : INetworkSerializable
    {
        // ClientId value used for bots, which have no owning peer.
        public const ulong NoClient = ulong.MaxValue;

        // Display name assigned at spawn ("Bot 07", "Player 2"). Deliberately not "You" — each peer
        // resolves that itself by comparing ClientId, so one table is correct for everyone.
        public FixedString32Bytes Name;
        public int Kills;
        public int Deaths;
        public int Team;
        public ulong ClientId;

        public bool IsBot => ClientId == NoClient;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref ClientId);
        }

        // Best first: kills descending, then fewest deaths, then name so the order is stable between
        // rebuilds instead of flickering between entries that tied.
        public static int Compare(MatchScoreEntry a, MatchScoreEntry b)
        {
            int byKills = b.Kills.CompareTo(a.Kills);
            if (byKills != 0) return byKills;

            int byDeaths = a.Deaths.CompareTo(b.Deaths);
            if (byDeaths != 0) return byDeaths;

            return string.CompareOrdinal(a.Name.ToString(), b.Name.ToString());
        }
    }
}
