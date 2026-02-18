using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    public override void OnNetworkSpawn()
    {
        // Only the server should handle spawning logic
        if (!IsServer) return;

        // Iterate through all currently connected clients
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayerForClient(client.ClientId);
        }
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        // Instantiate your prefab
        GameObject playerObj = Instantiate(playerPrefab);

        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}
