using System;
using Unity.Netcode;
using UnityEngine;

public static class Network
{
    public static bool IsNetworked  =>
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.IsListening;

    public static bool Server =>
        IsNetworked  && NetworkManager.Singleton.IsServer;

    public static bool Client =>
        IsNetworked  && NetworkManager.Singleton.IsClient;
    
    
    /// <summary>
    /// Executes an action locally if server or offline, otherwise calls the RPC action.
    /// </summary>
    public static void Execute(Action local, Action client)
    {
        if (!IsNetworked || Server)
        {
            // Local execution: either offline or we are the server
            local?.Invoke();
        }
        else
        {
            // Client: send to server
            client?.Invoke();
        }
    }
}

