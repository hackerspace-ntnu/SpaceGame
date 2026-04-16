using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance;
    
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
        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
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
}
