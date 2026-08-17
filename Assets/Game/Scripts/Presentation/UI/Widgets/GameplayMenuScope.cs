using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Takes control of the game while a full-screen menu is up, and hands back exactly what it
    /// took when the last one closes.
    /// <para>
    /// Shared, and reference-counted, because more than one of these screens can be open at a time
    /// — the dev artifact browser can be opened from the pause menu, and both want the cursor free
    /// and the player still. If each did its own handover the second one to close would re-lock the
    /// cursor and hand movement back while the first was still on screen, and whichever restored
    /// <see cref="Time.timeScale"/> last would win.
    /// </para>
    /// </summary>
    public static class GameplayMenuScope
    {
        private static readonly HashSet<object> owners = new();

        /// <summary>
        /// The subset of <see cref="owners"/> that wants the clock stopped.
        /// <para>
        /// Tracked separately because stopping time is not what every screen holding this scope is
        /// for. A full-screen menu wants the world to wait; the chat box does not — you type into
        /// it while the game runs on around you, which is the whole point of chatting mid-game.
        /// Freezing is therefore decided by whether <em>anyone</em> currently wants it, not by
        /// whoever happened to enter first.
        /// </para>
        /// </summary>
        private static readonly HashSet<object> freezers = new();

        private static PlayerController releasedPlayer;
        private static SpectatorCamera releasedSpectator;
        private static bool frozeTime;
        private static float previousTimeScale = 1f;

        public static bool IsActive => owners.Count > 0;

        /// <summary>True while the game clock is stopped, which only happens in a solo session.</summary>
        public static bool TimeFrozen => frozeTime;

        /// <summary>
        /// Claims control for <paramref name="owner"/>. Returns false when there is nothing to
        /// pause — no local player means the main menu or the lobby, which run their own screens.
        /// </summary>
        public static bool Enter(object owner) => Enter(owner, freezeTime: true);

        /// <summary>
        /// As <see cref="Enter(object)"/>, but <paramref name="freezeTime"/> false takes input and
        /// the cursor without stopping the clock — for a screen you are meant to use while the game
        /// carries on, like the chat box.
        /// </summary>
        public static bool Enter(object owner, bool freezeTime)
        {
            if (owner == null) return false;

            PlayerController player = FindLocalPlayer();
            if (player == null) return false;

            if (owners.Add(owner) && owners.Count == 1)
                TakeControl(player);

            if (freezeTime) freezers.Add(owner);
            SyncFreeze();

            return true;
        }

        /// <summary>Releases <paramref name="owner"/>'s claim; the last one out restores control.</summary>
        public static void Exit(object owner)
        {
            if (owner == null || !owners.Remove(owner)) return;

            // Before the early return: a chat box closing under an open pause menu must not leave
            // the clock stopped, and a pause menu closing over an open chat box must restart it.
            freezers.Remove(owner);
            SyncFreeze();

            if (owners.Count > 0) return;

            GiveControlBack();
        }

        /// <summary>
        /// Drops every claim without handing control back, for when the thing being paused has
        /// gone — a scene load, a disconnect. The clock is still restored, because a scene that
        /// starts under a zero timescale never finishes its own startup coroutines.
        /// </summary>
        public static void Abandon()
        {
            owners.Clear();
            freezers.Clear();
            releasedPlayer = null;
            releasedSpectator = null;
            Thaw();
        }

        // ------------------------------------------------------------------ internals

        private static void TakeControl(PlayerController player)
        {
            // PlayerLook re-locks the cursor every LateUpdate while it is enabled, so a cursor
            // merely unlocked here would be recaptured before the first click landed.
            // EnterCutsceneMode is the project's existing primitive for this: input, look and
            // movement stop while the camera keeps rendering, which is why gameplay is still
            // visible behind the panel.
            //
            // Skipped when a cutscene already holds the player, or resuming would hand back
            // control the cutscene never gave up.
            if (!player.InCutsceneMode)
            {
                player.EnterCutsceneMode();
                releasedPlayer = player;
            }

            var spectator = Object.FindFirstObjectByType<SpectatorCamera>();
            if (spectator != null && spectator.enabled)
            {
                spectator.enabled = false;
                releasedSpectator = spectator;
            }

            // Freezing is not decided here any more — see SyncFreeze, which the caller runs once
            // the owner sets are up to date.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static void GiveControlBack()
        {
            Thaw();

            // Whether the player died while this menu was up decides who owns the cursor next, so
            // read it before ExitCutsceneMode and before the reference is cleared.
            bool playerIsDead = releasedPlayer != null && releasedPlayer.IsDead;

            if (releasedPlayer != null)
            {
                releasedPlayer.ExitCutsceneMode();
                releasedPlayer = null;
            }

            if (releasedSpectator != null)
            {
                releasedSpectator.enabled = true;
                releasedSpectator = null;
            }

            // Closing the menu over a death screen must leave the cursor free — re-locking it here
            // is what makes the respawn button unclickable after pausing during death.
            if (playerIsDead) return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Applies the current freeze claims. Idempotent, so it is safe to call on every change.</summary>
        private static void SyncFreeze()
        {
            if (freezers.Count > 0) Freeze();
            else Thaw();
        }

        /// <summary>
        /// The clock only stops when stopping it affects nobody else. In a session with other
        /// players the simulation is authoritative on the host and shared by everyone, so one
        /// player opening a menu must not halt it.
        /// </summary>
        private static void Freeze()
        {
            if (frozeTime || !IsSoloSession()) return;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            frozeTime = true;
        }

        private static void Thaw()
        {
            if (!frozeTime) return;

            Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;
            frozeTime = false;
        }

        public static bool IsSoloSession()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return true;
            if (!manager.IsServer) return false;

            return manager.ConnectedClientsIds.Count <= 1;
        }

        /// <summary>
        /// The player this peer is driving.
        /// <para>
        /// Every peer holds a <see cref="PlayerController"/> for every player in the session, so a
        /// blind scene search would find someone else's on a client. Only the unnetworked case —
        /// a test scene with a player dropped straight in — falls back to searching.
        /// </para>
        /// </summary>
        public static PlayerController FindLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (manager == null || !manager.IsListening)
                return Object.FindFirstObjectByType<PlayerController>();

            NetworkObject local = manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;

            return local != null ? local.GetComponent<PlayerController>() : null;
        }
    }
}
