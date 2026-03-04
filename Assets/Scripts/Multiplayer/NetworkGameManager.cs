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
        DontDestroyOnLoad(gameObject);
    }
    public override void OnNetworkSpawn()
    {
        Debug.Log("OnNetworkSpawn");
        // Only the server should handle spawning logic
        if (!IsServer) return;
        
        Debug.Log("Spawning players");
        // Iterate through all currently connected clients
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
}
