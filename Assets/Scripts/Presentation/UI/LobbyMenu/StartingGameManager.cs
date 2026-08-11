using UnityEngine;
using UnityEngine.Networking;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;

public class StartingGameManager : NetworkBehaviour {

    public static async void StartGame(Lobby lobby)
    {
        // 1. Safety Check: Only the Host should trigger the start
        // In Lobby Service, the Host is usually the one who created it.
        if (lobby.HostId != AuthenticationService.Instance.PlayerId)
        {
            Debug.LogWarning("Only the host can start the game!");
            return;
        }

        try
        {
            // 2. Lock the Lobby so no one else can join while we are loading
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                IsLocked = true
            };
            await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, options);

            // 3. Tell Netcode to switch scenes for EVERYONE
            // Make sure "Enable Scene Management" is checked in your NetworkManager!
            NetworkManager networkManager = NetworkManager.Singleton;
            Debug.Log(networkManager.SceneManager);  
            networkManager.SceneManager.LoadScene("Tommy test scene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to lock lobby: {e.Message}");
        }
    }
}
