// Closes the transport's UDP socket when play mode ends.
//
// Without this, every play session that hosted leaves its socket bound inside the still-running
// editor process, and the NEXT session cannot bind the same port: "Failed to bind UDP socket
// because the address is already in use", every time, until the editor is restarted. It reads like
// a stray copy of the game is running somewhere — it is actually the editor holding its own port.
//
// This shuts the session down at ExitingPlayMode, while the objects still exist and there is still
// a frame to do it in — rather than at EnteredEditMode, by which point the NetworkManager is gone
// and nothing holds a reference to the socket any more.
//
// Honest about its limits: an earlier investigation (2026-08-03) concluded the leak lives below the
// managed layer, in the native NetworkDriver/Baselib handle, and could not be closed from C# at
// all. This is the sanctioned shutdown at the last moment it can still run; it may not be enough.
// Calling UnityTransport.Shutdown() directly WOULD reach further, but Netcode warns against it on
// every exit ("all pending events will be lost"), and trading a silent leak for guaranteed console
// noise is not a good trade. If a port is still stuck after this, restart the editor — that is the
// only thing known to release it.
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
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // ExitingPlayMode, not EnteredEditMode: the objects still exist here, which is the
            // whole point. By EnteredEditMode the NetworkManager is gone and the socket it owned
            // is unreachable — no reference left to close it with, and no finalizer that will.
            if (change != PlayModeStateChange.ExitingPlayMode) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            try
            {
                if (manager.IsListening) manager.Shutdown(discardMessageQueue: true);
            }
            catch (System.Exception e)
            {
                // Never let a teardown problem stop play mode from ending — that would be a far
                // worse failure than the leak this prevents.
                Debug.LogWarning($"[Net] Transport teardown on exiting play mode failed: {e.Message}");
            }
        }
    }
}
