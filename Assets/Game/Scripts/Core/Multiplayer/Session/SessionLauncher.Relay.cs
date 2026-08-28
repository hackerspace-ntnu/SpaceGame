// Relay: the only route a player can take into a session. See SessionLauncher.cs for the rules.
using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace SpaceGame.Core
{
    public static partial class SessionLauncher
    {
        /// <summary>
        /// Relay endpoint protocol. "dtls" is encrypted UDP and is what Relay hands out by default.
        ///
        /// This constant is the bug that made Relay unusable. The old code built RelayServerData
        /// from `allocation.RelayServer` — a legacy field with no protocol attached — and hardcoded
        /// isSecure:false. When Relay allocated a DTLS endpoint (the normal case) the client then
        /// spoke plaintext UDP at a port expecting a DTLS handshake, so the connection was never
        /// refused, it just never completed: a lobby that hangs instead of erroring. Building from
        /// the allocation's endpoint list instead picks the host/port AND the matching secure flag
        /// together, so they cannot disagree.
        /// </summary>
        private const string RelayConnectionType = "dtls";

        /// <summary>
        /// Allocates a Relay server and starts hosting on it. On success the result carries the
        /// join code to publish to the lobby.
        /// </summary>
        public static async Task<SessionResult> HostRelayAsync(int maxPlayers)
        {
            SessionResult services = await EnsureServicesAsync();
            if (!services.Success) return services;

            if (!TryGetTransport(out UnityTransport transport, out string transportError))
                return SessionResult.Fail(transportError);

            try
            {
                // maxPlayers counts the host; Relay counts only the peers connecting to it.
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));

                Shutdown();

                if (!NetworkManager.Singleton.StartHost())
                    return SessionResult.Fail("Relay was allocated but the host failed to start.");

                return SessionResult.Ok(joinCode);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Relay host failed: {e}");
                return SessionResult.Fail($"Could not create a Relay server.\n({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>Joins a Relay allocation by join code and waits for the handshake to complete.</summary>
        public static async Task<SessionResult> JoinRelayAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                return SessionResult.Fail("Enter a join code first.");

            SessionResult services = await EnsureServicesAsync();
            if (!services.Success) return services;

            if (!TryGetTransport(out UnityTransport transport, out string transportError))
                return SessionResult.Fail(transportError);

            try
            {
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(NormalizeJoinCode(joinCode));

                transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));

                Shutdown();

                if (!NetworkManager.Singleton.StartClient())
                    return SessionResult.Fail("The client refused to start.");

                return await WaitForClientConnectedAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Relay join failed: {e}");
                return SessionResult.Fail($"Could not join that Relay code.\n({e.GetType().Name}: {e.Message})");
            }
        }
    }
}
