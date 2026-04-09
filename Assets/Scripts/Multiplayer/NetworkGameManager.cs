using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    
    [SerializeField] SpawnPoints spawnPoints;

    private PlayerColorSync playerColorSync;
    public override void OnNetworkSpawn()
    {
        playerColorSync = FindAnyObjectByType<PlayerColorSync>();

        // Only the server should handle spawning logic
        if (!IsServer) return;

        // Iterate through all currently connected clients
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayerForClient(client.ClientId, playerColorSync.getColor(client.ClientId));
        }
    }

    private void SpawnPlayerForClient(ulong clientId, Color color)
    {
        // Instantiate your prefab
        Transform spawnpoint = spawnPoints.GetSpawnPoint(clientId);
        Debug.Log("Color: " + color.ToString());
        GameObject playerObj = Instantiate(playerPrefab, spawnpoint.position, spawnpoint.rotation);

        // TODO: CHANGE PLAYER MESH / PART OF MESH TO GIVEN COLOR
        //playerObj.GetComponent<MeshRenderer>().material.color = color;


        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}
