using System;
using System.Collections.Generic;
using SpaceGame.Persistence;

namespace SpaceGame.World.Bounties
{
    /// <summary>
    /// The open hunt as only the server knows it: whether its cast has been stood at the site, who they are, and how
    /// long the hunt has left. Saved whole by <c>BountySaveable</c>; never sent to clients.
    /// </summary>
    [Serializable]
    public sealed class BountyHuntRunState
    {
        /// <summary>One of the cast, by save identity, and the spot it keeps to.</summary>
        [Serializable]
        public struct HuntPiece
        {
            public SaveRef body;

            /// <summary>The poster's own agent: the one handed in.</summary>
            public bool target;

            /// <summary>The spot it was given; empty for one scattered round the last spot of its tag.</summary>
            public string spot;
        }

        /// <summary>
        /// The OnTaken entries have been stood. Set before the first body is spawned, so a failure part-way never spawns
        /// the cast twice.
        /// </summary>
        public bool spawned;

        /// <summary>The OnFirstTip entries have been stood. Set before their first body, like <see cref="spawned"/>.</summary>
        public bool tipCastSpawned;

        public List<HuntPiece> pieces = new();

        /// <summary>Seconds of server play the hunt has been open.</summary>
        public float elapsed;

        /// <summary>Seconds every player has been outside the hunt's bounds; 0 while anyone is inside.</summary>
        public float outsideFor;

        /// <summary>Cast of an ended hunt whose chunks were not loaded: despawned as soon as each streams in.</summary>
        public List<SaveRef> pendingDespawn = new();
    }
}
