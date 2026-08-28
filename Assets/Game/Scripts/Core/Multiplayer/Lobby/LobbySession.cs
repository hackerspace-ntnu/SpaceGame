using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The one owner of lobby state, for as long as the application runs.
    ///
    /// It is separate from the menu because the menu is destroyed by the load into the world, and
    /// the session must not be. A lobby that stops being heartbeated is delisted after 30 seconds,
    /// so a session tied to the menu scene could never be joined once the host started playing —
    /// which is exactly the thing "start now, let friends in later" requires.
    ///
    /// Everything above this is disposable view. Nothing here touches UnityEngine.UI, and no method
    /// throws across the boundary: failures arrive as <see cref="Failed"/> with a message already
    /// fit to show a player.
    ///
    /// This file holds the state and its lifecycle. The service operations live in the other
    /// partials — hosting, joining and browsing — and everything pure lives beside them:
    /// <see cref="LobbyOptions"/>, <see cref="LobbyRoster"/>, <see cref="LobbyTeams"/>,
    /// <see cref="LobbyJoinRecovery"/> and <see cref="LobbyServiceErrors"/>.
    /// </summary>
    public partial class LobbySession : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        private static LobbySession instance;

        /// <summary>
        /// The session, created on first use.
        ///
        /// Not placed in a scene: it has to outlive every scene, including the one that would have
        /// held it. Created lazily rather than from Bootstrap so entering the menu directly in the
        /// editor still works — the same reason NetworkBootstrap backfills its manager.
        /// </summary>
        public static LobbySession Instance
        {
            get
            {
                if (instance != null) return instance;

                var host = new GameObject(nameof(LobbySession));
                instance = host.AddComponent<LobbySession>();
                DontDestroyOnLoad(host);
                return instance;
            }
        }

        public LobbyState State { get; private set; } = LobbyState.Idle;

        /// <summary>The lobby this peer is in, or null. Refreshed by the poll.</summary>
        public Lobby Current { get; private set; }

        public bool IsHost =>
            Current != null && IsSignedIn && Current.HostId == LocalPlayerId;

        /// <summary>
        /// Which row of the current lobby is us, or -1.
        ///
        /// The instance half of <see cref="LobbyRoster.SlotOf"/>: it needs the authentication
        /// service, which is exactly what the views are kept away from so they stay testable
        /// without one.
        /// </summary>
        public int LocalSlot =>
            Current != null && IsSignedIn ? LobbyRoster.SlotOf(Current, LocalPlayerId) : -1;

        /// <summary>Raised whenever the roster, the code or the state moved. The view redraws from this.</summary>
        public event Action Changed;

        /// <summary>A message fit to show a player. Never an exception across this boundary.</summary>
        public event Action<string> Failed;

        private readonly LobbyHeartbeat heartbeat = new();
        private readonly LobbyPlayerPublisher publisher = new();
        private LobbyPoll poll;

        /// <summary>
        /// One operation at a time. Double-clicking Create used to allocate two Relay servers and
        /// two lobbies, then orphan the second pair when the first finished last.
        /// </summary>
        private bool busy;

        /// <summary>
        /// Keeps <see cref="HandleClientDisconnect"/> attached to whichever NetworkManager is live.
        /// A single subscription in <see cref="Awake"/> silently did nothing whenever the manager
        /// did not exist yet — see <see cref="DisconnectHook"/>.
        /// </summary>
        private DisconnectHook disconnects;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            poll = new LobbyPoll(OnPolled, OnLobbyClosed);
            disconnects = new DisconnectHook(HandleClientDisconnect);
            disconnects.Poll();
        }

        private void OnDestroy()
        {
            disconnects?.Detach();
        }

        private void Update()
        {
            // Re-checked every frame rather than assumed once: it is what makes a manager that
            // appears late — or is replaced between Play sessions — still reach us.
            disconnects?.Poll();

            string lobbyId = Current?.Id;
            float deltaTime = Time.deltaTime;

            heartbeat.Tick(deltaTime, IsHost ? lobbyId : null);

            // Null only for a duplicate instance that Awake has already condemned; Destroy lands at
            // the end of the frame, so this Update can still run once.
            poll?.Tick(deltaTime, lobbyId);
            FlushPublisher(deltaTime);
        }

        /// <summary>
        /// Hands the lobby membership back on the way out.
        ///
        /// Best-effort and often too late — quitting tears down the request loop, and stopping play
        /// mode in the editor gives it no chance at all. It is sent anyway because when it does land
        /// it frees the slot immediately, and <see cref="LobbyJoinRecovery"/> clears up whatever
        /// survives on the next join.
        /// </summary>
        private void OnApplicationQuit()
        {
            _ = RemoveSelfQuietly(Current?.Id);
        }

        // ─────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────

        /// <summary>Signed in and ready for Relay/Lobby calls. Safe to await repeatedly.</summary>
        public async Task<bool> EnsureReadyAsync()
        {
            SessionResult services = await SessionLauncher.EnsureServicesAsync();
            if (!services.Success) Fail(services.Error);
            return services.Success;
        }

        public async Task LeaveAsync()
        {
            string lobbyId = Current?.Id;

            // Local state first, so the UI responds even if the service call fails or hangs.
            Forget();
            SessionLauncher.Shutdown();

            await RemoveSelfQuietly(lobbyId);
        }

        /// <summary>
        /// Hands the membership back on the way out of a session, with nobody waiting for it.
        ///
        /// <para>
        /// Leaving to the menu is synchronous and ends in a scene load, so it cannot await this: a
        /// player who pressed "Main Menu" would sit staring at a frozen world for as long as the
        /// Lobby service took to answer. Nor can it be skipped — the host's lobby then stayed
        /// listed until its heartbeat lapsed, and the membership survived as exactly the ghost
        /// <see cref="LobbyJoinRecovery"/> exists to clean up.
        /// </para>
        ///
        /// <para>
        /// Firing a task and walking away is only safe because of what it is fired at. This object
        /// is <see cref="DontDestroyOnLoad"/> and outlives the scene load, and the request itself
        /// runs inside <see cref="RemoveSelfQuietly"/>, which is static and swallows its own
        /// failures. The try only has to cover the part that runs before the first await, which is
        /// <see cref="Forget"/> raising <see cref="Changed"/> at whatever is still subscribed.
        /// </para>
        ///
        /// <para>
        /// Reads the backing field rather than <see cref="Instance"/> on purpose: Instance is a lazy
        /// factory, and asking it here would create a DontDestroyOnLoad LobbySession purely to be
        /// told there is no lobby to leave — on every singleplayer exit.
        /// </para>
        /// </summary>
        public static void LeaveInBackground()
        {
            LobbySession session = instance;
            if (session == null) return;

            try
            {
                _ = session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not start leaving the lobby: {e.Message}");
            }
        }

        /// <summary>
        /// Publishes the local player's suit colour to the lobby, coalescing bursts.
        ///
        /// Not routed through <see cref="TryBegin"/>: that guard exists to stop a double-click
        /// allocating two Relay servers, and blocking here would silently drop the press.
        /// </summary>
        public void PublishSuitColor(int suitColor) => publisher.RequestSuitColor(suitColor);

        /// <summary>Publishes which team the local player has moved to, in a VS lobby. See <see cref="PublishSuitColor"/>.</summary>
        public void PublishTeam(int team) => publisher.RequestTeam(team);

        /// <summary>Publishes the local player's opinion of their VS team's colour. See <see cref="PublishSuitColor"/>.</summary>
        public void PublishTeamColor(int swatch) => publisher.RequestTeamColor(swatch);

        /// <summary>This peer's own view of the roster, ready for a screen to read.</summary>
        public RosterSnapshot CurrentSnapshot() => LobbyRoster.Snapshot(Current, LocalSlot, SuitPalette.Count);

        // ─────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────

        /// <summary>The name other players see. One identity, shared with PlayerIdentity in-game.</summary>
        private static string PlayerName => GameSettings.PlayerName;

        /// <summary>The suit colour other players see. Same store PlayerIdentity publishes from.</summary>
        private static int SuitColor => GameSettings.SuitColorIndex;

        private static bool IsSignedIn => AuthenticationService.Instance.IsSignedIn;

        private static string LocalPlayerId => AuthenticationService.Instance.PlayerId;

        private void Fail(string message) => Failed?.Invoke(message);

        /// <summary>Takes a lobby as ours and tells the view.</summary>
        private void Adopt(Lobby lobby, LobbyState state)
        {
            Current = lobby;
            State = state;
            Changed?.Invoke();
        }

        private void Forget() => Adopt(null, LobbyState.Idle);

        private void OnPolled(Lobby lobby)
        {
            // Leaving nulls Current while the request is in flight; writing the stale result back
            // would resurrect a lobby we have already left.
            if (Current == null) return;

            Adopt(lobby, State);
        }

        private void OnLobbyClosed()
        {
            Fail("The host closed the lobby.");
            Forget();
            SessionLauncher.Shutdown();
        }

        /// <summary>
        /// Sends whatever the publisher is holding. When there is no lobby to publish to — left,
        /// or not yet signed in — it is cancelled rather than left holding its values.
        /// </summary>
        private void FlushPublisher(float deltaTime)
        {
            if (Current == null || !IsSignedIn)
            {
                publisher.Cancel();
                return;
            }

            publisher.Tick(deltaTime, SendPlayerUpdateAsync);
        }

        private async Task SendPlayerUpdateAsync(UpdatePlayerOptions options)
        {
            Lobby updated = await LobbyService.Instance.UpdatePlayerAsync(Current.Id, LocalPlayerId, options);
            Adopt(updated, State);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsHost) return;
            if (clientId != manager.LocalClientId) return;

            string reason = manager.DisconnectReason;
            Debug.Log($"[LobbySession] Disconnected. Reason: '{reason}'");

            // Read before Forget nulls it: the membership has to be handed back, not just dropped
            // locally, or the next attempt to join that lobby is refused with "player is already a
            // member of the lobby" — anonymous auth reuses the same player id.
            string lobbyId = Current?.Id;

            // Any disconnect strands us, not just a clean host shutdown. Matching on the exact
            // reason string meant a host crash, a dropped connection or a Relay timeout left the
            // player sitting in a session that no longer existed.
            Fail(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
            Forget();

            // Not awaited: this is a Netcode callback, and the UI has already been told. The
            // removal is best-effort by design — RemoveSelfQuietly never throws.
            _ = RemoveSelfQuietly(lobbyId);
        }

        /// <summary>Best-effort removal. Never reports — callers are already handling a failure.</summary>
        private static async Task RemoveSelfQuietly(string lobbyId)
        {
            try
            {
                if (!string.IsNullOrEmpty(lobbyId) && IsSignedIn)
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, LocalPlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not remove self from {lobbyId}: {e.Message}");
            }
        }

        private bool TryBegin()
        {
            if (busy)
            {
                Debug.Log("[LobbySession] Ignoring input — an operation is already running.");
                return false;
            }

            busy = true;
            return true;
        }
    }
}
