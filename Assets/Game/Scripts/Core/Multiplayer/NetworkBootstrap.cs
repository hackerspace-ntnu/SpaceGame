using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Guarantees a <see cref="NetworkManager"/> exists before anything asks for one.
    ///
    /// The only NetworkManager in the project lives in the Bootstrap scene, and it survives later
    /// scene loads because NetworkManager calls DontDestroyOnLoad on itself. In a build that is
    /// enough — Bootstrap is scene 0, so it always runs. In the editor it is not: pressing Play with
    /// LobbyMenu open (the obvious way to work on the lobby) skips Bootstrap entirely, leaving
    /// NetworkManager.Singleton null. The lobby's Start() then threw a NullReferenceException on its
    /// first line, before it could wire up a single button — so every button on the screen silently
    /// did nothing, and nothing in the console pointed at the lobby.
    ///
    /// AfterSceneLoad is deliberate: it runs after every Awake/OnEnable in the opening scene and
    /// before any Start, so the manager is in place by the time lobby code looks for it, without
    /// racing an additive scene load.
    /// </summary>
    public static class NetworkBootstrap
    {
        public const string NetworkManagerPrefabPath = "Assets/Game/Prefabs/Systems/NetworkManager.prefab";

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureNetworkManagerInEditor()
        {
            if (NetworkManager.Singleton != null) return;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[NetworkBootstrap] No NetworkManager in this scene and none at {NetworkManagerPrefabPath}. " +
                    "Multiplayer cannot start. Play from the Bootstrap scene instead.");
                return;
            }

            var instance = Object.Instantiate(prefab);

            // Without this the object is "NetworkManager(Clone)", which reads like a duplicate of
            // the real one in every log line and hierarchy it shows up in.
            instance.name = prefab.name;

            Debug.LogWarning(
                "[NetworkBootstrap] Spawned a NetworkManager because this scene was entered directly. " +
                "This is an editor convenience — a build always starts at Bootstrap. Note that the " +
                "other Bootstrap systems (item registry, audio) are still absent, so anything " +
                "depending on registered items will misbehave until you play from Bootstrap.");
        }
#endif

        /// <summary>
        /// Strips NetworkObjects that replicate nothing out of the scene before Netcode starts.
        ///
        /// A NetworkObject with no NetworkBehaviour beside it has no state to synchronise, but
        /// Netcode does not know that: on server start it sweeps every loaded scene and spawns each
        /// in-scene NetworkObject it finds, then expects joining clients to hold a matching copy.
        /// A menu scene loaded by plain SceneManager — which is how LobbyMenu loads — is not part
        /// of that agreement, so the host spawns an object the client cannot account for, and the
        /// mismatch surfaces during client synchronisation rather than as anything naming the
        /// scene or object responsible.
        ///
        /// LobbySystem left one behind by deriving from NetworkBehaviour purely to reach
        /// NetworkManager.Singleton, which needed no networking at all. Cleaning up here rather
        /// than by hand in the scene means the same mistake anywhere else also cannot bite.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RemoveOrphanSceneNetworkObjects()
        {
            // Only ever before a session is live. If Netcode is already listening these objects may
            // legitimately be spawned, and tearing one out from under it is worse than leaving it.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;

            // FindObjectsByType returns scene instances only. Resources.FindObjectsOfTypeAll would
            // also return prefab assets loaded in memory, and destroying a component on one of
            // those edits the asset on disk.
            NetworkObject[] sceneObjects =
                Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (NetworkObject netObj in sceneObjects)
            {
                if (netObj == null || netObj.IsSpawned) continue;
                if (netObj.GetComponents<NetworkBehaviour>().Length > 0) continue;

                Debug.Log($"[NetworkBootstrap] Removing an inert NetworkObject from " +
                          $"'{netObj.gameObject.name}' in scene '{netObj.gameObject.scene.name}'. " +
                          "It has no NetworkBehaviour, so it replicates nothing, but Netcode would " +
                          "still try to spawn it and expect clients to match it.");

                Object.Destroy(netObj);
            }
        }

        /// <summary>
        /// Logs how many prefabs the running NetworkManager can actually spawn.
        ///
        /// Worth a line in the log on every host start because a short list is invisible on the
        /// host and fatal on clients: the server instantiates prefabs directly and never consults
        /// the list, so a truncated list produces a host that plays perfectly and clients that
        /// cannot spawn a player. The project shipped in exactly that state.
        /// See the editor-side Tools/SpaceGame/Multiplayer/Sync Network Prefabs.
        /// </summary>
        public static void LogRegisteredPrefabCount()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            int count = 0;
            foreach (NetworkPrefabsList list in networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
                if (list != null) count += list.PrefabList.Count;

            if (count <= 1)
            {
                Debug.LogError(
                    $"[NetworkBootstrap] Only {count} network prefab(s) registered. Clients will not be " +
                    "able to spawn players or items. Run Tools/SpaceGame/Multiplayer/Sync Network Prefabs.");
            }
            else
            {
                Debug.Log($"[NetworkBootstrap] {count} network prefabs registered.");
            }
        }
    }
}
