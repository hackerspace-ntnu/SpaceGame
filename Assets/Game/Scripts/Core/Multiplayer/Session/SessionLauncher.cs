using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The one place a network session is started, over any transport.
    ///
    /// Three rules hold for every method here, and they are the whole point of the class:
    ///
    /// 1. NOTHING THROWS. Every entry point returns a <see cref="SessionResult"/> carrying a message
    ///    fit to show a player. The lobby it replaced was built from `async void` handlers, so a
    ///    failed Relay allocation or an expired sign-in surfaced as a silently swallowed exception
    ///    and a menu button that simply did nothing.
    ///
    /// 2. CONNECTING IS AWAITED, NOT ASSUMED. `NetworkManager.StartClient()` returns true once the
    ///    attempt has been *dispatched*; the handshake fails later, asynchronously, over the wire.
    ///    Code that treats that bool as success shows a lobby screen for a session that never
    ///    connected. <see cref="WaitForClientConnectedAsync"/> waits for the real answer.
    ///
    /// 3. THE TRANSPORT IS ALWAYS RECONFIGURED. UnityTransport keeps whatever it was told last, so
    ///    a direct attempt after a Relay attempt would otherwise still dial the Relay server.
    ///    Every path here calls SetConnectionData or SetRelayServerData, both of which reset the
    ///    protocol as a side effect.
    ///
    /// This file holds what every transport shares — services sign-in, the connect wait, shutdown
    /// — and singleplayer's <see cref="HostLocal"/>. The player-facing Relay path lives in
    /// SessionLauncher.Relay.cs; the test-only direct path in SessionLauncher.Direct.cs. Which UGS
    /// profile to sign in under is <see cref="SessionProfile"/>'s decision.
    /// </summary>
    public static partial class SessionLauncher
    {
        /// <summary>How long a client waits for the handshake before calling it a failure.</summary>
        public const float ConnectTimeoutSeconds = 15f;

        // One shared init task. Several UI elements can race to the first UGS call on the same
        // frame, and UnityServices.InitializeAsync is not safe to have in flight twice.
        private static Task<SessionResult> servicesTask;

        public static bool IsRunning =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        /// <summary>Signed in and ready for Relay/Lobby calls. Safe to await repeatedly.</summary>
        public static async Task<SessionResult> EnsureServicesAsync()
        {
            servicesTask ??= InitialiseServicesAsync();

            SessionResult result = await servicesTask;

            // Don't cache a failure: the usual cause is a dropped network, and the player retrying
            // deserves a real second attempt rather than the first attempt's error replayed forever.
            if (!result.Success) servicesTask = null;

            return result;
        }

        private static async Task<SessionResult> InitialiseServicesAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();

                    // Must be decided here: the profile selects which cached credential the
                    // sign-in below restores, and it is fixed for the lifetime of the services.
                    string profile = SessionProfile.Resolve(Environment.GetCommandLineArgs(), Application.dataPath);
                    if (profile != null)
                    {
                        options.SetProfile(profile);
                        Debug.Log($"[SessionLauncher] Signing in under UGS profile '{profile}'.");
                    }

                    await UnityServices.InitializeAsync(options);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                return SessionResult.Ok();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Unity Services unavailable: {e}");
                // Deliberately offers no fallback route: Relay is the only way into a session, so
                // suggesting one would point the player at a screen that does not exist.
                return SessionResult.Fail(
                    "Could not reach Unity Gaming Services. Check your internet connection and try " +
                    $"again.\n({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// Hosts a session nobody else will join: singleplayer, which runs as a host of one.
        ///
        /// Deliberately does NOT call SetConnectionData. The transport keeps whatever the
        /// NetworkManager prefab was authored with — including its port, which is set to a value
        /// the project has already had to move once because Unity leaks the UDP socket on every
        /// Play session. Overriding it here would both re-break that workaround and bind the
        /// LAN interface for a session that only ever talks to itself.
        ///
        /// What it DOES do is clear stale Relay data. UnityTransport keeps its last configuration,
        /// so starting singleplayer after a Relay attempt in the same process would otherwise host
        /// on a dead allocation — the bug this exists to prevent.
        /// </summary>
        public static SessionResult HostLocal()
        {
            if (!TryGetTransport(out UnityTransport transport, out string transportError))
                return SessionResult.Fail(transportError);

            try
            {
                // Back to plain UDP on the prefab's own address and port, undoing any Relay setup
                // from earlier in this process.
                transport.UseWebSockets = false;
                transport.SetConnectionData(transport.ConnectionData.Address,
                                            transport.ConnectionData.Port,
                                            transport.ConnectionData.ServerListenAddress);

                Shutdown();

                return NetworkManager.Singleton.StartHost()
                    ? SessionResult.Ok()
                    : SessionResult.Fail($"Could not start a local session on port " +
                                         $"{transport.ConnectionData.Port}. Another program — or a " +
                                         $"leaked socket from a previous Play session — may be using it.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionLauncher] Local host failed: {e}");
                return SessionResult.Fail($"Could not start a local session.\n({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>
        /// Resolves when the local client is genuinely connected, genuinely rejected, or the wait
        /// times out. See rule 2 on the class: the bool from StartClient answers a different
        /// question than "am I in the session".
        /// </summary>
        public static async Task<SessionResult> WaitForClientConnectedAsync(float timeoutSeconds = ConnectTimeoutSeconds)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return SessionResult.Fail("No NetworkManager.");

            var completion = new TaskCompletionSource<SessionResult>();

            void OnConnected(ulong clientId)
            {
                if (clientId == networkManager.LocalClientId)
                    completion.TrySetResult(SessionResult.Ok());
            }

            void OnDisconnected(ulong clientId)
            {
                if (clientId != networkManager.LocalClientId) return;

                string reason = string.IsNullOrEmpty(networkManager.DisconnectReason)
                    ? "The host refused the connection."
                    : networkManager.DisconnectReason;

                completion.TrySetResult(SessionResult.Fail(reason));
            }

            networkManager.OnClientConnectedCallback += OnConnected;
            networkManager.OnClientDisconnectCallback += OnDisconnected;

            try
            {
                // Already connected before the handlers were attached — a real race on a LAN, where
                // the handshake can complete inside the same frame StartClient was called.
                if (networkManager.IsConnectedClient) return SessionResult.Ok();

                Task finished = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                if (finished != completion.Task)
                {
                    Shutdown();
                    return SessionResult.Fail(
                        $"Timed out after {timeoutSeconds:0}s with no response from the host. " +
                        "They may be offline, or a firewall may be blocking the connection.");
                }

                return await completion.Task;
            }
            finally
            {
                networkManager.OnClientConnectedCallback -= OnConnected;
                networkManager.OnClientDisconnectCallback -= OnDisconnected;
            }
        }

        /// <summary>Stops any session already running. Safe to call when nothing is.</summary>
        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }

        /// <summary>Relay codes are uppercase; players paste them in every case and with stray spaces.</summary>
        public static string NormalizeJoinCode(string code) =>
            string.IsNullOrEmpty(code) ? string.Empty : code.Trim().ToUpperInvariant();

        private static bool TryGetTransport(out UnityTransport transport, out string error)
        {
            transport = null;
            error = null;

            if (NetworkManager.Singleton == null)
            {
                error = "No NetworkManager in the scene. Start from the Bootstrap scene.";
                return false;
            }

            transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                error = "The NetworkManager has no UnityTransport component.";
                return false;
            }

            return true;
        }
    }
}
