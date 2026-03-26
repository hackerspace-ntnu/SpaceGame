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

            // Start host only if not already running
            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartHost();
            }
            NetworkManager.Singleton.OnServerStarted += SpawnLocalPlayer;
        }
        
        private void SpawnLocalPlayer()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            
            var client = NetworkManager.Singleton.ConnectedClients[NetworkManager.Singleton.LocalClientId];
            if (client.PlayerObject == null)
            {
                GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
                player.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
            }
        }
    
}
