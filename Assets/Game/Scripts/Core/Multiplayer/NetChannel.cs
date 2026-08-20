// The handler table half of the NetMessaging channel — see NetMessaging.cs for the why.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The handler table for one entity, and the only thing that ever invokes a handler.
    ///
    /// Separate from <see cref="NetRelay"/> because a NetworkBehaviour on an object with no
    /// NetworkObject is an error in Netcode, and the whole point is that an entity nobody has
    /// networked still works. This is a plain MonoBehaviour: it is added on demand, it works
    /// offline, and it dies with its GameObject so handlers cannot outlive their objects.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class NetChannel : MonoBehaviour
    {
        private readonly Dictionary<ushort, List<NetHandler>> handlers = new();
        private readonly List<NetHandler> dispatchBuffer = new();
        private bool warnedUnrelayed;

        /// <summary>
        /// The channel for <paramref name="self"/>'s entity, creating it if needed.
        ///
        /// "Entity" means the NetworkObject root when there is one, so a handler registered on a
        /// deeply nested weapon and a message addressed to the player body meet in the same place.
        /// </summary>
        public static NetChannel GetOrAdd(Component self)
        {
            if (self == null) return null;

            GameObject root = RootOf(self);
            if (root == null) return null;

            return root.TryGetComponent(out NetChannel channel) ? channel : root.AddComponent<NetChannel>();
        }

        /// <summary>As <see cref="GetOrAdd"/>, but returns null instead of adding one.</summary>
        public static NetChannel Find(Component self)
        {
            GameObject root = RootOf(self);
            return root != null && root.TryGetComponent(out NetChannel channel) ? channel : null;
        }

        internal static GameObject RootOf(Component self)
        {
            if (self == null) return null;

            NetworkObject netObj = self.GetComponentInParent<NetworkObject>();
            return netObj != null ? netObj.gameObject : self.transform.root.gameObject;
        }

        public void Register(ushort id, NetHandler handler)
        {
            if (handler == null) return;

            if (!handlers.TryGetValue(id, out List<NetHandler> list))
                handlers[id] = list = new List<NetHandler>();

            if (!list.Contains(handler)) list.Add(handler);
        }

        public void Unregister(ushort id, NetHandler handler)
        {
            if (handler != null && handlers.TryGetValue(id, out List<NetHandler> list))
                list.Remove(handler);
        }

        /// <summary>
        /// Run every handler for <paramref name="id"/>.
        ///
        /// A handler that throws is logged and the rest still run. One feature's bug must not stop
        /// the message that carries somebody else's damage, mount or pickup — a half-delivered
        /// message is how a session diverges and stays diverged.
        /// </summary>
        public void Dispatch(ushort id, in NetArg arg, ulong sender)
        {
            if (!handlers.TryGetValue(id, out List<NetHandler> list) || list.Count == 0)
                return;

            // Copied first: a handler is entitled to unsubscribe itself, or to spawn something that
            // subscribes, and either would invalidate the list mid-iteration.
            dispatchBuffer.Clear();
            dispatchBuffer.AddRange(list);

            foreach (NetHandler handler in dispatchBuffer)
            {
                try
                {
                    handler(in arg, sender);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Net] handler for message {id} on '{name}' threw: {e}", this);
                }
            }
        }

        /// <summary>
        /// Say once, per entity, that this thing is running a networked action locally because it
        /// has no way to replicate it. Not an error: the action still happens, on every machine,
        /// exactly as it did before the game had netcode. It is a to-do list.
        /// </summary>
        public void WarnUnrelayed(ushort id)
        {
            if (warnedUnrelayed) return;
            warnedUnrelayed = true;

            Debug.LogWarning($"[Net] '{name}' handled message {id} locally — it has no NetworkObject/" +
                             "NetRelay, so this action is not replicated. The session continues with " +
                             "each machine running its own copy.", this);
        }
    }
}
