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
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
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

        // ── What travels ───────────────────────────────────────────────────────

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
        private struct RopeEnd
        {
            /// <summary>The anchor's <see cref="NetworkObject.NetworkObjectId"/>, or 0 for a place.</summary>
            public ulong anchor;

            /// <summary>Where the knot sits on that anchor, in its local space.</summary>
            public Vector3 offset;

            /// <summary>Where in the world, for an end pinned to bare geometry.</summary>
            public Vector3 point;

            /// <summary>True for the end in a player's hand.</summary>
            public bool held;
        }

        private struct RopeEntry
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
        private struct PortalEntry
        {
            public ulong shooter;
            public JObject portals;
        }

        private struct Payload
        {
            public List<RopeEntry> ropes;
            public List<PortalEntry> portals;
        }

        // ── Building it ────────────────────────────────────────────────────────

        /// <summary>
        /// Everything a joiner is owed, as JSON, or null when there is nothing to send.
        ///
        /// <para>
        /// Static and free of session state so the empty case is provable without a NetworkManager.
        /// </para>
        /// <para>
        /// Nothing that already replicates belongs in here. Entity records, inventories and player
        /// poses all arrive on their own, and applying a second copy of them to a running client
        /// duplicates creatures and teleports people.
        /// </para>
        /// </summary>
        public static string BuildPayload()
        {
            var payload = new Payload
            {
                ropes = new List<RopeEntry>(),
                portals = new List<PortalEntry>(),
            };

            IReadOnlyList<Leash> ropes = Leash.All;

            for (int i = 0; ropes != null && i < ropes.Count; i++)
            {
                Leash rope = ropes[i];
                if (rope == null || !rope.A.IsAlive || !rope.B.IsAlive) continue;

                payload.ropes.Add(new RopeEntry
                {
                    a = Describe(rope.A),
                    b = Describe(rope.B),
                    length = rope.Length,
                });
            }

            foreach (PortalPairSaveable saver in
                     FindObjectsByType<PortalPairSaveable>(FindObjectsSortMode.None))
            {
                if (saver == null) continue;

                object captured = saver.CaptureState();
                if (captured == null) continue;

                ulong shooter = NetArg.IdOf(saver.gameObject);
                if (shooter == 0) continue;   // not spawned: nothing the receiver could resolve

                payload.portals.Add(new PortalEntry
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
        private static RopeEnd Describe(LeashEnd end)
        {
            ulong anchor = NetArg.IdOf(end.Anchor != null ? end.Anchor.gameObject : null);

            return new RopeEnd
            {
                anchor = anchor,
                offset = end.LocalOffset,
                point = end.Position,
                held = end.Kind == LeashEndKind.PlayerHand,
            };
        }

        // ── Applying it ────────────────────────────────────────────────────────

        private readonly List<RopeEntry> waitingRopes = new();
        private readonly List<PortalEntry> waitingPortals = new();
        private float deadline;

        /// <summary>Apply a payload built by <see cref="BuildPayload"/>. Safe on an empty string.</summary>
        public void ApplyPayload(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            Payload payload = JsonConvert.DeserializeObject<Payload>(json, SaveSerializer.Settings);

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
                if (TryTie(waitingRopes[i])) waitingRopes.RemoveAt(i);

            for (int i = waitingPortals.Count - 1; i >= 0; i--)
                if (TryPlace(waitingPortals[i])) waitingPortals.RemoveAt(i);

            if ((waitingRopes.Count == 0 && waitingPortals.Count == 0) || Time.time < deadline) return;

            Debug.LogWarning($"[Net] Joined mid-session and could not rebuild {waitingRopes.Count} " +
                             $"rope(s) and {waitingPortals.Count} player(s)' portals: what they were " +
                             "attached to never arrived on this machine.", this);

            waitingRopes.Clear();
            waitingPortals.Clear();
        }

        /// <summary>Tie one rope, or false while either of its ends is still on its way.</summary>
        private static bool TryTie(in RopeEntry entry)
        {
            if (!TryResolveEnd(entry.a, out GameObject rootA)) return false;
            if (!TryResolveEnd(entry.b, out GameObject rootB)) return false;

            // The tuning — and the rope MATERIAL, which nothing else can supply — comes off the
            // leash item's own prefab, exactly as the save path resolves it.
            LeashArtifact.TryResolveSettings(out Leash.Settings settings);
            if (entry.length > 0.01f) settings.length = entry.length;

            Leash rope = Leash.Create(settings);

            rope.RestoreEnd(true, rootA, entry.a.offset, entry.a.point, entry.a.held);
            rope.RestoreEnd(false, rootB, entry.b.offset, entry.b.point, entry.b.held);

            return true;
        }

        /// <summary>
        /// The live object for one end, or false while it is still on its way.
        ///
        /// <para>
        /// A null object with <c>true</c> is a legitimate answer and means "this end is a place,
        /// not a thing" — <see cref="Leash.RestoreEnd"/> makes an anchor for it.
        /// </para>
        /// </summary>
        private static bool TryResolveEnd(in RopeEnd end, out GameObject root)
        {
            root = null;
            if (end.anchor == 0) return true;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) return false;

            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(end.anchor, out NetworkObject obj)
                || obj == null)
                return false;

            root = obj.gameObject;
            return true;
        }

        /// <summary>Place one shooter's apertures, or false while that shooter is still on its way.</summary>
        private static bool TryPlace(in PortalEntry entry)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) return false;

            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(entry.shooter, out NetworkObject shooter)
                || shooter == null)
                return false;

            // Added rather than required: the component is authored on the player prefab, but a
            // shooter with no saver is one prefab change away, and losing the portals silently is
            // the failure this whole class exists to stop.
            if (!shooter.TryGetComponent(out PortalPairSaveable saver))
                saver = shooter.gameObject.AddComponent<PortalPairSaveable>();

            // ApplyNow, not RestoreState: a client joining a running session runs no deferred load
            // pass, so a staged record would sit there for ever. See PortalPairSaveable.ApplyNow.
            saver.ApplyNow(entry.portals);
            return true;
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

            string payload = BuildPayload();
            if (string.IsNullOrEmpty(payload)) return;

            SnapshotRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>Server → one client. The unicast NetMessaging has no direction for.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void SnapshotRpc(string payload, RpcParams rpcParams = default) =>
            ApplyPayload(payload);
    }
}
