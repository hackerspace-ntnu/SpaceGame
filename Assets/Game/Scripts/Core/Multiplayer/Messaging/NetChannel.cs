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
        private bool warnedUnrelayed;

        /// <summary>
        /// Spare copy buffers, one handed out per <see cref="Dispatch"/> currently on the stack.
        ///
        /// A single reusable buffer per channel is what this replaced, and it was wrong for the
        /// case that matters most: dispatch is RE-ENTRANT, and re-enters on the same channel. The
        /// standard round trip is a client sending UseItem to the server, whose handler answers by
        /// broadcasting ItemUsed — and on the host <c>SendTo.ClientsAndHost</c> runs the local end
        /// inline, so the inner Dispatch began while the outer one was still walking its buffer.
        /// Clearing it there threw "Collection was modified" out of an RPC handler on every item
        /// use, taking the rest of the outer dispatch with it.
        ///
        /// Static and unsynchronised because Netcode delivers messages on the main thread only, so
        /// "how many dispatches are in flight" is exactly the call depth. Buffers are cleared as
        /// they are returned, so the pool never holds a handler alive past its dispatch.
        /// </summary>
        private static readonly Stack<List<NetHandler>> BufferPool = new();

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

        /// <summary>
        /// Which of the <typeparamref name="T"/>s on this entity <paramref name="self"/> is — the
        /// number a message puts in <see cref="NetArg.A"/> so the other ones can drop it.
        ///
        /// <para>
        /// A channel belongs to the ENTITY, so an entity carrying several of the same component —
        /// a ship with four chairs, a hull with two doors, a craft with a wheel and a winch — has
        /// them all listening to the same ids. Without a number in the message, one press acts on
        /// every one of them. ArticulatedPartInteraction, VehicleStation and NetLatch each learned
        /// that separately; this is the shared answer.
        /// </para>
        /// <para>
        /// Positional, and that is a deliberate trade: every machine in a session runs the same
        /// prefab out of the same build, so the order they enumerate is identical and the numbers
        /// agree without anything being authored or serialized. It does NOT survive reordering a
        /// prefab's children between builds — fine, because these numbers never outlive a session,
        /// but worth knowing before somebody rearranges a hierarchy and wonders why the wrong door
        /// opens. Resolve it on first use rather than in Awake, so a component added at runtime —
        /// and one built by an EditMode test, where Awake never runs — still numbers itself.
        /// </para>
        /// </summary>
        internal static int IndexOf<T>(T self) where T : Component
        {
            GameObject root = RootOf(self);
            if (root == null) return 0;

            var siblings = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < siblings.Length; i++)
                if (ReferenceEquals(siblings[i], self)) return i;

            return 0;
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
            // subscribes, and either would invalidate the list mid-iteration. The copy is rented
            // rather than reused so that a handler which triggers another dispatch — the normal
            // request-then-broadcast round trip — gets its own buffer instead of resetting ours.
            List<NetHandler> buffer = BufferPool.Count > 0 ? BufferPool.Pop() : new List<NetHandler>();
            buffer.AddRange(list);

            try
            {
                foreach (NetHandler handler in buffer)
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
            finally
            {
                buffer.Clear();
                BufferPool.Push(buffer);
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
