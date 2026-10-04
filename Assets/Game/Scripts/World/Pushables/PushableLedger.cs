// Where every cart that has been moved stands, for everyone and for the save.
//
// A pushed cart follows its pusher's body on every machine, so nothing is sent while it moves. What nobody can derive is where it
// was LEFT: the pusher's body is somewhere else by then, a machine that joins afterwards never saw the push, and a chunk that
// unloads and loads again rebuilds the cart at its authored pose. So the server writes one entry per cart that is not at home
// when its holder lets go; every machine shows the list on the carts it has loaded (a cart in a chunk that loads later is posed
// the moment it appears), a joiner reads it with the spawn, and the save keeps it as a global record keyed by the cart's id.
//
// Lives on the NetworkGameManager prefab beside SessionSnapshot and ChatNetwork: one NetworkObject per session in the
// persistent scene, spawned on every peer before any player is. A cart that returns to its authored pose drops out of the list.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class PushableLedger : NetworkBehaviour, ISaveable
    {
        public const string Key = "pushables";   // written into save files — NEVER rename

        /// <summary>One cart that stands somewhere other than where it was authored.</summary>
        public struct CartRest : INetworkSerializable, IEquatable<CartRest>
        {
            public int Id;
            public Vector3 Position;
            public Quaternion Rotation;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Id);
                serializer.SerializeValue(ref Position);
                serializer.SerializeValue(ref Rotation);
            }

            public bool Equals(CartRest other) => Id == other.Id && Position == other.Position && Rotation == other.Rotation;
        }

        public struct State
        {
            public CartRest[] carts;
        }

        private static PushableLedger instance;

        private readonly NetworkList<CartRest> shared = new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // What this machine believes, by cart id: the truth on the server and offline, a mirror of the list on a client.
        private readonly Dictionary<int, CartRest> standing = new();

        public string SaveKey => Key;

        public int Count => standing.Count;

        private bool Mirrors => IsSpawned && !IsServer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>Called by a cart as it appears: it takes the pose the ledger holds for it, at once.</summary>
        public static void Shown(Pushable cart)
        {
            if (instance != null) instance.Apply(cart, settle: false);
        }

        /// <summary>Called by a cart the deciding machine has just let go of: records where it stands.</summary>
        public static void Rest(Pushable cart, CartPose pose)
        {
            if (instance != null) instance.Record(cart, pose);
        }

        private void OnEnable() => Bind();

        private void OnDisable() => Unbind();

        /// <summary>Becomes the session's ledger and shows what it holds on the carts already loaded. Done from OnEnable; public so an edit-mode test can stand one up.</summary>
        public void Bind()
        {
            instance = this;
            SaveManager.RegisterGlobalSaver(this);
            ShowAll(settle: false);
        }

        /// <summary>Stops being the session's ledger. Done from OnDisable; public so an edit-mode test can take one down.</summary>
        public void Unbind()
        {
            if (instance == this) instance = null;
            SaveManager.UnregisterGlobalSaver(this);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                shared.Clear();
                foreach (CartRest rest in standing.Values) shared.Add(rest);
                return;
            }

            shared.OnListChanged += OnListChanged;
            Adopt();
        }

        public override void OnNetworkDespawn() => shared.OnListChanged -= OnListChanged;

        private void OnListChanged(NetworkListEvent<CartRest> change) => Adopt();

        // A client takes the list as it is now: the carts it has loaded settle to it.
        private void Adopt()
        {
            standing.Clear();
            foreach (CartRest rest in shared) standing[rest.Id] = rest;
            ShowAll(settle: true);
        }

        // ── the deciding machine ────────────────────────────────────────────────────────────────

        private void Record(Pushable cart, CartPose pose)
        {
            if (!Network.Decides) return;

            if (cart.IsHome(pose)) standing.Remove(cart.Id);
            else standing[cart.Id] = new CartRest { Id = cart.Id, Position = pose.Position, Rotation = pose.Rotation };

            Mirror();
        }

        private void Mirror()
        {
            if (!IsSpawned || !IsServer) return;

            // A cart at a time is rare, and a list this short is cheaper to rewrite than to diff.
            shared.Clear();
            foreach (CartRest rest in standing.Values) shared.Add(rest);
        }

        // ── showing ────────────────────────────────────────────────────────────────────────────

        private void ShowAll(bool settle)
        {
            foreach (Pushable cart in Pushable.All)
                if (cart != null) Apply(cart, settle);
        }

        // An unheld cart stands where the ledger says, or at home when it says nothing. A held cart is the pusher's to place.
        private void Apply(Pushable cart, bool settle)
        {
            if (!cart.IsFree) return;

            CartPose pose = standing.TryGetValue(cart.Id, out CartRest rest) ? new CartPose(rest.Position, rest.Rotation) : cart.HomePose;

            if (settle) cart.Settle(pose);
            else cart.PlaceAt(pose);
        }

        // ── persistence ──────────────────────────────────────────────────────────────────────────

        public object CaptureState()
        {
            if (standing.Count == 0) return null;

            var carts = new CartRest[standing.Count];
            standing.Values.CopyTo(carts, 0);
            return new State { carts = carts };
        }

        public void RestoreState(JObject state)
        {
            standing.Clear();
            CartRest[] restored = state?.ToObject<State>(SaveSerializer.Serializer).carts;
            if (restored != null)
                foreach (CartRest rest in restored) standing[rest.Id] = rest;

            Mirror();
            ShowAll(settle: false);
        }
    }
}
