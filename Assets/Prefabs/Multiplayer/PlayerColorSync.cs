using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using UnityEngine;

public class PlayerColorSync : NetworkBehaviour
{
    private Color chosenColor = new Color();

    private Dictionary<ulong, Color> clientColor = new Dictionary<ulong, Color>();
    
    private LobbySystem lobbySystem;

    void Awake()
    {
        
        DontDestroyOnLoad(gameObject);
        lobbySystem = FindAnyObjectByType<LobbySystem>();
        
    }

    public void updateColorLocal()
    {
         ApplyColorFromLobby();
    }

    public Color getColor(ulong clientId)
    {
        return clientColor[clientId];
    }

    private async void ApplyColorFromLobby()
    {
        var lobby = await LobbyService.Instance.GetLobbyAsync(lobbySystem.getJoinedLobby().Id);

        var player = lobby.Players.Find(p => p.Id == AuthenticationService.Instance.PlayerId);

        if (player.Data.ContainsKey("PlayerColor"))
        {
            string hex = player.Data["PlayerColor"].Value;
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color lobbyColor))
            {
                chosenColor = lobbyColor;
                ServerRpcParams serverRpcParams = new ServerRpcParams();
                AddclientColorChosenServerRpc(chosenColor, serverRpcParams);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddclientColorChosenServerRpc(Color color, ServerRpcParams serverRpcParams)
    {
        ulong callerId = serverRpcParams.Receive.SenderClientId;
        if(!clientColor.ContainsKey(callerId))
        {
            clientColor.Add(callerId, color);
        }
        clientColor[callerId] = color;
        Debug.Log("Client: " + callerId + " set color to: " + color);
    }
}
