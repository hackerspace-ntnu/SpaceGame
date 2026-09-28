// Who hears whom. Pure, static, and testable without a session.
//
// This is the piece that decides the cost of voice: at 24 players an unrouted broadcast is every
// speaker to every listener, while distance culling and a cap on simultaneous speakers bound it to
// something a home connection serves comfortably. Keeping the decision here -- taking plain data,
// returning a list -- is what lets that be pinned by an EditMode test instead of by playtesting
// with two dozen people.
//
// Only the lobby rule exists so far. In a lobby there are no player bodies to measure between, and
// everyone is there to organise, so everyone hears everyone. The in-world rules -- audible radius,
// nearest-N cap, squad channel -- land here beside it.
using System.Collections.Generic;

namespace SpaceGame.Voice
{
    /// <summary>Routing decisions for one speaker's frame.</summary>
    public static class VoiceRouter
    {
        /// <summary>
        /// Everyone connected except the speaker themselves, written into
        /// <paramref name="listeners"/> (cleared first, so it can be reused every frame).
        /// <para>
        /// The speaker is excluded because hearing your own voice come back a network round trip
        /// later is the single most disorienting thing a voice system can do.
        /// </para>
        /// </summary>
        public static void Everyone(IReadOnlyList<ulong> connected, ulong speaker,
                                    List<ulong> listeners)
        {
            listeners.Clear();
            if (connected == null) return;

            for (int i = 0; i < connected.Count; i++)
            {
                if (connected[i] == speaker) continue;
                listeners.Add(connected[i]);
            }
        }
    }
}
