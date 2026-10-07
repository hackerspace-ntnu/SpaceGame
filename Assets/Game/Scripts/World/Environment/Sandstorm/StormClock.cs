// What time the WEATHER thinks it is.
//
// Every StormInstance records the moment it began, and everything about it — where it has travelled
// to, how far through its lifecycle it is, which way it has wandered — is a function of how long ago
// that was. So a storm's StartTime is only meaningful against a clock that means the same thing next
// session as it did this one, and neither clock underneath this project does: ServerTime.Time counts
// from when the host opened the session, Time.timeAsDouble counts from process start. Both restart at
// zero. A saved storm's StartTime read back against a fresh zero is a storm that started in the
// future, or one that has already blown itself out before the world finished loading.
//
// The fix is the one DayNightCycle already uses for the identical problem: keep an ANCHOR — at this
// reading of the shared clock, the weather clock read this — and restore by re-stating the anchor
// rather than by replaying elapsed time. Two machines evaluating the same function against the same
// shared clock and the same anchor cannot drift, and a world put back an hour later resumes its
// weather where it left it instead of starting the afternoon again.
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    /// <summary>
    /// The weather clock's anchor on the wire: at shared-clock reading <see cref="Clock"/>, weather
    /// time reads <see cref="Weather"/>.
    ///
    /// A pair, always sent together. The same weather time applied against each machine's own "now"
    /// would be a different moment on every machine that applied it, which is exactly the drift the
    /// anchor exists to remove.
    /// </summary>
    public struct StormClockAnchor : INetworkSerializable, System.IEquatable<StormClockAnchor>
    {
        /// <summary>
        /// False for the default value a NetworkVariable holds before the server has written one.
        /// Without it a client cannot tell "the server says the weather clock is at zero" from "the
        /// server has not said anything yet", and adopting the second as the first puts that client
        /// in different weather from everyone else until the real value arrives.
        /// </summary>
        public bool Set;

        public double Weather;
        public double Clock;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Set);
            serializer.SerializeValue(ref Weather);
            serializer.SerializeValue(ref Clock);
        }

        public bool Equals(StormClockAnchor other) =>
            Set == other.Set && Weather.Equals(other.Weather) && Clock.Equals(other.Clock);

        public override bool Equals(object obj) => obj is StormClockAnchor other && Equals(other);

        public override int GetHashCode() => Weather.GetHashCode() ^ Clock.GetHashCode();
    }

    /// <summary>
    /// The clock storms are timed against. Read it through <see cref="Sandstorms.WeatherTime"/>.
    ///
    /// <para>
    /// Deliberately NOT the same clock as <see cref="SpaceGame.World.DayNightCycle"/>'s. The sun
    /// anchors itself against the raw shared clock and re-states its own anchor when that clock
    /// changes identity; if the weather could also move the reading underneath it, restoring a saved
    /// world would swing the sun by however far the weather clock had jumped. Two clocks, each
    /// restored by its own saver, is the arrangement in which neither can move the other.
    /// </para>
    /// </summary>
    public static class StormClock
    {
        // At shared-clock reading anchorClock, weather time reads anchorWeather. Two numbers rather
        // than an accumulator, because an accumulator can only describe THIS machine's history.
        private static double anchorWeather;
        private static double anchorClock;

        /// <summary>
        /// Which clock <see cref="anchorClock"/> was measured against — a session's server time, or
        /// this process's game time. They count from different origins, so an anchor taken against
        /// one is meaningless against the other.
        /// </summary>
        private static bool anchoredToSession;

        private static bool hasAnchor;

        /// <summary>
        /// The last value <see cref="Now"/> produced. Needed to carry the reading across a change of
        /// clock source: the new clock cannot tell us what the old one said a moment ago.
        /// </summary>
        private static double lastNow;

        /// <summary>
        /// Raised whenever the anchor moves — i.e. whenever something states what time the weather
        /// is. The server's cue to replicate that statement; see <c>SandstormManager</c>.
        /// </summary>
        public static event System.Action AnchorMoved;

        /// <summary>
        /// The raw clock every machine agrees on. Server time when there is a session — including for
        /// clients, which estimate it — and plain game time when there is not.
        /// </summary>
        public static double Shared =>
            Network.IsNetworked ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        /// <summary>What time the weather is. Zero at the start of a world, and monotonic from there.</summary>
        public static double Now
        {
            get
            {
                Sync();
                lastNow = anchorWeather + (Shared - anchorClock);
                return lastNow;
            }
        }

        /// <summary>Has anything set the anchor yet? False means the numbers are zeroes, not an answer.</summary>
        public static bool HasAnchor => hasAnchor;

        /// <summary>The anchor as it stands, for something that has to carry it elsewhere — the wire.</summary>
        public static void ReadAnchor(out double weather, out double clock)
        {
            weather = anchorWeather;
            clock = anchorClock;
        }

        /// <summary>
        /// States that the weather clock reads <paramref name="weather"/> when the shared clock reads
        /// <paramref name="clock"/>. The only way weather time is ever set.
        /// </summary>
        public static void AnchorTo(double weather, double clock)
        {
            anchorWeather = weather;
            anchorClock = clock;
            anchoredToSession = Network.IsNetworked;
            hasAnchor = true;
            lastNow = weather + (Shared - clock);

            AnchorMoved?.Invoke();
        }

        /// <summary>
        /// Restore-only. Puts the weather clock back to a saved reading, anchored to this machine's
        /// idea of now — a save file outlives the session it was written in, so the reading it was
        /// measured against cannot travel with it. Called by the save system; do not call from
        /// gameplay.
        /// </summary>
        public static void RestoreNow(double weather) => AnchorTo(weather, Shared);

        /// <summary>
        /// Restore-only. Forgets the anchor, so the next read starts a fresh world at zero.
        ///
        /// Statics outlive a scene load, and a quickload or a return to the menu and into a different
        /// world would otherwise inherit the last world's weather time. Called by the save system
        /// immediately before it registers, so a restore that follows wins.
        /// </summary>
        public static void Reset()
        {
            anchorWeather = 0d;
            anchorClock = 0d;
            anchoredToSession = false;
            hasAnchor = false;
            lastNow = 0d;

            AnchorMoved?.Invoke();
        }

        /// <summary>
        /// Re-states the anchor when the clock underneath it changes identity — a session coming up
        /// under a world that started without one, or shutting down under one that had it.
        ///
        /// <para>
        /// Readings from the two clocks are not comparable, so an anchor left alone across the
        /// handover would throw every storm's age to an arbitrary number. Re-stating PRESERVES the
        /// weather time, which is why <c>SandstormManager.PromoteLocalRecords</c> no longer has to
        /// restamp the storms it promotes: their StartTimes are still measured against a clock that
        /// still means the same thing.
        /// </para>
        /// <para>
        /// <c>Network.IsNetworked</c> reads false during session teardown, which is a documented trap
        /// in this project. It is harmless here: re-stating keeps the value it already had, so the
        /// worst a spurious handover costs is one re-publish of an unchanged anchor.
        /// </para>
        /// </summary>
        private static void Sync()
        {
            bool networked = Network.IsNetworked;
            if (hasAnchor && anchoredToSession == networked) return;

            AnchorTo(hasAnchor ? lastNow : 0d, Shared);
        }
    }
}
