// The list of every place in the world worth going to, and the only thing NPC tasks ask for
// destinations.
//
// It is a flat static list of records rather than anything scene-shaped, for one reason: the world
// is 48 chunk scenes that stream in and out, and an NPC walking 2 km spends nearly all of that
// journey with its destination's chunk unloaded. Anything holding a Transform would be holding
// null. A record outlives the GameObject that registered it, which is the whole point.
//
// Nothing in here knows where sites come from. Today a WorldSiteMarker component registers them on
// enable; when procedural world generation lands it registers them directly and not one line below
// changes. That seam is the reason this is a registry and not a scene scan.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class WorldSiteRegistry
    {
        private static readonly List<WorldSite> sites = new();
        private static readonly Dictionary<string, int> indexById = new();

        // Reused by every query so a task picking a destination allocates nothing. Safe to share:
        // each query fills and fully consumes it inside one synchronous call.
        private static readonly List<int> scratch = new();

        /// <summary>Raised whenever the set changes, so anything caching a lookup can drop it.</summary>
        public static event Action Changed;

        public static int Count => sites.Count;

        public static IReadOnlyList<WorldSite> All => sites;

        /// <summary>
        /// Wipe the registry when play starts.
        ///
        /// Static state survives leaving play mode when domain reload is off, which would carry
        /// every site from the previous session into the next one — including sites from a
        /// different world. A stale site is worse than a missing one: NPCs would set off toward a
        /// place that is not in this world at all and walk until they hit the edge of the NavMesh.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            sites.Clear();
            indexById.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// Add a place, or update it if <paramref name="id"/> is already known.
        ///
        /// Re-registering under a known id is the ordinary case, not an error: a marker's chunk can
        /// stream in and out several times in a session, and each time it enables it offers its
        /// site again. Treating that as an update rather than a duplicate is what keeps a caravan
        /// from finding three copies of the same well.
        /// </summary>
        public static string Register(SiteKind kind, Vector3 position, float radius, string name, string id = null)
        {
            if (string.IsNullOrEmpty(id))
                id = Guid.NewGuid().ToString("N");

            var site = new WorldSite(id, kind, position, radius, name);

            if (indexById.TryGetValue(id, out int existing))
            {
                sites[existing] = site;
            }
            else
            {
                indexById[id] = sites.Count;
                sites.Add(site);
            }

            Changed?.Invoke();
            return id;
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id) || !indexById.TryGetValue(id, out int index))
                return;

            // Swap-remove, then repair the moved entry's index. Order in this list is not meaningful
            // to anything — every query sorts or scores for itself.
            int last = sites.Count - 1;
            if (index != last)
            {
                sites[index] = sites[last];
                indexById[sites[index].Id] = index;
            }

            sites.RemoveAt(last);
            indexById.Remove(id);
            Changed?.Invoke();
        }

        public static bool Contains(string id) => !string.IsNullOrEmpty(id) && indexById.ContainsKey(id);

        public static bool TryGet(string id, out WorldSite site)
        {
            if (!string.IsNullOrEmpty(id) && indexById.TryGetValue(id, out int index))
            {
                site = sites[index];
                return true;
            }

            site = default;
            return false;
        }

        /// <summary>
        /// The closest site of <paramref name="kind"/>, or false if there is none in range.
        ///
        /// <paramref name="excludeId"/> is how a task avoids sending an NPC straight back to the
        /// place it has just finished working — without it, "nearest scrap field" is a stable
        /// answer and a scavenger stands on one heap forever.
        /// </summary>
        public static bool TryFindNearest(SiteKind kind, Vector3 from, float maxDistance,
                                          out WorldSite site, string excludeId = null)
        {
            site = default;
            float best = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < sites.Count; i++)
            {
                WorldSite candidate = sites[i];
                if (candidate.Kind != kind) continue;
                if (excludeId != null && candidate.Id == excludeId) continue;

                float distance = candidate.FlatDistanceTo(from);
                if (distance > maxDistance || distance >= best) continue;

                best = distance;
                site = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Any site of <paramref name="kind"/> in range, chosen at random.
        ///
        /// Preferred over <see cref="TryFindNearest"/> for task destinations. Nearest is
        /// deterministic, and a group of NPCs sharing a home and a task list that all query nearest
        /// walk the same route to the same place forever. Random over a wide radius is what makes
        /// the world look like it has people going about separate business in it.
        /// </summary>
        public static bool TryFindRandom(SiteKind kind, Vector3 from, float maxDistance,
                                         out WorldSite site, string excludeId = null)
        {
            scratch.Clear();

            for (int i = 0; i < sites.Count; i++)
            {
                WorldSite candidate = sites[i];
                if (candidate.Kind != kind) continue;
                if (excludeId != null && candidate.Id == excludeId) continue;
                if (candidate.FlatDistanceTo(from) > maxDistance) continue;

                scratch.Add(i);
            }

            if (scratch.Count == 0)
            {
                site = default;
                return false;
            }

            site = sites[scratch[UnityEngine.Random.Range(0, scratch.Count)]];
            return true;
        }

        /// <summary>Every site of a kind within range, appended to <paramref name="results"/>.</summary>
        public static int Query(SiteKind kind, Vector3 from, float maxDistance, List<WorldSite> results)
        {
            if (results == null) return 0;

            int added = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                WorldSite candidate = sites[i];
                if (candidate.Kind != kind) continue;
                if (candidate.FlatDistanceTo(from) > maxDistance) continue;

                results.Add(candidate);
                added++;
            }

            return added;
        }

        public static int CountOf(SiteKind kind)
        {
            int total = 0;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i].Kind == kind) total++;

            return total;
        }
    }
}
