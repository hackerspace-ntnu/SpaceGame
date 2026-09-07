// Every storm cloud standing in the world on this machine, and the budget that keeps them countable.
//
// WHY THERE IS A CAP AT ALL. One cloud picks a target every two and a half seconds and bills a
// lightning strike on it, and it does that whether or not another cloud is doing the same thing over
// the same clearing. Four flasks uncorked together is four independent bolt timers over one set of
// bodies — not four times as interesting, just a body dying to a weapon nobody was aiming. The
// design named that risk before it could happen in a session, so the number is decided here rather
// than discovered in a playtest (GDC-L1-PERF-0004: budgets are allocated, not found).
//
// REGISTERED ON EVERY MACHINE, QUERIED ON ONE. Registration rides the cloud's OnEnable, which runs
// wherever a cloud exists; the count and the eviction are the server's alone, because a despawn is.
// Registering everywhere costs a list entry and means the field never disagrees with what is on
// screen — a client-side reader that wants to know how many storms are up gets a true answer.
//
// SPAWN ORDER IS AGE ORDER. The list is only ever appended to, so index 0 is the oldest cloud still
// standing. That is what the eviction wants: a storm somebody threw half a minute ago has had its
// run, and ending it is a smaller surprise than refusing the flask that was just uncorked.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The set of storm clouds standing in the world on this machine, and the per-world budget the
    /// Storm Flask enforces against it.
    /// </summary>
    public static class StormCloudField
    {
        private static readonly List<StormCloud> Live = new List<StormCloud>();

        // Statics survive a world unload, a return to the menu and — with Enter Play Mode Options
        // on, which they are here — play mode itself. A list still holding last session's destroyed
        // clouds would refuse the first flask of the next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        /// <summary>How many clouds are standing right now.</summary>
        public static int LiveCount
        {
            get
            {
                Sweep();
                return Live.Count;
            }
        }

        /// <summary>
        /// The first cloud still standing, or null. Spawn order, which for something that never
        /// moves and never changes hands is age order.
        /// </summary>
        public static StormCloud Oldest
        {
            get
            {
                Sweep();
                return Live.Count > 0 ? Live[0] : null;
            }
        }

        /// <summary>A cloud has appeared on this machine. Called from its own OnEnable.</summary>
        public static void Register(StormCloud cloud)
        {
            if (cloud == null || Live.Contains(cloud)) return;

            Live.Add(cloud);
        }

        /// <summary>A cloud is gone. Mirrors <see cref="Register"/>.</summary>
        public static void Unregister(StormCloud cloud) => Live.Remove(cloud);

        /// <summary>
        /// Drop entries whose object is gone.
        ///
        /// <para>
        /// A cloud can be destroyed without its OnDisable running — a scene unload does exactly
        /// that — so the list is swept rather than trusted, and a budget that counted destroyed
        /// clouds would refuse storms for the rest of the session. Walked backwards so the
        /// surviving entries keep their relative order, which is what makes index 0 the oldest.
        /// </para>
        /// </summary>
        private static void Sweep()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
                if (Live[i] == null) Live.RemoveAt(i);
        }
    }
}
