// The server's half of SessionSnapshot: describe the live world as a payload.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core
{
    /// <summary>
    /// Builds the <see cref="SnapshotPayload"/> a joining client is owed.
    ///
    /// Static and free of session state so the empty case is provable without a NetworkManager.
    /// </summary>
    public static class SnapshotCapture
    {
        /// <summary>
        /// Everything a joiner is owed, as JSON, or null when there is nothing to send.
        ///
        /// <para>
        /// Nothing that already replicates belongs in here. Entity records, inventories and player
        /// poses all arrive on their own, and applying a second copy of them to a running client
        /// duplicates creatures and teleports people.
        /// </para>
        /// </summary>
        public static string Build()
        {
            var payload = new SnapshotPayload
            {
                ropes = new List<SnapshotPayload.RopeEntry>(),
                portals = new List<SnapshotPayload.PortalEntry>(),
            };

            IReadOnlyList<Leash> ropes = Leash.All;

            for (int i = 0; ropes != null && i < ropes.Count; i++)
            {
                Leash rope = ropes[i];
                if (rope == null || !rope.A.IsAlive || !rope.B.IsAlive) continue;

                payload.ropes.Add(new SnapshotPayload.RopeEntry
                {
                    a = Describe(rope.A),
                    b = Describe(rope.B),
                    length = rope.Length,
                });
            }

            foreach (PortalPairSaveable saver in
                     Object.FindObjectsByType<PortalPairSaveable>(FindObjectsSortMode.None))
            {
                if (saver == null) continue;

                object captured = saver.CaptureState();
                if (captured == null) continue;

                ulong shooter = NetArg.IdOf(saver.gameObject);
                if (shooter == 0) continue;   // not spawned: nothing the receiver could resolve

                payload.portals.Add(new SnapshotPayload.PortalEntry
                {
                    shooter = shooter,
                    portals = JObject.FromObject(captured, SaveSerializer.Serializer),
                });
            }

            // Nothing tied and nothing open. Sending an empty payload would cost a round trip to
            // say so, and would make every joiner run an apply that does nothing.
            if (payload.ropes.Count == 0 && payload.portals.Count == 0) return null;

            return JsonConvert.SerializeObject(payload, SaveSerializer.Settings);
        }

        /// <summary>
        /// Turn one live rope end into something the wire can carry.
        ///
        /// <para>
        /// An end whose anchor is not a spawned NetworkObject degrades to its world POINT rather
        /// than losing the whole rope. The joiner then gets a rope tied to that place instead of to
        /// that thing — which is the honest answer, because a prop nobody networked has a different
        /// copy on every machine and there is no shared thing to name.
        /// </para>
        /// </summary>
        private static SnapshotPayload.RopeEnd Describe(LeashEnd end)
        {
            ulong anchor = NetArg.IdOf(end.Anchor != null ? end.Anchor.gameObject : null);

            return new SnapshotPayload.RopeEnd
            {
                anchor = anchor,
                offset = end.LocalOffset,
                point = end.Position,
                held = end.Kind == LeashEndKind.PlayerHand,
            };
        }
    }
}
