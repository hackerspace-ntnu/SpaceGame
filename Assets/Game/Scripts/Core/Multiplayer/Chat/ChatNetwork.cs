// The wire for in-game chat.
//
// Chat is the one gameplay system that cannot ride the generic NetMessaging layer, for two
// reasons that are both structural rather than incidental. NetArg is a fixed struct of ints and
// vectors with no room for a string, and widening it would put a 128-byte field on every damage,
// mount and item message in the game to serve one feature. And NetTo has three directions —
// Server, All, Others — none of which is "this one player": a command's answer ("no player called
// Bob") is worked out on the server and belongs to the person who asked, nobody else.
//
// So chat gets its own three RPCs, on the object that already exists once per session and is
// spawned on every peer before any player is.
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Carries chat between peers, and is the server's authority on who said what.
    /// <para>
    /// Lives on the <c>NetworkGameManager</c> prefab: that object already carries a
    /// <see cref="NetworkObject"/>, is placed in <c>persistentScene</c> — which is loaded beneath
    /// every gameplay scene, including an additively loaded minigame arena — and spawns before the
    /// first player does. A chat component on the player prefab would instead exist once per human,
    /// lose messages that arrive before your own body spawns, and have nowhere to put a system
    /// message on a machine with no local player.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class ChatNetwork : NetworkBehaviour
    {
        /// <summary>Messages a client may send back-to-back before the throttle bites.</summary>
        private const int BurstMessages = 4;

        /// <summary>Seconds one message costs, once the burst is spent.</summary>
        private const float RefillSeconds = 1.2f;

        /// <summary>How long a join announcement waits for the joiner's chosen name to replicate.</summary>
        private const float NameWaitSeconds = 6f;

        private static ChatNetwork instance;

        /// <summary>The session's chat, or null when there is none (offline, or before spawn).</summary>
        public static ChatNetwork Instance => instance != null ? instance : null;

        // Server-side. The moment each client's next message becomes free, as a token bucket:
        // spending is moving this forward, and a client whose credit runs past the horizon is
        // sending faster than the bucket refills.
        private readonly Dictionary<ulong, float> creditUntil = new();

        // Clients already told they are going too fast, so the throttle itself cannot be turned
        // into a flood of "slow down" notices.
        private readonly HashSet<ulong> throttled = new();

        // Server-side. Last known name per client, so a leave message can name somebody whose
        // player object has already been destroyed by the time we hear about it.
        private readonly Dictionary<ulong, string> lastKnownName = new();

        // ------------------------------------------------------------------ life cycle

        public override void OnNetworkSpawn()
        {
            instance = this;

            // A spawn of this object is the start of a session on this machine — a fresh host, or
            // this client joining one. Clearing here rather than on disconnect means the log is
            // emptied by an event every peer actually observes; a hard disconnect raises nothing.
            ChatLog.Clear();

            if (!IsServer) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;

            // Clients that connected before this object spawned — on a host, that is the host's
            // own client, which connects during startup.
            foreach (ulong id in manager.ConnectedClientsIds) OnClientConnected(id);
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this) instance = null;

            // This object exists only for the duration of a session, so its despawn is the session
            // ending — leaving to the main menu, or being disconnected. The log goes with it: the
            // chat window outlives every scene load (it has to, or world streaming and interiors
            // would empty it), so without this the last thing anyone said is still sitting on the
            // main menu behind the buttons.
            ChatLog.Clear();

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            creditUntil.Clear();
            throttled.Clear();
            lastKnownName.Clear();
        }

        public override void OnDestroy()
        {
            // Not merely tidiness: OnNetworkDespawn does not run for an object destroyed without a
            // despawn — a hard disconnect, or the scene unload that happens on the way back to the
            // main menu — and that is the common case for leaving a game rather than the rare one.
            // Both the stale static and the leftover log would otherwise survive the session.
            if (instance == this) instance = null;

            ChatLog.Clear();

            base.OnDestroy();
        }

        // ---------------------------------------------------------------------- sending

        /// <summary>
        /// Sends what the local player typed.
        /// <para>
        /// Degrades the way the rest of this codebase does: with no session, no spawned chat object
        /// or no network at all, the line is handled here and now — which is the correct
        /// single-player behaviour and means the caller never has to ask which case it is in.
        /// </para>
        /// </summary>
        public static void Send(string raw)
        {
            string text = ChatText.Sanitize(raw);
            if (text.Length == 0) return;

            if (instance == null || !instance.IsSpawned || !Network.IsNetworked)
            {
                HandleLocally(text);
                return;
            }

            // Already the server: no round trip. Same shortcut NetRelay takes, and for the same
            // reason — it keeps the host on one code path with a remote client without depending
            // on what SenderClientId reads as during a locally invoked RPC.
            if (instance.IsServer) instance.Handle(Network.LocalClientId, text);
            else instance.SubmitRpc(new FixedString512Bytes(text));
        }

        /// <summary>
        /// Server-side: tells the whole session something. Offline it is simply shown here.
        /// Public so other systems (a match starting, a world event) can speak into the log.
        /// </summary>
        public static void Announce(string text)
        {
            text = ChatText.Sanitize(text);
            if (text.Length == 0) return;

            if (instance == null || !instance.IsSpawned || !Network.IsNetworked || !instance.IsServer)
            {
                ChatLog.AddSystem(text);
                return;
            }

            instance.SystemRpc(new FixedString512Bytes(text));
        }

        /// <summary>Server-side: tells one player something, privately.</summary>
        public static void Notify(ulong clientId, string text)
        {
            text = ChatText.Sanitize(text);
            if (text.Length == 0) return;

            if (instance == null || !instance.IsSpawned || !Network.IsNetworked || !instance.IsServer)
            {
                ChatLog.AddNotice(text);
                return;
            }

            // RpcTarget is an instance property on NetworkBehaviour, not a static one — it is the
            // behaviour's own target factory.
            instance.NoticeRpc(new FixedString512Bytes(text),
                               instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>The single-player path, and the one taken before a session exists.</summary>
        private static void HandleLocally(string text)
        {
            if (ChatCommands.IsCommand(text))
            {
                string reply = ChatCommands.Execute(Network.LocalClientId, text);
                if (!string.IsNullOrEmpty(reply)) ChatLog.AddNotice(reply);
                return;
            }

            ChatLog.AddPlayer(LocalName(), text);
        }

        private static string LocalName()
        {
            string name = GameSettings.SanitiseName(GameSettings.PlayerName);
            return string.IsNullOrWhiteSpace(name) ? "Player 1" : name;
        }

        // -------------------------------------------------------------------------- RPCs

        /// <summary>
        /// A client offering a line. Everything trustworthy about it is decided here.
        /// <para>
        /// The name is looked up from the sender's replicated <see cref="PlayerIdentity"/> and
        /// never taken from the message, so no client can put words in another player's mouth. The
        /// text is sanitised a second time even though the sender already did: the first pass ran
        /// on their machine, which is not a place this server gets to trust.
        /// </para>
        /// </summary>
        [Rpc(SendTo.Server)]
        private void SubmitRpc(FixedString512Bytes raw, RpcParams rpcParams = default) =>
            Handle(rpcParams.Receive.SenderClientId, raw.ToString());

        /// <summary>Server-side: what to do with a line from <paramref name="sender"/>.</summary>
        private void Handle(ulong sender, string raw)
        {
            string text = ChatText.Sanitize(raw);
            if (text.Length == 0) return;

            if (!AllowFrom(sender))
            {
                // Once per spree, not once per message — otherwise the throttle answers a flood
                // with a flood.
                if (throttled.Add(sender))
                    Notify(sender, "You are sending messages too quickly.");
                return;
            }

            throttled.Remove(sender);

            if (ChatCommands.IsCommand(text))
            {
                string reply = ChatCommands.Execute(sender, text);
                if (!string.IsNullOrEmpty(reply)) Notify(sender, reply);
                return;
            }

            BroadcastRpc(new FixedString64Bytes(NameOf(sender)), new FixedString512Bytes(text));
        }

        [Rpc(SendTo.Everyone)]
        private void BroadcastRpc(FixedString64Bytes sender, FixedString512Bytes text) =>
            ChatLog.AddPlayer(sender.ToString(), text.ToString());

        [Rpc(SendTo.Everyone)]
        private void SystemRpc(FixedString512Bytes text) => ChatLog.AddSystem(text.ToString());

        [Rpc(SendTo.SpecifiedInParams)]
        private void NoticeRpc(FixedString512Bytes text, RpcParams rpcParams) =>
            ChatLog.AddNotice(text.ToString());

        // ------------------------------------------------------------------- throttling

        /// <summary>
        /// Token bucket, server-side: <see cref="BurstMessages"/> in hand, then one every
        /// <see cref="RefillSeconds"/>. Unscaled time, so a host whose clock is stopped by its own
        /// pause menu does not hand every client an unlimited allowance.
        /// </summary>
        private bool AllowFrom(ulong client)
        {
            float now = Time.unscaledTime;
            float horizon = now + BurstMessages * RefillSeconds;

            creditUntil.TryGetValue(client, out float next);
            if (next < now) next = now;

            if (next + RefillSeconds > horizon) return false;

            creditUntil[client] = next + RefillSeconds;
            return true;
        }

        // ------------------------------------------------------------------ join / leave

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            StartCoroutine(AnnounceJoin(clientId));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            creditUntil.Remove(clientId);
            throttled.Remove(clientId);

            if (!lastKnownName.TryGetValue(clientId, out string name))
                name = $"Player {clientId + 1}";

            lastKnownName.Remove(clientId);

            Announce($"{name} left the game.");
        }

        /// <summary>
        /// Waits for the joiner to be somebody before saying they arrived.
        /// <para>
        /// A connection callback fires well before that client's player object has spawned, and the
        /// name is a NetworkVariable the owner writes after that — so announcing immediately says
        /// "Player 3 joined the game" about somebody who has a name. The wait is bounded, because a
        /// player who never publishes one still deserves an announcement.
        /// </para>
        /// </summary>
        private IEnumerator AnnounceJoin(ulong clientId)
        {
            float deadline = Time.unscaledTime + NameWaitSeconds;

            while (Time.unscaledTime < deadline)
            {
                PlayerIdentity identity = FindIdentity(clientId);
                if (identity != null && identity.HasPublishedName) break;

                yield return null;
            }

            // Left again during the wait — the disconnect announcement has already been and gone,
            // and "X joined" after "X left" reads as a bug.
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) yield break;
            if (!StillConnected(manager, clientId)) yield break;

            string name = NameOf(clientId);
            lastKnownName[clientId] = name;

            Announce($"{name} joined the game.");
        }

        /// <summary>
        /// The display name for a client, from the one roster every peer holds a full copy of.
        /// Falls back to the same <c>Player N</c> shape <see cref="PlayerIdentity"/> uses, so a
        /// nameless player is labelled identically wherever they appear.
        /// </summary>
        private string NameOf(ulong clientId)
        {
            PlayerIdentity identity = FindIdentity(clientId);
            if (identity == null) return $"Player {clientId + 1}";

            string name = identity.DisplayName;
            lastKnownName[clientId] = name;
            return name;
        }

        /// <summary>A loop rather than LINQ's Contains: this runs inside a per-frame wait.</summary>
        private static bool StillConnected(NetworkManager manager, ulong clientId)
        {
            var ids = manager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == clientId) return true;

            return false;
        }

        private static PlayerIdentity FindIdentity(ulong clientId)
        {
            var roster = PlayerIdentity.All;

            for (int i = 0; i < roster.Count; i++)
            {
                PlayerIdentity identity = roster[i];
                if (identity != null && identity.IsSpawned && identity.OwnerClientId == clientId)
                    return identity;
            }

            return null;
        }
    }
}
