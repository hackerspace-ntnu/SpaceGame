// How many times a thing may throw before it is switched off, and over what span.
//
// Pure, and deliberately ignorant of Unity: the caller passes the clock in. That is what makes the
// decision testable without a frame loop, and it is the same split UnderTerrainRule makes against
// UnderTerrainGuard — the part that decides has no components in it.
//
// The window matters more than the count. A module that throws five times in one frame is broken and
// must stop; a module that throws once every thirty seconds is a bug worth fixing but not worth
// removing a creature's ability to walk over. Without the window the second case eventually reaches
// any threshold and gets quarantined for being slow, which is the guard causing the outage.
using System.Collections.Generic;

namespace SpaceGame.Diagnostics
{
    public sealed class FaultBudget
    {
        private struct Entry
        {
            public int Count;
            public float WindowStart;
            public bool Quarantined;
        }

        private readonly Dictionary<string, Entry> entries = new();

        public FaultBudget(int maxFaults, float windowSeconds)
        {
            // Clamped rather than trusted: a max of 0 would quarantine everything on its first fault
            // and a negative window would restart the window on every call, so both turn the guard
            // into the outage it exists to prevent.
            MaxFaults = maxFaults > 0 ? maxFaults : 1;
            WindowSeconds = windowSeconds > 0f ? windowSeconds : 0f;
        }

        public int MaxFaults { get; }

        public float WindowSeconds { get; }

        /// <summary>
        /// Counts one fault against <paramref name="key"/>.
        /// Returns true only on the call that trips quarantine — never again for that key, so a
        /// thing throwing every frame produces one quarantine line and not sixty a second.
        /// </summary>
        public bool Record(string key, float now)
        {
            if (string.IsNullOrEmpty(key)) return false;

            entries.TryGetValue(key, out Entry entry);

            if (entry.Count == 0 || now - entry.WindowStart > WindowSeconds)
            {
                entry.Count = 1;
                entry.WindowStart = now;
            }
            else
            {
                entry.Count++;
            }

            bool trips = !entry.Quarantined && entry.Count >= MaxFaults;
            if (trips) entry.Quarantined = true;

            entries[key] = entry;
            return trips;
        }

        public bool IsQuarantined(string key) =>
            !string.IsNullOrEmpty(key) && entries.TryGetValue(key, out Entry e) && e.Quarantined;

        /// <summary>Faults counted against <paramref name="key"/> in its current window.</summary>
        public int CountFor(string key) =>
            !string.IsNullOrEmpty(key) && entries.TryGetValue(key, out Entry e) ? e.Count : 0;

        public void Clear() => entries.Clear();
    }
}
