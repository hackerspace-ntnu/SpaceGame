using Unity.Netcode;
using UnityEngine;

public class SingleplayerInitializer : NetworkBehaviour
{
        
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform spawnPoint;
        
        private void Start()
        {
            // Ensure NetworkManager exists
            if (!NetworkManager.Singleton)
            {
                Debug.LogError("No NetworkManager in scene!");
                return;
            }

            NetworkManager.Singleton.OnServerStarted += SpawnLocalPlayer;

            // Start host only if not already running.
            // Subscribe first so we cannot miss the server-start callback.
            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartHost();
            }
            else if (NetworkManager.Singleton.IsServer)
            {
                SpawnLocalPlayer();
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnServerStarted -= SpawnLocalPlayer;
        }
        
        private void SpawnLocalPlayer()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            
            var client = NetworkManager.Singleton.ConnectedClients[NetworkManager.Singleton.LocalClientId];
            if (client.PlayerObject == null)
            {
                GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError($"Assigned player prefab '{playerPrefab.name}' is missing a NetworkObject component.");
                    Destroy(player);
                    return;
                }

                networkObject.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
            }
        }
    
}
