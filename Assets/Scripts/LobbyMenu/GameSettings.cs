using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameSettings : MonoBehaviour
{
    private Dictionary<ulong, Color> playerColors = new Dictionary<ulong, Color>();
    void Start()
    {
        DontDestroyOnLoad(gameObject);
    }

    public void setPlayerColor(ulong playerId, Color color)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if(playerColors.ContainsKey(playerId))
        {
            playerColors[playerId] = color;
        }
        else
        { 
            playerColors.Add(playerId, color);
        }
        Debug.Log("Set player color!");
    }

    public void removePlayer(ulong playerId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        playerColors.Remove(playerId);
    }

    public Color getPlayerColor(ulong playerId)
    {
        return playerColors[playerId];
    }
}
