using System.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
    /// <summary>
    /// The thing that notices, from inside the world, that the session has ended.
    ///
    /// <para>
    /// <see cref="LobbySession"/> already handled a disconnect correctly — it reads the lobby id
    /// before forgetting it, hands the membership back, and raises <c>Failed</c> with the reason.
    /// What it had no answer for is WHO IS LISTENING. <c>Failed</c>'s only subscriber anywhere in
    /// the project is the lobby screen, and that screen was destroyed by the load into the world;
    /// nothing in the world scene subscribed to <c>OnClientDisconnectCallback</c> either. So a host
    /// quitting mid-game produced no message, no menu and no error. The world simply stopped
    /// updating around a player whose only way out was killing the process.
    /// </para>
    ///
    /// <para>
    /// Bootstrapped from a static rather than placed in a scene, for the same reason
    /// <c>PauseMenuUI</c> is: gameplay is spread over a persistent scene, streamed world chunks and
    /// an additively loaded arena, and a listener that has to exist in all of them cannot be
    /// authored into one of them. It costs one null test a frame until a session starts.
    /// </para>
    ///
    /// <para>
    /// Only clients act on this. A host leaving is not a disconnect to recover from — it is the
    /// session ending, deliberately, through the pause menu.
    /// </para>
    /// </summary>
    public class SessionWatchdog : MonoBehaviour
    {
        private static SessionWatchdog instance;

        private DisconnectHook disconnects;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(SessionWatchdog));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SessionWatchdog>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Through a hook rather than a plain subscription: this object is created
            // AfterSceneLoad and so is NetworkBootstrap's backfilled manager, and the order between
            // two RuntimeInitializeOnLoadMethods is undefined. See DisconnectHook.
            disconnects = new DisconnectHook(HandleDisconnect);
            disconnects.Poll();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            disconnects?.Detach();
        }

        private void Update() => disconnects?.Poll();

        private void HandleDisconnect(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            // Netcode's own flags, not Network.Server: that helper also asks IsListening, and this
            // callback is raised from inside the teardown that clears it — so a host would read as
            // a client at the exact moment it shut its own session down. On a host this same
            // callback fires for every REMOTE client that leaves as well, which must not send the
            // host anywhere at all.
            if (manager.IsServer || manager.IsHost) return;
            if (clientId != manager.LocalClientId) return;

            // Already on the way out. Shutting the session down raises this very callback, so
            // without the guard a client leaving through the pause menu would immediately be told
            // it had lost the host it deliberately let go of.
            if (SessionExit.InProgress) return;

            // Not ours while the lobby screen is up: it subscribes to LobbySession.Failed, which
            // carries the same reason, and it reports it on the page the player is already reading
            // rather than throwing them out to a menu they are standing in front of.
            //
            // Asked as "is that screen alive" rather than "are we in the menu scene", which is the
            // tempting shorthand — the lobby IS a page over MainMenu, so the two agree everywhere
            // except the window that matters most: a joiner whose host quits while the world is
            // still loading has handed the lobby screen off already, and is left staring at a
            // loading overlay for a session that ended. That one is ours.
            if (FindFirstObjectByType<LobbyUI>() != null) return;

            string notice = Describe(manager.DisconnectReason);
            Debug.Log($"[SessionWatchdog] The session ended: {notice}");

            // Read the reason NOW and act NEXT FRAME. Netcode calls NetworkManager.Shutdown itself
            // the instant this callback returns, and DisconnectReason does not survive it — while a
            // scene load started from inside a Netcode callback runs while NetworkSceneManager is
            // still unwinding the event that produced it, which is the same re-entrancy
            // NetworkGameManager.SpawnWhenReady yields a frame to avoid.
            StartCoroutine(ReturnToMenuNextFrame(notice));
        }

        private static IEnumerator ReturnToMenuNextFrame(string notice)
        {
            // Frame-based, so it still runs with the game clock stopped under an open pause menu.
            yield return null;

            // saveWorld: false — a client owns no world save. See SessionExit.ToMainMenu.
            SessionExit.ToMainMenu(saveWorld: false, notice: notice);
        }

        /// <summary>
        /// The disconnect reason, in a form fit to show a player.
        ///
        /// <para>
        /// Empty is the normal case and not an error: <c>DisconnectReason</c> only carries text when
        /// a server chose to refuse somebody, and a host that quits, crashes or drops off the
        /// network sends nothing at all. So the fallback has to say what actually happened rather
        /// than apologise for having nothing to say.
        /// </para>
        ///
        /// <para>
        /// Deliberately does not match on the reason string to decide whether this is recoverable.
        /// A host crash, a dropped connection and a Relay timeout strand the player identically —
        /// the same point <c>LobbySession.HandleClientDisconnect</c> already makes about the bug
        /// that matching caused there.
        /// </para>
        /// </summary>
        public static string Describe(string disconnectReason) =>
            string.IsNullOrWhiteSpace(disconnectReason)
                ? "Lost connection to the host."
                : disconnectReason.Trim();
    }
}
