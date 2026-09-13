using UnityEngine;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The clock behind the session list's automatic refresh.
    ///
    /// <para>
    /// Lobby rate-limits QueryLobbies to one call per second, so <see cref="Interval"/> is the
    /// ceiling rather than a comfortable margin. Two things keep it inside the limit. The interval
    /// is measured from the moment a query FINISHES, not on a fixed clock, so two requests can
    /// never be in flight at once and a slow response spaces the next one out by however long it
    /// took. And a failure backs off, doubling up to <see cref="MaxBackoff"/> and resetting on the
    /// next success, so a service that is refusing us is not asked again at full rate.
    /// </para>
    ///
    /// <para>
    /// Pure so the cadence can be tested without a page. The third guard — the shared minimum
    /// spacing between this refresh and the Refresh button — lives in <c>LobbySession</c>, which is
    /// the only place that can see both callers.
    /// </para>
    /// </summary>
    public sealed class LobbyAutoRefresh
    {
        public const float Interval = 1f;
        public const float MaxBackoff = 15f;

        /// <summary>Doublings stop here; the cap takes over.</summary>
        private const int MaxDoublings = 4;

        private float timer = Interval;
        private int failures;

        /// <summary>
        /// Whether the first query has ever landed. Cleared only by construction, so a page cannot
        /// open by announcing there is nothing there before it has looked.
        /// </summary>
        public bool HasLanded { get; private set; }

        /// <summary>How long until the next query is due. For tests.</summary>
        public float SecondsUntilDue => Mathf.Max(0f, timer);

        /// <summary>Counts down. True once a query is due; the caller starts one and reports how it went.</summary>
        public bool Advance(float deltaTime)
        {
            timer -= deltaTime;
            return timer <= 0f;
        }

        /// <summary>A query came back with a list.</summary>
        public void Landed()
        {
            failures = 0;
            timer = Interval;
            HasLanded = true;
        }

        /// <summary>A query failed. The list on screen is the last one known to be true, so it stays.</summary>
        public void Refused()
        {
            failures++;
            timer = Mathf.Min(Interval * (1 << Mathf.Min(failures, MaxDoublings)), MaxBackoff);
        }
    }
}
