using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    [SerializeField] SpawnPoints spawnPoints;

    [Header("World Streaming (optional)")]
    [SerializeField] private WorldStreamer worldStreamer;

    public override void OnNetworkSpawn()
    {
        // Only the server should handle spawning logic
        if (!IsServer) return;

        if (worldStreamer == null)
        {
            SpawnAllPlayers();
            return;
        }

        worldStreamer.PreloadChunksAroundPositions(GetSpawnPositionsForConnectedClients(), SpawnAllPlayers);
    }

    private void SpawnAllPlayers()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayerForClient(client.ClientId);
        }
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        // Instantiate your prefab
        Transform spawnpoint = spawnPoints.GetSpawnPoint(clientId);
        GameObject playerObj = Instantiate(playerPrefab, spawnpoint.position, spawnpoint.rotation);

        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }

    private IEnumerable<Vector3> GetSpawnPositionsForConnectedClients()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            yield return spawnPoints.GetSpawnPoint(client.ClientId).position;
        }
    }
}
