// The bounty's arithmetic, apart from any scene so it can be tested on its own: which tip a resident gives, where the
// search area sits, how many stand at a hunt's site, when a hunt fails and starts over, and what a hand-in pays. Every machine
// works these out the same way, so none of it is sent.
using System.Collections.Generic;
using System.Globalization;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.World.Posters;
using UnityEngine;

namespace SpaceGame.World.Bounties
{
    public static class BountyRules
    {
        /// <summary>Between a spot's tag and its place among the site's spots of that tag, in a cast piece's saved spot.</summary>
        public const char SpotSeparator = '#';

        // Knuth's multiplicative hash: spreads neighbouring resident indices across the tips.
        private const uint IndexSpread = 2654435761u;

        // The search area's centre is never nearer the hideout than this share of its offset radius.
        private const float NearestOffset = 0.4f;

        /// <summary>
        /// What a hunt's cast is drawn with: the same in one world every time its site loads, so a reload or a second
        /// wave never reshuffles who stands there, and different between worlds.
        /// </summary>
        public static int CastSeed(string worldId, string huntId) =>
            (int)RosterDraw.Hash(RosterDraw.StableHash(worldId), RosterDraw.StableHash(huntId));

        /// <summary>How many cast entry number <paramref name="entryIndex"/> stands: <paramref name="minCount"/> to <paramref name="maxCount"/>, fixed by the seed.</summary>
        public static int CastCount(int seed, int entryIndex, int minCount, int maxCount)
        {
            int least = Mathf.Max(0, minCount);
            return maxCount <= least ? least : least + RosterDraw.IndexFor(seed, entryIndex, maxCount - least + 1);
        }

        /// <summary>A cast piece's saved spot: the spot's tag and its place among the site's spots of that tag, as <c>tag#place</c>.</summary>
        public static string SpotKey(string tag, int place) =>
            (tag ?? string.Empty) + SpotSeparator + place.ToString(CultureInfo.InvariantCulture);

        /// <summary>Reads a <see cref="SpotKey"/>. False for an empty one: a piece scattered round the last spot of its tag.</summary>
        public static bool TryReadSpotKey(string key, out string tag, out int place)
        {
            tag = string.Empty;
            place = -1;
            int split = string.IsNullOrEmpty(key) ? -1 : key.LastIndexOf(SpotSeparator);
            if (split < 0 || !int.TryParse(key.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out place))
                return false;

            tag = key.Substring(0, split);
            return true;
        }

        /// <summary>Whether <paramref name="point"/> is within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
        public static bool FlatWithin(Vector3 point, Vector3 centre, float radius)
        {
            Vector3 offset = point - centre;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Whether every one of <paramref name="players"/> is outside the hunt's bounds. False with nobody to ask: a
        /// world still binding its players is not a crew that walked away.
        /// </summary>
        public static bool AllOutside(IReadOnlyList<Transform> players, Vector3 centre, float radius)
        {
            bool anyone = false;
            foreach (Transform player in players)
            {
                if (player == null) continue;
                if (FlatWithin(player.position, centre, radius)) return false;
                anyone = true;
            }
            return anyone;
        }

        /// <summary>Whether the target died when the poster wanted it alive: that hunt cannot be handed in.</summary>
        public static bool KilledWhenWantedAlive(WantedStatus wanted, bool dead) => dead && wanted != WantedStatus.Dead;

        /// <summary>
        /// Why an open hunt whose cast stands fails now, or <see cref="BountyFailure.None"/>: the target fallen beneath the
        /// ground, the target killed when wanted alive, the cast standing too long, or every player out of its bounds too
        /// long. The most direct cause wins, so the crew are told what happened: a target that fell through the world and
        /// died of it is lost, not killed. The whole crew going down is no failure: the hunt waits for them.
        /// </summary>
        public static BountyFailure FailureOf(float elapsed, float outsideFor, float maxOpenSeconds, float outOfBoundsSeconds,
                                              bool targetKilled, bool targetLost)
        {
            if (targetLost) return BountyFailure.TargetLost;
            if (targetKilled) return BountyFailure.TargetKilled;
            if (elapsed >= maxOpenSeconds) return BountyFailure.OutOfTime;
            return outsideFor >= outOfBoundsSeconds ? BountyFailure.OutOfBounds : BountyFailure.None;
        }

        /// <summary>
        /// The tip resident number <paramref name="residentIndex"/> gives about <paramref name="bountyId"/>: always the
        /// same one, so asking twice repeats it and other tips come from other people.
        /// </summary>
        public static int TipIndex(string bountyId, int residentIndex, int tipCount)
        {
            if (tipCount <= 0) return 0;
            uint mixed = unchecked(LineTable.IdOf(bountyId) ^ ((uint)residentIndex * IndexSpread));
            return (int)(mixed % (uint)tipCount);
        }

        /// <summary>
        /// Where the visor's search area is centred: within <paramref name="offsetRadius"/> of the hideout, never on it,
        /// in the same place on every machine.
        /// </summary>
        public static Vector3 SearchCentre(string bountyId, Vector3 hideout, float offsetRadius)
        {
            uint hash = LineTable.IdOf(bountyId);
            float angle = (hash & 0xffff) / 65536f * Mathf.PI * 2f;
            float distance = Mathf.Lerp(NearestOffset, 1f, (hash >> 16) / 65536f) * offsetRadius;
            return hideout + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
        }

        /// <summary>Whether the target came in the way the poster asked for.</summary>
        public static bool BroughtAsWanted(WantedStatus wanted, bool dead) => dead == (wanted == WantedStatus.Dead);

        /// <summary>How many of a reward of <paramref name="count"/> are paid: all of it as wanted, a share otherwise.</summary>
        public static int Payout(int count, bool asWanted, float otherStateShare) =>
            asWanted ? count : Mathf.FloorToInt(count * Mathf.Clamp01(otherStateShare));

        /// <summary>Whether a hunt at <paramref name="index"/> can be taken now as far as the open one goes: none is. Whether it was claimed is the board's.</summary>
        public static bool CanTake(in BountyProgress progress, int index) =>
            index >= 0 && !progress.HasOpen;

        /// <summary>The nearest of <paramref name="candidates"/> within <paramref name="within"/> metres of <paramref name="from"/>, or null.</summary>
        public static Transform NearestWithin(IReadOnlyList<Transform> candidates, Vector3 from, float within)
        {
            Transform nearest = null;
            float best = within * within;
            foreach (Transform candidate in candidates)
            {
                if (candidate == null) continue;
                float d = (candidate.position - from).sqrMagnitude;
                if (d > best) continue;
                best = d;
                nearest = candidate;
            }
            return nearest;
        }
    }
}
