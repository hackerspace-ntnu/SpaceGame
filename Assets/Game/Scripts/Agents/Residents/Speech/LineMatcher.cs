// Picks the one line a resident says, Valve-response-system style: every row whose filled criteria
// all hold is a candidate, the most specific candidates win, and a seeded pick among the winners
// keeps the same situation from always producing the same sentence.
//
// Pure: no Unity calls, no state. The inspector re-runs it with runnersUp to show why a line won.
using System.Collections.Generic;

namespace SpaceGame.Agents.Residents
{
    /// <summary>The facts a line is matched against: who speaks, about what, how they feel, what they see.</summary>
    public struct LineQuery
    {
        public string person, archetype;
        public Topic topic;
        public Stance stance;
        public Observation observation;
        public Activity activity;
        public Register? repliesTo;
        public int seed;
    }

    public static class LineMatcher
    {
        /// <summary>A row written for this named resident.</summary>
        public const int PersonScore = 8;

        /// <summary>A row written for this resident's archetype.</summary>
        public const int ArchetypeScore = 4;

        /// <summary>Each other filled criterion that holds.</summary>
        public const int CriterionScore = 1;

        /// <summary>
        /// The line to say, or null when nothing matches. Lines in <paramref name="recentlySaid"/> are
        /// passed over while anything else matches — a repeat is better than silence, a fresh line is
        /// better than a repeat even when it is a little less specific.
        /// </summary>
        public static LineRow Pick(LineTable t, in LineQuery q, IReadOnlyCollection<uint> recentlySaid,
                                   out int score, List<(LineRow, int)> runnersUp = null)
        {
            score = 0;
            runnersUp?.Clear();
            if (t == null) return null;

            var fresh = new List<(LineRow row, int score)>();
            var stale = new List<(LineRow row, int score)>();
            foreach (LineRow row in t.Rows)
            {
                int s = Score(row, q);
                if (s < 0) continue;
                bool said = recentlySaid != null && Contains(recentlySaid, row.id);
                (said ? stale : fresh).Add((row, s));
            }

            List<(LineRow row, int score)> pool = fresh.Count > 0 ? fresh : stale;
            if (pool.Count == 0) return null;

            int best = int.MinValue;
            foreach (var c in pool) if (c.score > best) best = c.score;

            var top = pool.FindAll(c => c.score == best);
            LineRow winner = top[(int)(Mix(q.seed) % (uint)top.Count)].row;
            score = best;

            if (runnersUp != null)
            {
                foreach (var c in fresh) if (c.row != winner) runnersUp.Add((c.row, c.score));
                foreach (var c in stale) if (c.row != winner) runnersUp.Add((c.row, c.score));
                runnersUp.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            }

            return winner;
        }

        /// <summary>The row's specificity for <paramref name="q"/>, or -1 when a filled criterion fails.</summary>
        public static int Score(LineRow row, in LineQuery q)
        {
            int s = 0;
            if (row.speaker != null)
            {
                if (Same(row.speaker, q.person)) s += PersonScore;
                else if (Same(row.speaker, q.archetype)) s += ArchetypeScore;
                else return -1;
            }

            if (row.topic.HasValue && !Holds(row.topic.Value == q.topic, ref s)) return -1;
            if (row.stance != null && !Holds(row.MatchesStance(q.stance), ref s)) return -1;
            if (row.observation.HasValue && !Holds(row.observation.Value == q.observation, ref s)) return -1;
            if (row.activity.HasValue && !Holds(row.activity.Value == q.activity, ref s)) return -1;
            if (row.repliesTo.HasValue && !Holds(q.repliesTo == row.repliesTo.Value, ref s)) return -1;
            return s;
        }

        private static bool Holds(bool criterion, ref int s)
        {
            if (criterion) s += CriterionScore;
            return criterion;
        }

        private static bool Same(string a, string b) =>
            !string.IsNullOrEmpty(b) && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        private static bool Contains(IReadOnlyCollection<uint> ids, uint id)
        {
            if (ids is ICollection<uint> collection) return collection.Contains(id);
            foreach (uint said in ids) if (said == id) return true;
            return false;
        }

        /// <summary>Spreads neighbouring seeds apart, so seed and seed + 1 rarely pick the same row.</summary>
        private static uint Mix(int seed)
        {
            uint x = unchecked((uint)seed);
            x ^= x >> 16;
            x = unchecked(x * 0x7feb352du);
            x ^= x >> 15;
            x = unchecked(x * 0x846ca68bu);
            x ^= x >> 16;
            return x;
        }
    }
}
