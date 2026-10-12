// Who hears whom. Pure, static, and testable without a session.
//
// This is the piece that decides the cost of voice: at 24 players an unrouted broadcast is every
// speaker to every listener, while distance culling and a cap on simultaneous speakers bound it to
// something a home connection serves comfortably. Keeping the decision here -- taking plain data,
// returning a list -- is what lets that be pinned by an EditMode test instead of by playtesting
// with two dozen people.
//
// The host runs this for every frame it relays, so it is also the anti-cheat half of proximity
// (GDC-L1-MP-0004): a voice beyond range is never sent, and a modified client cannot turn up what
// it was never given. Each listener still fades what it does receive, but that is presentation.
//
// Still to land beside it: the nearest-N cap on simultaneous speakers, and the squad channel.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>Routing decisions for one speaker's frame.</summary>
    public static class VoiceRouter
    {
        /// <summary>
        /// Everyone connected who is within <paramref name="range"/> metres of the speaker, written
        /// into <paramref name="listeners"/> (cleared first, so it can be reused every frame).
        /// <para>
        /// The speaker is always excluded: hearing your own voice come back a network round trip
        /// later is the single most disorienting thing a voice system can do.
        /// </para>
        /// <para>
        /// Anyone missing from <paramref name="bodies"/> is treated as everywhere at once — heard by
        /// all and hearing all. That is the lobby, where nobody has a body to measure between and
        /// everyone is there to organise, and it is also a player between bodies (loading in, dead,
        /// spectating), who would otherwise drop out of every conversation without warning.
        /// </para>
        /// </summary>
        public static void Proximity(IReadOnlyList<ulong> connected, ulong speaker,
                                     IReadOnlyDictionary<ulong, Vector3> bodies, float range,
                                     List<ulong> listeners)
        {
            listeners.Clear();
            if (connected == null) return;

            Vector3 mouth = default;
            bool placed = bodies != null && bodies.TryGetValue(speaker, out mouth);
            float reach = range * range;

            for (int i = 0; i < connected.Count; i++)
            {
                ulong listener = connected[i];
                if (listener == speaker) continue;

                if (placed && bodies.TryGetValue(listener, out Vector3 ear) &&
                    (ear - mouth).sqrMagnitude > reach)
                {
                    continue;
                }

                listeners.Add(listener);
            }
        }
    }
}
