using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-authoritative interior loader.
/// Lives in the persistent scene. Loads interior scenes additively beside the streamed exterior
/// (it does not unload exterior chunks — those keep streaming around the entrance, which makes
/// re-exit instant and keeps SceneTracked entities alive).
/// </summary>
public class InteriorManager : MonoBehaviour
{
    public static InteriorManager Instance { get; private set; }

    /// <summary>Where the player was last standing in the exterior, keyed by NetworkObjectId (or 0 in offline).</summary>
    private struct ReturnInfo
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Scene ExteriorScene;
    }

    private readonly Dictionary<ulong, ReturnInfo> returnInfoByPlayer = new();
    private readonly Dictionary<string, int> interiorRefCount = new();

    private Scene persistentScene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        persistentScene = gameObject.scene;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    //  Public API — call from interactables
    // ─────────────────────────────────────────────

    public void EnterInterior(GameObject player, InteriorScene def)
    {
        if (player == null || def == null || string.IsNullOrEmpty(def.SceneName))
        {
            Debug.LogWarning("[InteriorManager] EnterInterior called with invalid args.");
            return;
        }

        Network.Execute(
            local: () => ServerEnterInterior(player, def.SceneName, def.SpawnAnchorId),
            client: () =>
            {
                if (!player.TryGetComponent<NetworkObject>(out var netObj))
                {
                    Debug.LogError("[InteriorManager] Player missing NetworkObject — cannot route enter request.");
                    return;
                }
                EnterInteriorServerRpc(netObj, def.SceneName, def.SpawnAnchorId);
            });
    }

    public void ExitInterior(GameObject player)
    {
        if (player == null) return;

        Network.Execute(
            local: () => ServerExitInterior(player),
            client: () =>
            {
                if (!player.TryGetComponent<NetworkObject>(out var netObj))
                {
                    Debug.LogError("[InteriorManager] Player missing NetworkObject — cannot route exit request.");
                    return;
                }
                ExitInteriorServerRpc(netObj);
            });
    }

    // ─────────────────────────────────────────────
    //  RPC entry points
    // ─────────────────────────────────────────────

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void EnterInteriorServerRpc(NetworkObjectReference playerRef, string sceneName, string anchorId)
    {
        if (!playerRef.TryGet(out var netObj)) return;
        ServerEnterInterior(netObj.gameObject, sceneName, anchorId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExitInteriorServerRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out var netObj)) return;
        ServerExitInterior(netObj.gameObject);
    }

    // ─────────────────────────────────────────────
    //  Server-side implementation
    // ─────────────────────────────────────────────

    private void ServerEnterInterior(GameObject player, string sceneName, string anchorId)
    {
        ulong key = GetPlayerKey(player);

        // Remember where the player was so ExitInterior can put them back.
        returnInfoByPlayer[key] = new ReturnInfo
        {
            Position = player.transform.position,
            Rotation = player.transform.rotation,
            ExteriorScene = persistentScene.IsValid() ? persistentScene : player.scene,
        };

        var existing = SceneManager.GetSceneByName(sceneName);
        if (existing.IsValid() && existing.isLoaded)
        {
            interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
            PlacePlayerAtAnchor(player, existing, anchorId);
            return;
        }

        // Capture player by id — references can dangle while the load runs.
        var pendingPlayerId = key;
        var pendingPlayer = player;

        Action<Scene> onLoaded = scene =>
        {
            interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
            if (pendingPlayer == null) return;
            PlacePlayerAtAnchor(pendingPlayer, scene, anchorId);
        };

        LoadInteriorAdditive(sceneName, onLoaded);
    }

    private void ServerExitInterior(GameObject player)
    {
        ulong key = GetPlayerKey(player);
        if (!returnInfoByPlayer.TryGetValue(key, out var info))
        {
            Debug.LogWarning("[InteriorManager] No return info for player — cannot exit.");
            return;
        }

        Scene currentInterior = player.scene;
        string interiorName = currentInterior.name;

        // Move player back to exterior first so the interior can safely unload.
        if (info.ExteriorScene.IsValid() && info.ExteriorScene.isLoaded)
        {
            if (player.transform.parent != null) player.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(player, info.ExteriorScene);
        }
        TeleportPlayer(player, info.Position, info.Rotation);
        returnInfoByPlayer.Remove(key);

        if (string.IsNullOrEmpty(interiorName) || currentInterior == info.ExteriorScene)
            return;

        int remaining = interiorRefCount.GetValueOrDefault(interiorName) - 1;
        if (remaining <= 0)
        {
            interiorRefCount.Remove(interiorName);
            UnloadInterior(currentInterior);
        }
        else
        {
            interiorRefCount[interiorName] = remaining;
        }
    }

    private void PlacePlayerAtAnchor(GameObject player, Scene scene, string anchorId)
    {
        var anchor = InteriorAnchor.Find(scene, anchorId);
        Vector3 position = anchor != null ? anchor.transform.position : Vector3.zero;
        Quaternion rotation = anchor != null ? anchor.transform.rotation : Quaternion.identity;

        if (anchor == null)
            Debug.LogWarning($"[InteriorManager] No InteriorAnchor '{anchorId}' in {scene.name} — dropping player at origin.");

        if (player.transform.parent != null) player.transform.SetParent(null);
        SceneManager.MoveGameObjectToScene(player, scene);
        TeleportPlayer(player, position, rotation);
    }

    private static void TeleportPlayer(GameObject player, Vector3 position, Quaternion rotation)
    {
        if (player.TryGetComponent<CharacterController>(out var cc))
        {
            cc.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            cc.enabled = true;
            return;
        }

        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        player.transform.SetPositionAndRotation(position, rotation);
    }

    private static ulong GetPlayerKey(GameObject player)
    {
        if (player.TryGetComponent<NetworkObject>(out var netObj))
            return netObj.NetworkObjectId;
        return 0;
    }

    // ─────────────────────────────────────────────
    //  Scene load / unload (Netcode-aware)
    // ─────────────────────────────────────────────

    private void LoadInteriorAdditive(string sceneName, Action<Scene> onLoaded)
    {
        if (Network.IsNetworked)
        {
            void Handler(SceneEvent evt)
            {
                if (evt.SceneEventType != SceneEventType.LoadEventCompleted) return;
                if (evt.SceneName != sceneName) return;
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= Handler;
                onLoaded?.Invoke(SceneManager.GetSceneByName(sceneName));
            }
            NetworkManager.Singleton.SceneManager.OnSceneEvent += Handler;

            var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= Handler;
                Debug.LogError($"[InteriorManager] Failed to load interior {sceneName}: {status}");
            }
        }
        else
        {
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[InteriorManager] Failed to load interior {sceneName} (offline). Is it in Build Settings?");
                return;
            }
            op.completed += _ => onLoaded?.Invoke(SceneManager.GetSceneByName(sceneName));
        }
    }

    private void UnloadInterior(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        if (Network.IsNetworked)
            NetworkManager.Singleton.SceneManager.UnloadScene(scene);
        else
            SceneManager.UnloadSceneAsync(scene);
    }
}
