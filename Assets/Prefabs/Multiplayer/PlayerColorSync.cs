using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

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

    public override void OnNetworkSpawn()
    {
        /*ServerRpcParams serverRpcParams = new ServerRpcParams();
        AddclientColorChosenServerRpc(new Color(1,1,1), serverRpcParams);*/
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

    public Dictionary<ulong, Color> getAllColors()
    {
        Dictionary<ulong, Color> clientColors = new Dictionary<ulong, Color>();

        if (lobbySystem.getJoinedLobby().HostId != AuthenticationService.Instance.PlayerId)
        {
            throw new LobbyServiceException(LobbyExceptionReason.UnknownErrorCode,
            "Only host of lobby can get colors!");
        }

        foreach (Player p in lobbySystem.getJoinedLobby().Players)
        {
            string hex = p.Data["PlayerColor"].Value;
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color lobbyColor))
            {
                clientColors[ulong.Parse(p.AllocationId)] = lobbyColor;

                ServerRpcParams serverRpcParams = new ServerRpcParams();
                AddclientColorChosenServerRpc(chosenColor, serverRpcParams);
            }
        }

        return new Dictionary<ulong, Color>();
    }


    [ServerRpc(InvokePermission = RpcInvokePermission.Owner)]
    private void AddclientColorChosenServerRpc(Color color, ServerRpcParams serverRpcParams)
    {
        Debug.Log("Setting color! " + color);
        ulong callerId = serverRpcParams.Receive.SenderClientId;
        if (!clientColor.ContainsKey(callerId))
        {
            clientColor.Add(callerId, color);
        }
        clientColor[callerId] = color;
        Debug.Log("Client: " + callerId + " set color to: " + color);
    }
}
