using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>The outcome of a connection attempt. Never an exception — see <see cref="SessionLauncher"/>.</summary>
    public readonly struct SessionResult
    {
        public readonly bool Success;

        /// <summary>Ready to show a player verbatim. Empty when <see cref="Success"/>.</summary>
        public readonly string Error;

        /// <summary>Relay join code, when hosting over Relay. Empty otherwise.</summary>
        public readonly string JoinCode;

        private SessionResult(bool success, string error, string joinCode)
        {
            Success = success;
            Error = error ?? string.Empty;
            JoinCode = joinCode ?? string.Empty;
        }

        public static SessionResult Ok(string joinCode = null) => new(true, null, joinCode);
        public static SessionResult Fail(string error) => new(false, error, null);
    }

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
    /// </summary>
    public static class SessionLauncher
    {
        /// <summary>How long a client waits for the handshake before calling it a failure.</summary>
        public const float ConnectTimeoutSeconds = 15f;

        /// <summary>Default port for the Relay-free direct path. TEST ONLY — see HostDirect.</summary>
        public const ushort DefaultDirectPort = 7777;

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
        /// Signs this process in under its own UGS profile: <c>-sgprofile client</c>.
        ///
        /// Needed to run two instances of the game on ONE machine. Every instance on a machine
        /// shares a single PlayerPrefs file, the anonymous credential is cached in it, and
        /// anonymous sign-in restores the SAME PlayerId from that cache — so without this the
        /// second instance is not a second player. Lobby memberships are keyed by PlayerId, so it
        /// is then refused from a lobby it is already a member of (409, see
        /// LobbySession.JoinWithConflictRecoveryAsync) and the recovery sweep hands back the
        /// membership the first instance is hosting on.
        ///
        /// A profile is just a namespace inside PlayerPrefs, so a distinct one buys a distinct
        /// cached credential and therefore a distinct player.
        /// </summary>
        public const string ProfileArg = "-sgprofile";

        /// <summary>
        /// Marks an extra editor instance launched by Multiplayer Play Mode; <c>-name</c> carries
        /// its instance name ("Player2"…). The Authentication SDK reads this exact pair itself
        /// (AuthenticationPackageInitializer.GetProfile) but only inside UNITY_EDITOR and only
        /// while the profile is still "default", so resolving it here — to the same name — keeps
        /// one visible owner of the decision rather than a silent fallback.
        /// </summary>
        private const string EditorModeArg = "-editor-mode";
        private const string NameArg = "-name";

        /// <summary>A ParrelSync clone lives in "&lt;project&gt;_clone_N", sharing the original's PlayerPrefs.</summary>
        private const string CloneMarker = "_clone_";

        /// <summary>UGS rejects a profile outside <c>^[a-zA-Z0-9_-]{1,30}$</c>.</summary>
        private const int MaxProfileLength = 30;

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
                    string profile = ResolveProfileName(Environment.GetCommandLineArgs(), Application.dataPath);
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

        // ─────────────────────────────────────────────
        //  Relay
        // ─────────────────────────────────────────────

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

        // ─────────────────────────────────────────────
        //  Direct — TEST ONLY. Not a way into the game.
        //
        //  Relay is the only route a player can take. These two methods exist solely so
        //  MultiplayerAutotest can stand up a host and a client in two separate processes on
        //  127.0.0.1, which is the only way the client half of the netcode can be tested at all:
        //  this codebase asks NetworkManager.Singleton who it is, so a second manager in the same
        //  process is invisible to Network.IsNetworked/Simulates/Owns.
        //
        //  Relay cannot serve that test. It needs UGS auth, a live allocation, and a join code that
        //  only exists at runtime on the host — and two -batchmode processes have no channel to
        //  pass that code between them. A fixed loopback address needs no coordination.
        //
        //  The player-facing half of this path — DirectConnectController, the retired lobby's
        //  "Direct" tab — was deleted. Do not wire these to a menu; that reintroduces the second
        //  route that deletion existed to remove.
        // ─────────────────────────────────────────────

        /// <summary>
        /// TEST ONLY — see the section comment above. Hosts on a plain UDP socket, touching no Unity
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
        /// TEST ONLY — see the section comment above. Connects straight to an address, then waits
        /// for the handshake. Called by <see cref="MultiplayerAutotest"/>; not reachable from any menu.
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

        // ─────────────────────────────────────────────
        //  Shared
        // ─────────────────────────────────────────────

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

        /// <summary>
        /// This machine's LAN address, or loopback if offline. Only <see cref="HostDirect"/> uses it,
        /// to bind the test host's advertised address — no player is ever shown an IP.
        /// </summary>
        public static string GetLocalIPv4()
        {
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface nic in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ip in
                             nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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

        /// <summary>
        /// The UGS profile this process should sign in under, or null to leave the SDK default
        /// alone. Pure so it can be tested without a live service — see SessionProfileTests, and
        /// <see cref="ProfileArg"/> for why any of this exists.
        ///
        /// Three ways an instance can be told apart, most explicit first:
        ///   1. <c>-sgprofile &lt;name&gt;</c>  — the only one that works for a BUILT player run
        ///      beside the editor, which is otherwise indistinguishable from it.
        ///   2. MPPM's <c>-editor-mode -name Player2</c>.
        ///   3. A ParrelSync clone folder.
        ///
        /// Returning null for an ordinary launch is the important case: a real player's PlayerId
        /// has to survive relaunching the game, which means keeping the default profile.
        /// </summary>
        public static string ResolveProfileName(string[] args, string projectPath)
        {
            string explicitProfile = ArgValue(args, ProfileArg);
            if (explicitProfile != null) return Sanitise(explicitProfile);

            // -name alone is a stock Unity argument; only the pair means a virtual player.
            if (HasArg(args, EditorModeArg))
            {
                string instanceName = ArgValue(args, NameArg);
                if (instanceName != null) return Sanitise(instanceName);
            }

            int marker = projectPath == null ? -1 : projectPath.LastIndexOf(CloneMarker, StringComparison.Ordinal);
            if (marker < 0) return null;

            // "…/SpaceGame_clone_0/Assets" → "clone_0". Cut at the separator or the profile
            // carries the rest of the path, which sanitising would turn into underscores.
            string tail = projectPath.Substring(marker + 1);
            int separator = tail.IndexOfAny(new[] { '/', '\\' });

            return Sanitise(separator < 0 ? tail : tail.Substring(0, separator));
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;

            foreach (string arg in args)
                if (arg == name) return true;

            return false;
        }

        /// <summary>The value after <paramref name="name"/>, or null — including when it is last.</summary>
        private static string ArgValue(string[] args, string name)
        {
            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];

            return null;
        }

        /// <summary>
        /// Forces a name into what SetProfile accepts. It throws on anything else, and it is
        /// called from the one method here that is not allowed to throw (rule 1 on the class).
        /// ASCII only on purpose: char.IsLetterOrDigit passes 'é', the SDK's regex does not.
        /// </summary>
        private static string Sanitise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var builder = new System.Text.StringBuilder(MaxProfileLength);
            foreach (char c in raw.Trim())
            {
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                               (c >= '0' && c <= '9') || c == '-' || c == '_';

                builder.Append(allowed ? c : '_');

                if (builder.Length == MaxProfileLength) break;
            }

            return builder.ToString();
        }

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
