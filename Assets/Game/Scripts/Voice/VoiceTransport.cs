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
// ## Framing
//
// Up (client to server) is the encoded frame and nothing else; the sender's id arrives beside the
// payload. Down (server to listener) prefixes the speaker's id, because the listener has to know
// whose voice to mix. The id is packed by hand, little end first, rather than through
// WriteValueSafe -- eight bytes either way, and this makes the layout explicit at both ends.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>
    /// Carries encoded voice frames between peers. Nothing here throws: with no session, every send
    /// is a no-op and every handler is simply never registered.
    /// </summary>
    public sealed class VoiceTransport : IDisposable
    {
        /// <summary>Client to server: one encoded frame.</summary>
        public const string UpMessage = "SpaceGame.Voice.Up";

        /// <summary>Server to listener: speaker id, then one encoded frame.</summary>
        public const string DownMessage = "SpaceGame.Voice.Down";

        private const int SpeakerIdBytes = sizeof(ulong);

        /// <summary>Server side: <c>(sender, buffer, length)</c> for a frame a client sent up.</summary>
        public event Action<ulong, byte[], int> FrameFromClient;

        /// <summary>Listener side: <c>(speaker, buffer, length)</c> for a frame to play.</summary>
        public event Action<ulong, byte[], int> FrameToPlay;

        // Not readonly, and not an oversight: FastBufferReader.ReadBytesSafe takes its destination
        // by ref (it will grow the array if it has to), and a readonly field cannot be passed that
        // way. Both are allocated once here and never reassigned by this class.
        private byte[] inbound = new byte[VoiceFormat.MaxPacketBytes + SpeakerIdBytes];
        private byte[] payload = new byte[VoiceFormat.MaxPacketBytes];

        private NetworkManager registeredOn;

        public bool IsRegistered => registeredOn != null;

        /// <summary>
        /// Attaches to the live session. Safe to call repeatedly — it re-attaches when the
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

            // The server is the only thing that receives Up, and every peer receives Down — but a
            // host is both, so both are registered unconditionally and the handlers guard instead.
            messaging.RegisterNamedMessageHandler(UpMessage, OnUp);
            messaging.RegisterNamedMessageHandler(DownMessage, OnDown);
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
            }

            registeredOn = null;
        }

        /// <summary>Sends one encoded frame to the server for routing.</summary>
        public void SendToServer(byte[] packet, int length)
        {
            if (registeredOn == null || packet == null || length <= 0) return;
            if (length > VoiceFormat.MaxPacketBytes) return;

            try
            {
                using var writer = new FastBufferWriter(length, Allocator.Temp);
                writer.WriteBytesSafe(packet, length);

                registeredOn.CustomMessagingManager.SendNamedMessage(
                    UpMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] Could not send a frame upstream: {e.Message}");
            }
        }

        /// <summary>
        /// Server side: forwards <paramref name="speaker"/>'s frame to exactly
        /// <paramref name="listeners"/>. The caller decides who those are.
        /// </summary>
        public void SendToListeners(IReadOnlyList<ulong> listeners, ulong speaker,
                                    byte[] packet, int length)
        {
            if (registeredOn == null || listeners == null || listeners.Count == 0) return;
            if (packet == null || length <= 0 || length > VoiceFormat.MaxPacketBytes) return;

            try
            {
                using var writer = new FastBufferWriter(SpeakerIdBytes + length, Allocator.Temp);

                var header = new byte[SpeakerIdBytes];
                WriteId(header, speaker);

                writer.WriteBytesSafe(header, SpeakerIdBytes);
                writer.WriteBytesSafe(packet, length);

                registeredOn.CustomMessagingManager.SendNamedMessage(
                    DownMessage, listeners, writer, NetworkDelivery.Unreliable);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] Could not relay a frame: {e.Message}");
            }
        }

        public void Dispose() => Unregister();

        // ------------------------------------------------------------------ internals

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
            int size = reader.Length - reader.Position;
            if (size <= SpeakerIdBytes || size > inbound.Length) return;

            reader.ReadBytesSafe(ref inbound, size);

            ulong speaker = ReadId(inbound);
            int length = size - SpeakerIdBytes;

            Array.Copy(inbound, SpeakerIdBytes, payload, 0, length);
            FrameToPlay?.Invoke(speaker, payload, length);
        }

        // Packed by hand, least significant byte first, so both ends agree regardless of what the
        // machine's own endianness happens to be.
        private static void WriteId(byte[] into, ulong id)
        {
            for (int i = 0; i < SpeakerIdBytes; i++) into[i] = (byte)(id >> (i * 8));
        }

        private static ulong ReadId(byte[] from)
        {
            ulong id = 0;
            for (int i = 0; i < SpeakerIdBytes; i++) id |= (ulong)from[i] << (i * 8);
            return id;
        }
    }
}
