// The payload half of the NetMessaging channel — see NetMessaging.cs for the why.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The payload for every networked message.
    ///
    /// Deliberately one fixed struct rather than a type per message. Every call site in the game
    /// fits in these fields, a struct costs no allocation, and it means adding a message is a
    /// constant in <see cref="NetMsg"/> and nothing else — no serializer, no registration, no
    /// generated code.
    /// </summary>
    public struct NetArg : INetworkSerializable
    {
        /// <summary>A NetworkObjectId — the subject of the message. 0 means "none".</summary>
        public ulong Target;

        public int A;
        public int B;
        public Vector3 P;
        public Quaternion R;

        // Deliberately absent from NetworkSerialize, so it never leaves this machine.
        //
        // An id can only be minted for a spawned NetworkObject, which means that offline — and for
        // anything not networked — Target is 0 and Resolve would answer null. That would make
        // every message carrying a subject work online and break in single-player, which is the
        // exact failure this whole layer exists to prevent. On the local dispatch path the struct
        // is handed straight to the handler, so the reference is simply still here.
        private GameObject localTarget;

        public NetArg(ulong target = 0, int a = 0, int b = 0) : this()
        {
            Target = target;
            A = a;
            B = b;
            R = Quaternion.identity;
        }

        /// <summary>Point this message at <paramref name="go"/>. Works networked and offline.</summary>
        public NetArg With(GameObject go)
        {
            Target = IdOf(go);
            localTarget = go;
            return this;
        }

        /// <summary>As <see cref="With(GameObject)"/>, for a component on the subject.</summary>
        public NetArg With(Component component) =>
            With(component != null ? component.gameObject : null);

        /// <summary>The GameObject this message is about, or null if there is none or it is gone.</summary>
        public readonly GameObject Resolve()
        {
            if (localTarget != null) return localTarget;
            if (Target == 0 || !Network.IsNetworked) return null;

            var spawned = NetworkManager.Singleton.SpawnManager;
            if (spawned == null) return null;

            return spawned.SpawnedObjects.TryGetValue(Target, out NetworkObject obj) && obj != null
                ? obj.gameObject
                : null;
        }

        /// <summary>
        /// True when <see cref="R"/> carries a real orientation.
        ///
        /// A default-constructed NetArg leaves it all-zero, which is not a rotation — so this is how
        /// a handler tells "the sender told me where they were aiming" from "nobody filled this in".
        /// Distinguishing those matters: the alternative is for a peer to fall back on its own
        /// camera, which on the server means firing along the host's crosshair.
        /// </summary>
        public readonly bool HasOrientation =>
            R.x * R.x + R.y * R.y + R.z * R.z + R.w * R.w > 1e-4f;

        /// <summary>The id to put in <see cref="Target"/> for <paramref name="go"/>, or 0.</summary>
        public static ulong IdOf(GameObject go)
        {
            if (go == null) return 0;
            NetworkObject netObj = go.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref A);
            serializer.SerializeValue(ref B);
            serializer.SerializeValue(ref P);
            serializer.SerializeValue(ref R);
        }
    }
}
