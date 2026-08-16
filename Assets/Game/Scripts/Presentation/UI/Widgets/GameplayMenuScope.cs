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
        public static bool Enter(object owner)
        {
            if (owner == null) return false;

            PlayerController player = FindLocalPlayer();
            if (player == null) return false;

            if (owners.Add(owner) && owners.Count == 1)
                TakeControl(player);

            return true;
        }

        /// <summary>Releases <paramref name="owner"/>'s claim; the last one out restores control.</summary>
        public static void Exit(object owner)
        {
            if (owner == null || !owners.Remove(owner)) return;
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

            Freeze();

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
