// Who walks with whom. Derived from the roster alone — never stored, never saved, the same answer on every
// rebuild — so regenerating a settlement or adding a resident re-pairs everyone without a migration.
// Guards pair off in roster order (two to a pair, an odd one patrols alone); roamers pair with a friend or
// relative who is also a free roamer. The lower index leads; the other follows it and shares its day.
using System.Collections.Generic;

namespace SpaceGame.Agents.Residents
{
    /// <summary>One resident as the pairing sees it: an index, what kind of walker it is, and whom it likes.</summary>
    public readonly struct CompanionCandidate
    {
        public readonly int index;
        public readonly bool patrols, roams;
        public readonly IReadOnlyList<int> friends;

        public CompanionCandidate(int index, bool patrols, bool roams, IReadOnlyList<int> friends) =>
            (this.index, this.patrols, this.roams, this.friends) = (index, patrols, roams, friends);
    }

    public static class Companions
    {
        /// <summary>Follower index → leader index, for every resident who walks with a leader.</summary>
        public static Dictionary<int, int> Pair(IReadOnlyList<CompanionCandidate> candidates)
        {
            var leaderOf = new Dictionary<int, int>();
            var sorted = new List<CompanionCandidate>(candidates);
            sorted.Sort((a, b) => a.index.CompareTo(b.index));

            var patrols = sorted.FindAll(c => c.patrols);
            for (int i = 0; i + 1 < patrols.Count; i += 2) leaderOf[patrols[i + 1].index] = patrols[i].index;

            var taken = new HashSet<int>();
            var roamers = new Dictionary<int, CompanionCandidate>();
            foreach (CompanionCandidate c in sorted)
                if (c.roams && !c.patrols) roamers[c.index] = c;

            foreach (CompanionCandidate c in sorted)
            {
                if (!roamers.ContainsKey(c.index) || taken.Contains(c.index) || c.friends == null) continue;
                foreach (int friend in c.friends)
                {
                    if (friend == c.index || !roamers.ContainsKey(friend) || taken.Contains(friend)) continue;
                    int leader = c.index < friend ? c.index : friend, follower = c.index < friend ? friend : c.index;
                    leaderOf[follower] = leader;
                    taken.Add(leader);
                    taken.Add(follower);
                    break;
                }
            }
            return leaderOf;
        }

        /// <summary>The patrol pair number of each patrolling resident: pairs count 0, 1, 2…; a solo guard takes the next.</summary>
        public static Dictionary<int, int> PatrolSlots(IReadOnlyList<CompanionCandidate> candidates, out int slots)
        {
            var sorted = new List<CompanionCandidate>(candidates.Count);
            foreach (CompanionCandidate c in candidates)
                if (c.patrols) sorted.Add(c);
            sorted.Sort((a, b) => a.index.CompareTo(b.index));

            var slotOf = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++) slotOf[sorted[i].index] = i / 2;
            slots = (sorted.Count + 1) / 2;
            return slotOf;
        }
    }
}
