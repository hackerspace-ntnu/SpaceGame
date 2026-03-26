using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance;
    [SerializeField] private GameObject playerPrefab;

    [SerializeField] SpawnPoints spawnPoints;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

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

        // WorldStreamer may not have had its OnNetworkSpawn called yet,
        // so wait until it's ready before requesting chunk preloads.
        if (worldStreamer.IsReady)
        {
            PreloadAndSpawn();
        }
        else
        {
            StartCoroutine(WaitForStreamerThenSpawn());
        }
    }

    private IEnumerator WaitForStreamerThenSpawn()
    {
        while (!worldStreamer.IsReady)
            yield return null;

        PreloadAndSpawn();
    }

    private void PreloadAndSpawn()
    {
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

    public void Respawn()
    {
        var clientId = NetworkManager.Singleton.LocalClientId;
        RequestRespawnServerRpc(clientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRespawnServerRpc(ulong clientId)
    {
        var client = NetworkManager.Singleton.ConnectedClients[clientId];
        if (client.PlayerObject != null)
        {
            client.PlayerObject.Despawn();
        }
        SpawnPlayerForClient(clientId);
    }

    private IEnumerable<Vector3> GetSpawnPositionsForConnectedClients()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            yield return spawnPoints.GetSpawnPoint(client.ClientId).position;
        }
    }
}
