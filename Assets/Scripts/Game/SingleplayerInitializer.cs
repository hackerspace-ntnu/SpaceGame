using Unity.Netcode;
using UnityEngine;

public class SingleplayerInitializer : NetworkBehaviour
{
        
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform spawnPoint;
        
        private void Start()
        {
            if (NetworkManager.Singleton)
            {
                NetworkManager.Singleton.StartHost();
            }
        }
        
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
        }
    
}
