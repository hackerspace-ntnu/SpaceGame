// Voice on the wire, over NGO's named messages rather than RPCs.
//
// ## Why not a NetworkBehaviour, the way ChatNetwork does it
//
// Because voice has to work in the LOBBY. The netcode session is already up there -- the host
// StartHosts before it creates the lobby, and a joiner is connected to Relay before the roster
// appears -- but `persistentScene` has not loaded, so NetworkGameManager does not exist and there
// is no spawned NetworkObject anywhere to hang an [Rpc] on. Named messages need none: they work
// from the moment the connection is listening, in the menu and in the world alike.
//
// They also happen to be a better fit in their own right. CustomMessagingManager sends to ONE
// client or to a chosen list, which is exactly the shape of "only the people in earshot", and it
// takes a NetworkDelivery -- so voice can be Unreliable, which it must be. A reliable voice stream
// head-of-line blocks on the first lost packet and the latency never recovers.
//
// On a host, NGO invokes the handler locally for any send that names the host's own client id, so
// the host hears relayed voice and receives its own roster through the same paths as a client.
//
// ## Messages
//
// - Up (client to server, unreliable): one encoded frame. The sender's id arrives beside it.
// - Down (server to listener, unreliable): speaker id, then one encoded frame.
// - Name (client to server, reliable): this player's display name and Unity account id.
// - Roster (server to everyone, reliable + fragmented): the whole VoiceRoster table, which can
//   exceed one packet at 24 names.
//
// ## Trust
//
// Down and Roster are only accepted FROM THE SERVER. Both carry client ids inside the payload, and
// a client that could send either to the host could make the host hear audio attributed to anyone,
// or rename anyone -- the host is the one peer every client can reach directly. Up and Name are
// attributed to the id they arrived from, never to an id written inside them.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>
    /// Carries encoded voice frames and the name table between peers. Nothing here throws: with no
    /// session every send is a no-op and every handler is simply never registered.
    /// </summary>
    public sealed class VoiceTransport : IDisposable
    {
        public const string UpMessage = "SpaceGame.Voice.Up";
        public const string DownMessage = "SpaceGame.Voice.Down";
        public const string NameMessage = "SpaceGame.Voice.Name";
        public const string RosterMessage = "SpaceGame.Voice.Roster";

        /// <summary>Server side: <c>(sender, buffer, length)</c> for a frame a client sent up.</summary>
        public event Action<ulong, byte[], int> FrameFromClient;

        /// <summary>Listener side: <c>(speaker, buffer, length)</c> for a frame to play.</summary>
        public event Action<ulong, byte[], int> FrameToPlay;

        /// <summary>Server side: <c>(sender, identity)</c> — a peer announcing who it is.</summary>
        public event Action<ulong, VoiceRoster.Entry> NameFromClient;

        /// <summary>Every peer: the server's current identity table. The dictionary is reused.</summary>
        public event Action<IReadOnlyDictionary<ulong, VoiceRoster.Entry>> RosterReceived;

        // Not readonly, and not an oversight: FastBufferReader.ReadBytesSafe takes its destination
        // by ref (it will grow the array if it has to), and a readonly field cannot be passed that
        // way. All four are allocated once here and never reassigned by this class.
        private byte[] inbound = new byte[VoiceFormat.MaxPacketBytes + VoiceFormat.ClientIdBytes];
        private byte[] payload = new byte[VoiceFormat.MaxPacketBytes];
        private byte[] nameIn = new byte[VoiceRoster.MaxIdentityBytes];
        private byte[] rosterIn = new byte[VoiceRoster.MaxEncodedBytes];

        private readonly byte[] header = new byte[VoiceFormat.ClientIdBytes];
        private readonly byte[] rosterOut = new byte[VoiceRoster.MaxEncodedBytes];
        private readonly byte[] identityOut = new byte[VoiceRoster.MaxIdentityBytes];
        private readonly Dictionary<ulong, VoiceRoster.Entry> rosterScratch =
            new Dictionary<ulong, VoiceRoster.Entry>();

        private NetworkManager registeredOn;

        public bool IsRegistered => registeredOn != null;

        /// <summary>
        /// Attaches to the live session. Safe to call every frame — it re-attaches when the
        /// NetworkManager has been replaced, which happens on every new session.
        /// </summary>
        public void Register()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;
            if (ReferenceEquals(registeredOn, manager)) return;

            Unregister();

            CustomMessagingManager messaging = manager.CustomMessagingManager;
            if (messaging == null) return;

            // A host is both server and client, so everything is registered on every peer and the
            // handlers guard instead.
            messaging.RegisterNamedMessageHandler(UpMessage, OnUp);
            messaging.RegisterNamedMessageHandler(DownMessage, OnDown);
            messaging.RegisterNamedMessageHandler(NameMessage, OnName);
            messaging.RegisterNamedMessageHandler(RosterMessage, OnRoster);
            registeredOn = manager;
        }

        public void Unregister()
        {
            if (registeredOn == null) return;

            // The manager may already be torn down; its messaging goes null with it.
            CustomMessagingManager messaging = registeredOn.CustomMessagingManager;
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(UpMessage);
                messaging.UnregisterNamedMessageHandler(DownMessage);
                messaging.UnregisterNamedMessageHandler(NameMessage);
                messaging.UnregisterNamedMessageHandler(RosterMessage);
            }

            registeredOn = null;
        }

        // --------------------------------------------------------------------- sending

        /// <summary>Sends one encoded frame to the server for routing.</summary>
        public void SendToServer(byte[] packet, int length)
        {
            if (registeredOn == null || packet == null || length <= 0) return;
            if (length > VoiceFormat.MaxPacketBytes) return;

            Send(UpMessage, packet, length, NetworkDelivery.Unreliable,
                 (messaging, writer) => messaging.SendNamedMessage(
                     UpMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable));
        }

        /// <summary>
        /// Server side: forwards <paramref name="speaker"/>'s frame to exactly
        /// <paramref name="listeners"/>. The caller decides who those are.
        /// </summary>
        public void SendToListeners(IReadOnlyList<ulong> listeners, ulong speaker,
                                    byte[] packet, int length)
        {
            // An empty list is not merely a no-op to NGO: SendNamedMessage logs an error for it.
            if (registeredOn == null || listeners == null || listeners.Count == 0) return;
            if (packet == null || length <= 0 || length > VoiceFormat.MaxPacketBytes) return;

            try
            {
                using var writer = new FastBufferWriter(VoiceFormat.ClientIdBytes + length, Allocator.Temp);

                VoiceFormat.WriteClientId(header, 0, speaker);
                writer.WriteBytesSafe(header, VoiceFormat.ClientIdBytes);
                writer.WriteBytesSafe(packet, length);

                registeredOn.CustomMessagingManager.SendNamedMessage(
                    DownMessage, listeners, writer, NetworkDelivery.Unreliable);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] Could not relay a frame: {e.Message}");
            }
        }

        /// <summary>Tells the server who this player is: display name and Unity account id.</summary>
        public void SendIdentity(string name, string accountId)
        {
            if (registeredOn == null) return;

            int length = VoiceRoster.EncodeIdentity(name, accountId, identityOut);

            Send(NameMessage, identityOut, length, NetworkDelivery.Reliable,
                 (messaging, writer) => messaging.SendNamedMessage(
                     NameMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable));
        }

        /// <summary>Server side: hands the whole name table to every peer, the host included.</summary>
        public void SendRoster(IReadOnlyDictionary<ulong, VoiceRoster.Entry> names)
        {
            if (registeredOn == null || !Network.Server) return;

            int length = VoiceRoster.Encode(names, rosterOut);

            // Fragmented: two dozen names can exceed a single packet, and a reliable message that
            // is too large for one is refused outright rather than split.
            Send(RosterMessage, rosterOut, length, NetworkDelivery.ReliableFragmentedSequenced,
                 (messaging, writer) => messaging.SendNamedMessageToAll(
                     RosterMessage, writer, NetworkDelivery.ReliableFragmentedSequenced));
        }

        public void Dispose() => Unregister();

        // -------------------------------------------------------------------- receiving

        private void OnUp(ulong sender, FastBufferReader reader)
        {
            if (!Network.Server) return;

            int size = reader.Length - reader.Position;
            if (size <= 0 || size > VoiceFormat.MaxPacketBytes) return;

            reader.ReadBytesSafe(ref payload, size);
            FrameFromClient?.Invoke(sender, payload, size);
        }

        private void OnDown(ulong sender, FastBufferReader reader)
        {
            // See Trust in the header: the speaker id is INSIDE this payload, so only the server's
            // word for it counts.
            if (sender != NetworkManager.ServerClientId) return;

            int size = reader.Length - reader.Position;
            if (size <= VoiceFormat.ClientIdBytes || size > inbound.Length) return;

            reader.ReadBytesSafe(ref inbound, size);

            ulong speaker = VoiceFormat.ReadClientId(inbound, 0);
            int length = size - VoiceFormat.ClientIdBytes;

            Array.Copy(inbound, VoiceFormat.ClientIdBytes, payload, 0, length);
            FrameToPlay?.Invoke(speaker, payload, length);
        }

        private void OnName(ulong sender, FastBufferReader reader)
        {
            if (!Network.Server) return;

            int size = reader.Length - reader.Position;
            if (size <= 0 || size > VoiceRoster.MaxIdentityBytes) return;

            reader.ReadBytesSafe(ref nameIn, size);

            if (!VoiceRoster.DecodeIdentity(nameIn, size, out VoiceRoster.Entry identity))
            {
                Debug.LogWarning($"[Voice] Ignored a malformed identity from client {sender}.");
                return;
            }

            NameFromClient?.Invoke(sender, identity);
        }

        private void OnRoster(ulong sender, FastBufferReader reader)
        {
            // Only the server's table. The ids are inside the payload, as with Down.
            if (sender != NetworkManager.ServerClientId) return;

            int size = reader.Length - reader.Position;
            if (size <= 0 || size > rosterIn.Length) return;

            reader.ReadBytesSafe(ref rosterIn, size);

            if (!VoiceRoster.Decode(rosterIn, size, rosterScratch))
            {
                Debug.LogWarning("[Voice] Ignored a malformed name table from the server.");
                return;
            }

            RosterReceived?.Invoke(rosterScratch);
        }

        // --------------------------------------------------------------------- plumbing

        /// <summary>The write-then-send shape every plain (unprefixed) message shares.</summary>
        private void Send(string message, byte[] bytes, int length, NetworkDelivery delivery,
                          Action<CustomMessagingManager, FastBufferWriter> dispatch)
        {
            try
            {
                using var writer = new FastBufferWriter(length, Allocator.Temp);
                writer.WriteBytesSafe(bytes, length);
                dispatch(registeredOn.CustomMessagingManager, writer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] Could not send {message} ({delivery}): {e.Message}");
            }
        }
    }
}
