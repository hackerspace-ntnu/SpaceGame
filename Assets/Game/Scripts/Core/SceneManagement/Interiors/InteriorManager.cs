using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;

namespace SpaceGame.Core
{
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
        private class ReturnInfo
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Scene ExteriorScene;
            /// <summary>
            /// Pin GameObject registered with WorldStreamer so the exterior chunks under the
            /// return position stay loaded for the duration of the interior visit. Without this,
            /// WorldStreamer chunks the world around the interior anchor and unloads the player's
            /// origin — they then fall through the ground on exit.
            /// </summary>
            public Transform ReturnPin;
        }

        private readonly Dictionary<ulong, ReturnInfo> returnInfoByPlayer = new();
        private readonly Dictionary<string, int> interiorRefCount = new();

        private Scene persistentScene;
        private PersistentSceneVisibility persistentVisibility;
        private WorldStreamer worldStreamer;

        [Header("Exit")]
        [Tooltip("Hard cap on how long ServerExitInterior will wait for the chunk under the return position to load before teleporting anyway.")]
        [SerializeField] private float exitChunkLoadTimeoutSeconds = 8f;
        [Tooltip("Cap on how far the player can be moved upward by the ground-clamp. Keeps a stuck return position from launching them into the sky.")]
        [SerializeField] private float groundClampMaxLift = 50f;
        [Tooltip("After exiting an interior, how long (seconds) entrance triggers refuse to fire for that player. " +
                 "Exiting drops the player back where they entered — right inside the entrance volume — so without " +
                 "this lockout a walk-in entrance re-fires instantly and yo-yos them straight back in.")]
        [SerializeField] private float postExitEntranceLockout = 2f;

        /// <summary>Per-player real-time stamp until which entrance triggers should treat re-entry as locked out.</summary>
        private readonly Dictionary<ulong, float> entranceLockoutUntil = new();

        /// <summary>
        /// True if <paramref name="player"/> just exited an interior and the post-exit lockout window
        /// has not elapsed. Entrance triggers (InteriorEntrance, volume triggers) should not fire while
        /// this is true — it stops the player yo-yoing straight back into the interior they just left.
        /// </summary>
        public bool IsEntranceLockedOut(GameObject player)
        {
            if (player == null) return false;
            return entranceLockoutUntil.TryGetValue(GetPlayerKey(player), out float until)
                   && Time.unscaledTime < until;
        }

        /// <summary>
        /// True if <paramref name="player"/> is currently inside an interior (has been moved out of the
        /// exterior by EnterInterior and not yet returned by ExitInterior). A player has a ReturnInfo
        /// entry for exactly the duration of an interior visit, so this is the authoritative test.
        ///
        /// Triggers in the persistent/exterior scene use this instead of a raw scene-equality check:
        /// world streaming legitimately migrates the player between exterior chunk sub-scenes, so
        /// "player.scene != trigger.scene" is true in normal play and must NOT block triggering.
        /// The thing a trigger actually needs to exclude is a player who is off in an interior.
        /// </summary>
        public bool IsInsideInterior(GameObject player)
        {
            if (player == null) return false;
            return returnInfoByPlayer.ContainsKey(GetPlayerKey(player));
        }

        private int TotalInteriorOccupancy
        {
            get
            {
                int sum = 0;
                foreach (var n in interiorRefCount.Values) sum += n;
                return sum;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            persistentScene = gameObject.scene;
            persistentVisibility = new PersistentSceneVisibility(persistentScene);
        }

        private void OnDestroy()
        {
            // Drop any outstanding return pins so we don't leak transforms that linger in
            // WorldStreamer's trackedTransforms list across scene reloads.
            foreach (var kvp in returnInfoByPlayer)
            {
                var info = kvp.Value;
                if (info?.ReturnPin == null) continue;
                if (worldStreamer != null)
                    worldStreamer.UnregisterTrackedTransform(info.ReturnPin);
                Destroy(info.ReturnPin.gameObject);
            }
            returnInfoByPlayer.Clear();
            entranceLockoutUntil.Clear();

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

            // Re-entry without a prior exit (interior-to-interior, or duplicate call) — drop the
            // old pin so we don't leak it and don't keep chunks loaded around a stale position.
            if (returnInfoByPlayer.TryGetValue(key, out var stale))
                CleanupReturnInfo(key, stale);

            Vector3 returnPos = player.transform.position;
            Quaternion returnRot = player.transform.rotation;
            Scene exteriorScene = persistentScene.IsValid() ? persistentScene : player.scene;

            // Drop a pin at the return position and register it with WorldStreamer. While the
            // player is inside, WorldStreamer sees them at the interior anchor and would unload
            // the exterior chunks under their origin — the pin keeps those chunks alive.
            Transform pin = CreateReturnPin(returnPos);

            // Remember where the player was so ExitInterior can put them back.
            returnInfoByPlayer[key] = new ReturnInfo
            {
                Position = returnPos,
                Rotation = returnRot,
                ExteriorScene = exteriorScene,
                ReturnPin = pin,
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

            // Drive the exit on a coroutine so we can wait for the exterior chunks under the
            // return position to be fully loaded before teleporting the player. Otherwise the
            // teleport lands on an unloaded chunk and the player falls through the ground.
            StartCoroutine(ExitInteriorRoutine(player, key, info));
        }

        private IEnumerator ExitInteriorRoutine(GameObject player, ulong key, ReturnInfo info)
        {
            Scene currentInterior = player.scene;
            string interiorName = currentInterior.name;

            // Make sure the chunk under the return position is loaded before the move.
            // The return pin should already be keeping it alive, but if WorldStreamer was
            // mid-unload when EnterInterior fired, the chunk can still be in Loading state.
            EnsureWorldStreamer();
            if (worldStreamer != null)
            {
                worldStreamer.PreloadChunksAroundPosition(info.Position);

                float deadline = Time.unscaledTime + Mathf.Max(0.5f, exitChunkLoadTimeoutSeconds);
                while (!worldStreamer.IsChunkLoadedAt(info.Position))
                {
                    if (player == null)
                    {
                        CleanupReturnInfo(key, info);
                        yield break;
                    }
                    if (Time.unscaledTime >= deadline)
                    {
                        Debug.LogWarning(
                            $"[InteriorManager] Timed out waiting for exterior chunk under {info.Position} to load. " +
                            "Teleporting anyway — player may briefly clip if terrain isn't ready.");
                        break;
                    }
                    yield return null;
                }
            }

            if (player == null)
            {
                CleanupReturnInfo(key, info);
                yield break;
            }

            // Move player back to exterior so the interior can safely unload.
            if (info.ExteriorScene.IsValid() && info.ExteriorScene.isLoaded)
            {
                if (player.transform.parent != null) player.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(player, info.ExteriorScene);
                SceneManager.SetActiveScene(info.ExteriorScene);
            }
            TeleportPlayer(player, info.Position, info.Rotation);

            // One frame to let Physics.SyncTransforms-ish settling happen, then ground-clamp
            // so a stale Y (e.g., we entered the interior mid-jump or the terrain changed) can't
            // leave the player floating or clipped into the ground.
            yield return null;
            GroundClampPlayer(player, info.Position);

            // The player is now standing exactly where they entered — i.e. inside the entrance
            // trigger volume. Arm a lockout so entrance triggers ignore them until they've had a
            // chance to walk clear; otherwise a walk-in entrance fires this same frame and sends
            // them straight back in.
            entranceLockoutUntil[key] = Time.unscaledTime + Mathf.Max(0f, postExitEntranceLockout);

            CleanupReturnInfo(key, info);

            if (string.IsNullOrEmpty(interiorName) || currentInterior == info.ExteriorScene)
            {
                if (TotalInteriorOccupancy == 0) persistentVisibility?.Restore();
                yield break;
            }

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

            if (TotalInteriorOccupancy == 0) persistentVisibility?.Restore();
        }

        private void CleanupReturnInfo(ulong key, ReturnInfo info)
        {
            if (info != null && info.ReturnPin != null)
            {
                if (worldStreamer != null)
                    worldStreamer.UnregisterTrackedTransform(info.ReturnPin);
                Destroy(info.ReturnPin.gameObject);
                info.ReturnPin = null;
            }
            returnInfoByPlayer.Remove(key);
        }

        private void GroundClampPlayer(GameObject player, Vector3 returnPos)
        {
            if (player == null) return;
            if (worldStreamer == null) return;

            if (!worldStreamer.TrySampleGroundHeight(returnPos, out float groundY))
                return;

            var pos = player.transform.position;
            // Only lift up to ground level if the saved Y is below it (we were clipped under terrain
            // due to a respawn-chunk mismatch). Don't drop the player onto the ground if they're
            // legitimately a bit above — gravity will resolve that.
            if (pos.y < groundY)
            {
                float lifted = Mathf.Min(groundY + 0.05f, pos.y + groundClampMaxLift);
                TeleportPlayer(player, new Vector3(pos.x, lifted, pos.z), player.transform.rotation);
            }
        }

        private Transform CreateReturnPin(Vector3 worldPos)
        {
            EnsureWorldStreamer();
            if (worldStreamer == null) return null;

            var go = new GameObject("InteriorReturnPin");
            if (persistentScene.IsValid() && persistentScene.isLoaded)
                SceneManager.MoveGameObjectToScene(go, persistentScene);
            go.transform.position = worldPos;
            worldStreamer.RegisterTrackedTransform(go.transform);
            return go.transform;
        }

        private void EnsureWorldStreamer()
        {
            if (worldStreamer != null) return;
            worldStreamer = FindFirstObjectByType<WorldStreamer>();
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

            // Activate the interior scene so its RenderSettings (ambient, fog, skybox) take effect.
            // Without this, renderers in the interior sample the persistent scene's bright skybox ambient.
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.SetActiveScene(scene);

            persistentVisibility?.Suspend();
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
}
