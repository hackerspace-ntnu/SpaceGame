using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Lobbies;
using SpaceGame.Core.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
    /// <summary>
    /// The one way out of a live session and back to the main menu.
    ///
    /// <para>
    /// It exists because there are two ways out and they were not the same teardown. The pause
    /// menu's "Main Menu" saved the world, cleared the staged session, shut Netcode down and loaded
    /// the menu — but never handed the lobby membership back, so a host's lobby stayed listed until
    /// its 30-second heartbeat lapsed and the membership survived as exactly the ghost
    /// <see cref="LobbyJoinRecovery"/> exists to clean up. A client whose
    /// host vanished did none of it at all: the only thing listening for that was the lobby screen,
    /// which the load into the world had already destroyed, so the world simply stopped updating
    /// around a player whose only way out was killing the process.
    /// </para>
    ///
    /// <para>
    /// One sequence now, with one difference: whether the world is written on the way out. See
    /// <see cref="ToMainMenu"/> for why that has to be told rather than worked out here.
    /// </para>
    /// </summary>
    public static class SessionExit
    {
        /// <summary>The scene every route out lands in.</summary>
        public const string MenuSceneName = "MainMenu";

        /// <summary>
        /// True from the moment a return is committed until the menu is up.
        ///
        /// Both a re-entrancy guard and a suppressor. Tearing a session down raises the very
        /// disconnect callback <see cref="SessionWatchdog"/> is listening for, so without this a
        /// client leaving deliberately through the pause menu would be answered by a second exit
        /// reporting the connection it had just closed itself as a lost host.
        /// </summary>
        public static bool InProgress { get; private set; }

        /// <summary>The line to show once the menu is up, or null when the player asked to leave.</summary>
        private static string pendingNotice;

        /// <summary>
        /// Puts the statics back to how a fresh process finds them.
        ///
        /// The project reloads the domain on entering play mode today, which does this for free —
        /// but that is one checkbox in Editor settings, and the failure if it is ever ticked is not
        /// a warning: a session left mid-exit latches <see cref="InProgress"/>, and from then on
        /// every route back to the menu, including the pause menu's button, silently does nothing.
        /// SubsystemRegistration runs before the first scene either way.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnMenuLoaded;
            InProgress = false;
            pendingNotice = null;
        }

        /// <summary>
        /// Ends the session and goes back to the menu.
        ///
        /// <para>
        /// <paramref name="saveWorld"/> is passed in rather than decided here, and it has to be.
        /// <c>SaveManager.SaveOnExit</c> guards itself with <c>Network.Server</c>, which needs a
        /// NetworkManager that is still listening — and on the path this matters for, the
        /// disconnect, Netcode has already dropped the connection before anyone gets here. A client
        /// would therefore read as "offline", pass that guard, and write a world record it does not
        /// own over the host's. A client owns no world save: the world is the host's, and this
        /// machine's copy of it is whatever streamed in before the connection died.
        /// </para>
        /// </summary>
        /// <param name="saveWorld">Write the world on the way out. Only the machine that owns it.</param>
        /// <param name="notice">Shown over the menu on arrival. Null for a deliberate exit, where
        /// the player already knows why they are looking at the menu.</param>
        public static void ToMainMenu(bool saveWorld, string notice = null)
        {
            if (InProgress) return;

            InProgress = true;
            pendingNotice = notice;

            // The world first, before the shutdown below tears down the stores a save reads from.
            // Leaving to the menu is otherwise indistinguishable from losing the session.
            if (saveWorld) SaveManager.SaveOnExit();

            // A staged world must not follow the player back to the menu, or the next thing they do
            // — joining someone else — starts with a save of their own waiting to be restored.
            WorldSession.Clear();

            // Best effort and deliberately not awaited; see LobbySession.LeaveInBackground. This is
            // what stops a host's lobby staying listed for its whole heartbeat window, and what
            // stops the next join racing this player's own stale membership.
            LobbySession.LeaveInBackground();

            // Whatever screen had taken the player is going away with the scene, so nothing is
            // handed back. The clock is still restored, because a scene loaded under a zero
            // timescale never finishes its own startup coroutines.
            GameplayMenuScope.Abandon();

            // The loading overlay waits for a world that is now never arriving — it deliberately
            // does not time out, because dropping a player into a half-built world is worse than a
            // long load. A joiner whose host quits mid-load is exactly the case its Dismiss escape
            // hatch is for, and leaving it up would put the menu behind a screen saying "Loading".
            LoadingScreenUI.Dismiss();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Still needed after LeaveInBackground, which only shuts the transport down when there
            // was a lobby to leave — singleplayer is a host of one that never touched the service.
            // Shutdown is deferred by Netcode to its next update, so this does not re-enter the
            // disconnect callback that may have brought us here.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            SceneManager.sceneLoaded += OnMenuLoaded;
            SceneManager.LoadScene(MenuSceneName, LoadSceneMode.Single);
        }

        /// <summary>
        /// Puts the reason on screen once there is a screen to put it on.
        ///
        /// The notice cannot be shown where the disconnect happened — that world is being unloaded
        /// — and it must not be shown before the menu exists, because the screen that shows it
        /// hides the menu's own canvases and puts them back on the way out. Subscribed only while a
        /// return is in flight, so nothing is left listening between sessions.
        /// </summary>
        private static void OnMenuLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MenuSceneName) return;

            SceneManager.sceneLoaded -= OnMenuLoaded;
            InProgress = false;

            string notice = pendingNotice;
            pendingNotice = null;

            if (!string.IsNullOrEmpty(notice)) SessionEndedScreen.Open(notice);
        }
    }
}
