using System;
using System.Collections.Generic;
using SpaceGame.Persistence;

namespace SpaceGame.World.Bounties
{
    /// <summary>
    /// The open hunt as only the server knows it: whether its cast has been stood at the site, who they are, and how
    /// long they have stood. Saved whole by <c>BountySaveable</c>; never sent to clients.
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

        /// <summary>Seconds of server play the cast has stood. Nothing counts before: there is nothing yet to lose.</summary>
        public float elapsed;

        /// <summary>Seconds every player has been outside the hunt's bounds while the cast stood; 0 while anyone is inside.</summary>
        public float outsideFor;

        /// <summary>Cast of an ended or restarted hunt not yet gone: despawned as soon as each is loaded.</summary>
        public List<SaveRef> pendingDespawn = new();

        /// <summary>Hands the whole cast to <see cref="pendingDespawn"/>, leaving none: the run no longer keeps them.</summary>
        public void WithdrawPieces()
        {
            foreach (HuntPiece piece in pieces)
                if (piece.body.IsSet) pendingDespawn.Add(piece.body);
            pieces.Clear();
        }
    }
}
