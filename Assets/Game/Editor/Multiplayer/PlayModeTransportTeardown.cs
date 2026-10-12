// Closes the transport's UDP socket before the editor can orphan it.
//
// A leaked socket stays bound inside the still-running editor process, and the NEXT session cannot
// bind the same port: "Could not start a local session on port 7782", every time, until the editor
// is restarted. It reads like a stray copy of the game is running somewhere -- it is the editor
// holding its own port.
//
// Measured 2026-10-04: disposing a NetworkDriver DOES release its socket in this editor (bind 7799,
// Dispose, the port is gone). So a leak means the driver was never disposed, and the one path that
// skips it is a DOMAIN RELOAD DURING PLAY -- a script recompiling while a session is hosted, which
// the editor's default "Recompile And Continue Playing" does. The reload throws the managed side away
// without OnDestroy or OnApplicationQuit, and the native driver, with its socket, is orphaned.
//
// NetworkManager.Shutdown() cannot prevent that on its own: it only raises a flag, and a host then
// takes several network ticks to finish -- ticks a reload never runs. So before a reload during play
// this flags the shutdown (which also keeps Netcode from warning about a direct transport shutdown)
// and then shuts the transport down synchronously, which disposes the driver and frees the port.
//
// Leaving play mode needs nothing extra: NetworkManager.OnApplicationQuit runs ShutdownInternal
// synchronously, which shuts the transport down. The ExitingPlayMode hook below only flags the
// shutdown early so every peer sees a clean disconnect.
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    [InitializeOnLoad]
    internal static class PlayModeTransportTeardown
    {
        static PlayModeTransportTeardown()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            Teardown(closeTransportNow: false);
        }

        private static void OnBeforeAssemblyReload()
        {
            if (!EditorApplication.isPlaying) return;
            Teardown(closeTransportNow: true);
        }

        private static void Teardown(bool closeTransportNow)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;

            try
            {
                manager.Shutdown(discardMessageQueue: true);
                if (closeTransportNow) manager.NetworkConfig.NetworkTransport?.Shutdown();
            }
            catch (System.Exception e)
            {
                // Never let a teardown problem stop play mode from ending or a reload from running --
                // either would be a far worse failure than the port this frees. Logged as an error so
                // a teardown that stops working is seen, not discovered as a stuck port later.
                Debug.LogError($"[Net] Transport teardown failed; port {PortOf(manager)} may stay bound until the editor restarts: {e}");
            }
        }

        private static string PortOf(NetworkManager manager) =>
            manager.NetworkConfig.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport utp
                ? utp.ConnectionData.Port.ToString()
                : "?";
    }
}
