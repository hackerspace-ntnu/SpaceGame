using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SingleplayerInitializer : NetworkBehaviour
{
        
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform spawnPoint;
        
        private void Start()
        {
            if (!NetworkManager.Singleton)
            {
                return;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                return;
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
            }

            NetworkManager.Singleton.StartHost();
        }
        
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
        }
    
}
