// The state a joining client was never told about.
//
// Most gameplay state replicates on its own: a NetworkVariable has a current value a joiner reads
// at spawn, and a NetworkTransform publishes a pose. But a system whose state exists only because
// every machine ran the same Present() has nothing for a joiner to read — they were not there for
// the event, and events do not replay. Several systems on this branch are in that position, and the
// symptom is the same for all of them: a rope holding nothing, a player teleporting through a solid
// wall, a creature straining against an invisible force.
//
// WHY THIS DOES NOT REUSE THE SAVERS, which was the obvious idea and the first one tried. A leash
// record and a portal record already describe exactly this state, and rebuilding from one is what
// LeashSaveable and PortalPairSaveable do — but both address their subjects with a SaveRef, and a
// SaveRef DOES NOT RESOLVE ON A CLIENT. SaveRefBinder says so itself and warns when anybody tries:
// the entity registry is filled by hydration, which is server-only, and the player table is filled
// by a server RPC handler. A snapshot built out of SaveRefs captures correctly, travels correctly,
// and then silently resolves to nothing on the one machine it exists for.
//
// So this addresses everything by NetworkObjectId instead. That is the one name for a live object
// that both machines already agree on, and it is exactly the right scope: a join snapshot lives and
// dies with the session, which is precisely how long the id is valid. The save file keeps its
// SaveRefs, because it has to survive a restart and an id does not. Two questions, two answers.
//
// It cannot ride NetMessaging: NetArg has no string field and no room for a rope's two endpoints,
// and this has to reach ONE client rather than all of them. That is the same pair of reasons
// ChatNetwork gives, and this follows it — a NetworkBehaviour on the object that already exists
// once per session and spawns before any player does.
//
// Three files: SnapshotPayload is what travels, SnapshotCapture builds it on the server, and
// SnapshotRestore places each entry on the joiner. This one is the wire and the retry loop.
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core
{
    /// <summary>
    /// Hands each joining client the world state that only ever travelled as an event.
    ///
    /// <para>
    /// Lives on the <c>NetworkGameManager</c> prefab, beside <see cref="ChatNetwork"/> and for the
    /// same reasons: that object carries a <see cref="NetworkObject"/>, sits in the persistent
    /// scene loaded beneath every gameplay scene, and is spawned on every peer before the first
    /// player is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class SessionSnapshot : NetworkBehaviour
    {
        /// <summary>
        /// How long a joiner keeps trying to place what it was sent.
        ///
        /// <para>
        /// The snapshot goes out the moment the server sees the connection, which is well before
        /// that client has spawned everybody else's player objects or streamed in the chunk a
        /// roped creature stands in — so the things an entry names usually do not exist yet on the
        /// machine receiving it. Long enough to cover a slow scene sync, short enough that an entry
        /// naming something genuinely gone stops being retried. Same shape and same reasoning as
        /// <c>LeashSaveable</c>'s own retry window.
        /// </para>
        /// </summary>
        private const float ResolveWindowSeconds = 30f;

        private readonly List<SnapshotPayload.RopeEntry> waitingRopes = new();
        private readonly List<SnapshotPayload.PortalEntry> waitingPortals = new();
        private float deadline;

        /// <summary>Apply a payload built by <see cref="SnapshotCapture.Build"/>. Safe on an empty string.</summary>
        public void ApplyPayload(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            SnapshotPayload payload = JsonConvert.DeserializeObject<SnapshotPayload>(json, SaveSerializer.Settings);

            waitingRopes.Clear();
            waitingPortals.Clear();

            if (payload.ropes != null) waitingRopes.AddRange(payload.ropes);
            if (payload.portals != null) waitingPortals.AddRange(payload.portals);

            if (waitingRopes.Count == 0 && waitingPortals.Count == 0) return;

            deadline = Time.time + ResolveWindowSeconds;
        }

        private void Update()
        {
            if (waitingRopes.Count == 0 && waitingPortals.Count == 0) return;

            // Walked backwards so an entry placed this frame can be removed without disturbing the
            // ones still waiting.
            for (int i = waitingRopes.Count - 1; i >= 0; i--)
                if (SnapshotRestore.TryTie(waitingRopes[i])) waitingRopes.RemoveAt(i);

            for (int i = waitingPortals.Count - 1; i >= 0; i--)
                if (SnapshotRestore.TryPlace(waitingPortals[i])) waitingPortals.RemoveAt(i);

            if ((waitingRopes.Count == 0 && waitingPortals.Count == 0) || Time.time < deadline) return;

            Debug.LogWarning($"[Net] Joined mid-session and could not rebuild {waitingRopes.Count} " +
                             $"rope(s) and {waitingPortals.Count} player(s)' portals: what they were " +
                             "attached to never arrived on this machine.", this);

            waitingRopes.Clear();
            waitingPortals.Clear();
        }

        // ── Session plumbing ───────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.OnClientConnectedCallback += OnClientConnected;
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        public override void OnDestroy()
        {
            // Not merely tidiness, and the same trap ChatNetwork documents: OnNetworkDespawn does
            // not run for an object destroyed without a despawn — a hard disconnect, or the scene
            // unload on the way back to the main menu — which is the common way a session ends. A
            // subscription left behind would answer the next session's joins on a destroyed
            // behaviour.
            Unsubscribe();

            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback -= OnClientConnected;
        }

        /// <summary>
        /// Server-side. Somebody has joined; tell them what they missed.
        ///
        /// <para>
        /// The host's own connection is skipped: it has been here the whole time, and rebuilding
        /// over its own live state would give it a second copy of every rope.
        /// </para>
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;

            string payload = SnapshotCapture.Build();
            if (string.IsNullOrEmpty(payload)) return;

            SnapshotRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>Server → one client. The unicast NetMessaging has no direction for.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void SnapshotRpc(string payload, RpcParams rpcParams = default) =>
            ApplyPayload(payload);
    }
}
