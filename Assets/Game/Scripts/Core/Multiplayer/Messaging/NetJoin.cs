// The late joiner's question — "what state is this in?" — asked once there is a wire to ask on.
//
// Lifted out of NetLatch when the weathervane ring became the second fixture that needed it. Both
// hold shared world state the server owns, both announce changes as events, and both leave a
// client that connected after the last event holding the prefab's state until it asks.
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    public static class NetJoin
    {
        /// <summary>
        /// Whether this machine has anyone to ask: a client of a live session. A host, an offline
        /// session, an EditMode test and a scene opened straight from the editor are all already
        /// the authority, and have no business starting the coroutine below.
        /// </summary>
        public static bool ShouldAsk(MonoBehaviour owner) =>
            owner != null && owner.isActiveAndEnabled && Network.IsNetworked && !Network.Server;

        /// <summary>
        /// Run <paramref name="ask"/> once the entity's NetworkObject is actually spawned.
        ///
        /// Waiting matters: before the spawn there is no relay, the send falls through to a local
        /// dispatch, and the client answers its own question with the state it already had — which
        /// is the prefab's, which is the thing being corrected.
        ///
        /// An entity with no NetworkObject has no wire at all, so nobody could answer and nobody
        /// would hear it; the fixture still works on this machine alone (NetChannel.WarnUnrelayed
        /// says so once).
        /// </summary>
        public static IEnumerator AskWhenSpawned(Component owner, Action ask)
        {
            GameObject root = NetChannel.RootOf(owner);
            NetworkObject netObj = root != null ? root.GetComponent<NetworkObject>() : null;
            if (netObj == null) yield break;

            while (!netObj.IsSpawned)
            {
                if (!Network.IsNetworked) yield break;
                yield return null;
            }

            ask();
        }
    }
}
