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
// In story mode the crew has a suit radio (VoiceRadio), so there the host relays every voice to
// everyone (Everyone) and each listener decides how much is heard in person and how much over the
// radio. Versus stays on Proximity: the other team must not be able to listen in.
//
// Still to land beside these: the nearest-N cap on simultaneous speakers, and the squad channel.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>Routing decisions for one speaker's frame.</summary>
    public static class VoiceRouter
    {
        /// <summary>
        /// Everyone connected except the speaker, wherever they are, written into
        /// <paramref name="listeners"/> (cleared first). The story-mode rule: with the suit radio
        /// nobody is ever out of reach, so distance is the listener's business, not the router's.
        /// </summary>
        public static void Everyone(IReadOnlyList<ulong> connected, ulong speaker, List<ulong> listeners)
        {
            listeners.Clear();
            if (connected == null) return;

            for (int i = 0; i < connected.Count; i++)
            {
                if (connected[i] != speaker) listeners.Add(connected[i]);
            }
        }

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
