using System;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;


public class SpawnManager : NetworkBehaviour
{
    public static SpawnManager Instance;

    private SpawnPoint[] spawnPoints;
    
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject networkPlayerPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("No SpawnPoint found in scene!");
        }
    }

    public Vector3 GetSpawnPoint()
    {
        return spawnPoints[0].GetSpawnPoint();
    }
    
    private void SpawnPlayer()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("Cannot spawn player: No SpawnPoint found!");
            return;
        }
        
        Vector3 spawnPosition = GetSpawnPoint();
        Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
    }

    public void SpawnNetworkPlayer()
    {
        RequestSpawnServerRpc(NetworkManager.Singleton.LocalClientId);
    }
    
    
    public void RespawnPlayer(GameObject player)
    {
        if (Network.IsNetworked)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            RequestSpawnServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            Destroy(player);
            SpawnPlayer();
        }
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSpawnServerRpc(ulong clientId)
    {
        var client = NetworkManager.Singleton.ConnectedClients[clientId];
        if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
        {
            client.PlayerObject.Despawn();
        }
        SpawnPlayerForClient(clientId);
    }
    
    public void SpawnPlayerForClient(ulong clientId)
    {

        Vector3 spawnPosition = GetSpawnPoint();
        GameObject playerObj = Instantiate(networkPlayerPrefab, spawnPosition, Quaternion.identity);

        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}
