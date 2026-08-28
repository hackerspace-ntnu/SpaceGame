// What a joining client is handed — see SessionSnapshot.cs for why it names things by
// NetworkObjectId and not by SaveRef.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Everything a joiner is owed, as it travels. Built by <see cref="SnapshotCapture"/> on the
    /// server and placed by <see cref="SnapshotRestore"/> on the joiner.
    ///
    /// Plain public fields, and no properties: <c>SaveSerializer</c>'s resolver serialises fields,
    /// and anything else added here would travel too.
    /// </summary>
    internal struct SnapshotPayload
    {
        /// <summary>
        /// One end of a rope, named the way a live session can name it.
        ///
        /// <para>
        /// Exactly one of <see cref="anchor"/> and <see cref="point"/> matters, which is the same
        /// split <c>LeashSaveable.Endpoint</c> makes: an end tied to a THING travels as that
        /// thing's id plus a local offset, so the knot rides it, and an end pinned to a PLACE
        /// travels as a world point, which is identical on every machine by definition.
        /// </para>
        /// </summary>
        public struct RopeEnd
        {
            /// <summary>The anchor's <see cref="Unity.Netcode.NetworkObject.NetworkObjectId"/>, or 0 for a place.</summary>
            public ulong anchor;

            /// <summary>Where the knot sits on that anchor, in its local space.</summary>
            public Vector3 offset;

            /// <summary>Where in the world, for an end pinned to bare geometry.</summary>
            public Vector3 point;

            /// <summary>True for the end in a player's hand.</summary>
            public bool held;
        }

        public struct RopeEntry
        {
            public RopeEnd a;
            public RopeEnd b;

            /// <summary>
            /// Carried because a tie across a wide gap pays the rope out to reach — once, and
            /// capped, but permanently. Rebuilding at the artifact's authored length would put a
            /// long rope under instant tension the moment physics resumed.
            /// </summary>
            public float length;
        }

        /// <summary>One shooter's apertures, addressed by the shooter's NetworkObjectId.</summary>
        public struct PortalEntry
        {
            public ulong shooter;
            public JObject portals;
        }

        public List<RopeEntry> ropes;
        public List<PortalEntry> portals;
    }
}
