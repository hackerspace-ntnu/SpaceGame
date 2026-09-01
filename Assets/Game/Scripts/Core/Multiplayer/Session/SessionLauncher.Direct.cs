// Direct — TEST ONLY. Not a way into the game.
//
// Relay is the only route a player can take. These two methods exist solely so
// MultiplayerAutotest can stand up a host and a client in two separate processes on
// 127.0.0.1, which is the only way the client half of the netcode can be tested at all:
// this codebase asks NetworkManager.Singleton who it is, so a second manager in the same
// process is invisible to Network.IsNetworked/Simulates/Owns.
//
// Relay cannot serve that test. It needs UGS auth, a live allocation, and a join code that
// only exists at runtime on the host — and two -batchmode processes have no channel to
// pass that code between them. A fixed loopback address needs no coordination.
//
// The player-facing half of this path — DirectConnectController, the retired lobby's
// "Direct" tab — was deleted. Do not wire these to a menu; that reintroduces the second
// route that deletion existed to remove.
using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SpaceGame.Core
{
    public static partial class SessionLauncher
    {
        /// <summary>Default port for the Relay-free direct path. TEST ONLY — see HostDirect.</summary>
        public const ushort DefaultDirectPort = 7777;

        /// <summary>
        /// TEST ONLY — see the file header. Hosts on a plain UDP socket, touching no Unity
        /// service. Called by <see cref="MultiplayerAutotest"/>; not reachable from any menu.
        /// </summary>
        public static SessionResult HostDirect(ushort port = DefaultDirectPort)
        {
            if (!TryGetTransport(out UnityTransport transport, out string transportError))
                return SessionResult.Fail(transportError);

            try
            {
                // Listen on 0.0.0.0 rather than the advertised address, or the socket binds to one
                // interface and refuses the LAN clients this mode exists to serve.
                transport.SetConnectionData(GetLocalIPv4(), port, "0.0.0.0");

                Shutdown();

                return NetworkManager.Singleton.StartHost()
                    ? SessionResult.Ok()
                    : SessionResult.Fail($"Could not listen on port {port}. Another program may be using it.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Direct host failed: {e}");
                return SessionResult.Fail($"Could not host on port {port}.\n({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// TEST ONLY — see the file header. Connects straight to an address, then waits for the
        /// handshake. Called by <see cref="MultiplayerAutotest"/>; not reachable from any menu.
        /// </summary>
        public static async Task<SessionResult> JoinDirectAsync(string address, ushort port = DefaultDirectPort)
        {
            if (string.IsNullOrWhiteSpace(address))
                return SessionResult.Fail("Enter the host's IP address first.");

            if (!TryGetTransport(out UnityTransport transport, out string transportError))
                return SessionResult.Fail(transportError);

            try
            {
                transport.SetConnectionData(address.Trim(), port);

                Shutdown();

                if (!NetworkManager.Singleton.StartClient())
                    return SessionResult.Fail("The client refused to start.");

                return await WaitForClientConnectedAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Direct join failed: {e}");
                return SessionResult.Fail($"Could not connect to {address}:{port}.\n({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// This machine's LAN address, or loopback if offline. Only <see cref="HostDirect"/> uses it,
        /// to bind the test host's advertised address — no player is ever shown an IP.
        /// </summary>
        public static string GetLocalIPv4()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ip.Address.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionLauncher] Could not read local IP: {e.Message}");
            }

            return "127.0.0.1";
        }
    }
}
