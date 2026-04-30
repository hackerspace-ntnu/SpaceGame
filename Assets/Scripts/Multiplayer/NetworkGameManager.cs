using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance;
    [SerializeField] private GameObject playerPrefab;
    private GameSettings gameSettings;
    
    [Header("World Streaming (optional)")]
    [SerializeField] private WorldStreamer worldStreamer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        gameSettings = FindFirstObjectByType<GameSettings>();
        if (!IsServer) return;
        
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        OnClientConnected(OwnerClientId);
    }

    private void OnClientConnected(ulong clientId)
    {
        StartCoroutine(SpawnWhenReady(clientId));
    }
    
    private IEnumerator SpawnWhenReady(ulong clientId)
    {
        
        if (worldStreamer)
        {
            var pos = SpawnManager.Instance.GetSpawnPoint();
            yield return WaitForWorldReady(new[] { pos });
        }
        
        SpawnManager.Instance.SpawnPlayerForClient(clientId);
    }
    
    IEnumerator WaitForWorldReady(IEnumerable<Vector3> positions)
    {
        bool done = false;

        worldStreamer.PreloadChunksAroundPositions(positions, () =>
        {
            done = true;
        });

        while (!done)
            yield return null;
    }
    /*
        private void SpawnAllPlayers()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            Color playerColor = gameSettings.getPlayerColor(client.ClientId);
            SpawnPlayerForClient(client.ClientId, playerColor);
        }
    }

    private void SpawnPlayerForClient(ulong clientId, Color color)
    {
        Transform spawnpoint = spawnPoints.GetSpawnPoint(clientId);
        GameObject playerObj = Instantiate(playerPrefab, spawnpoint.position, spawnpoint.rotation);

        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        GameObject scarfMesh = playerObj.GetComponent<PlayerController>().getScarfMesh();

        SkinnedMeshRenderer skinnedMeshRenderer = scarfMesh.GetComponent<SkinnedMeshRenderer>();

        skinnedMeshRenderer.GetPropertyBlock(propBlock);

        propBlock.SetColor("_BaseColor", color);

        skinnedMeshRenderer.SetPropertyBlock(propBlock);
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }*/
}
