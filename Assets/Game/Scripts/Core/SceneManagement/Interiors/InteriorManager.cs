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

        // ─────────────────────────────────────────────
        //  Scene lifecycle, for anything that has to follow an interior in and out
        //
        //  Static, and shaped exactly like WorldStreamer's chunk events, because the save system
        //  consumes both through one code path. Interiors were invisible to it until these existed:
        //  WorldSaveStore.Hydrate had two callers, the persistent scene and chunks, so a cave was
        //  never hydrated and never dehydrated and nothing a player did inside one — a looted crate,
        //  a killed creature — outlived the walk back to the entrance.
        // ─────────────────────────────────────────────

        /// <summary>An interior scene has finished loading and its contents are addressable.</summary>
        public static event Action<string, Scene> OnInteriorLoaded;

        /// <summary>
        /// An interior scene is about to be unloaded and everything in it destroyed.
        ///
        /// Fired BEFORE the unload is issued, which is the whole point: this is the last moment
        /// anything can read the state of the objects in it.
        /// </summary>
        public static event Action<string, Scene> OnInteriorWillUnload;

        /// <summary>An interior scene is gone.</summary>
        public static event Action<string> OnInteriorUnloaded;

        /// <summary>
        /// Where a player was, and where they came from, while they are inside an interior.
        ///
        /// The saveable projection of <see cref="ReturnInfo"/>. The pin is not in it: a pin is a live
        /// GameObject registered with the streamer, rebuilt on restore rather than stored.
        /// </summary>
        public struct InteriorVisit
        {
            /// <summary>The interior scene the player is standing in.</summary>
            public string InteriorScene;

            /// <summary>Where in that interior they were standing.</summary>
            public Vector3 InsidePosition;
            public Quaternion InsideRotation;

            /// <summary>Where in the exterior they walked in from, and must be put back.</summary>
            public Vector3 ReturnPosition;
            public Quaternion ReturnRotation;
        }

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

        // ─────────────────────────────────────────────
        //  Local view — this machine's screen only
        // ─────────────────────────────────────────────

        /// <summary>
        /// Make this machine look like it is inside <paramref name="interiorSceneName"/>.
        ///
        /// Called on the machine whose own player just went in, never on the server's behalf. See
        /// the note on <c>NotifyViewer</c> for why this half is separated from the session half.
        /// </summary>
        public void ShowInteriorView(string interiorSceneName)
        {
            var scene = SceneManager.GetSceneByName(interiorSceneName);
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.SetActiveScene(scene);

            persistentVisibility?.Suspend();
        }

        /// <summary>Undo <see cref="ShowInteriorView"/> — this machine is back outside.</summary>
        public void ShowExteriorView(string exteriorSceneName)
        {
            var scene = SceneManager.GetSceneByName(exteriorSceneName);
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.SetActiveScene(scene);
            else if (persistentScene.IsValid() && persistentScene.isLoaded)
                SceneManager.SetActiveScene(persistentScene);

            persistentVisibility?.Restore();
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

        /// <summary>
        /// Send <paramref name="player"/> into <paramref name="def"/>, from any machine.
        ///
        /// The routing lives on the player, not here: this class has no NetworkObject and therefore
        /// no RPC channel of its own. It used to declare RPC methods anyway — see
        /// <see cref="PlayerInteriorTransit"/> for what that silently did.
        /// </summary>
        public void EnterInterior(GameObject player, InteriorScene def)
        {
            if (player == null || def == null || string.IsNullOrEmpty(def.SceneName))
            {
                Debug.LogWarning("[InteriorManager] EnterInterior called with invalid args.");
                return;
            }

            if (!TryGetTransit(player, out PlayerInteriorTransit transit)) return;

            transit.RequestEnter(def);
        }

        public void ExitInterior(GameObject player)
        {
            if (player == null) return;
            if (!TryGetTransit(player, out PlayerInteriorTransit transit)) return;

            transit.RequestExit();
        }

        private bool TryGetTransit(GameObject player, out PlayerInteriorTransit transit)
        {
            transit = player.GetComponent<PlayerInteriorTransit>();
            if (transit != null) return true;

            Debug.LogError($"[InteriorManager] '{player.name}' has no PlayerInteriorTransit, so an " +
                           "interior transition cannot be routed to the server. Add one to the " +
                           "player prefab.", player);
            return false;
        }

        // ─────────────────────────────────────────────
        //  Server-side implementation
        //
        //  Called only by PlayerInteriorTransit, which is what guarantees these run on the server.
        // ─────────────────────────────────────────────

        public void ServerEnterInterior(GameObject player, string sceneName, string anchorId)
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

        public void ServerExitInterior(GameObject player)
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

            // Move player back to exterior so the interior can safely unload. Scene membership is
            // session state and stays here; what the exterior LOOKS like is this player's own
            // machine's business and is handed to them below.
            if (info.ExteriorScene.IsValid() && info.ExteriorScene.isLoaded)
            {
                if (player.transform.parent != null) player.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(player, info.ExteriorScene);
            }
            TeleportPlayer(player, info.Position, info.Rotation);

            NotifyViewer(player, t => t.NotifyExited(info.ExteriorScene.name));

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
                yield break;

            // The refcount is about the SCENE, not about anybody's screen: it decides when the last
            // occupant has left and the interior can be unloaded for everyone. The exterior coming
            // back into view is a separate question, answered per player by NotifyExited above —
            // tying the two together meant one player leaving a cave while another stayed inside
            // either un-hid the world for the wrong person or never un-hid it at all.
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

        /// <summary>
        /// Tell one player's own machine that its view should change.
        ///
        /// Everything about an interior transition splits along this line. Which scene an object
        /// lives in, which scenes are loaded, and where a body stands are session facts and belong
        /// to the server. Which scene is ACTIVE and whether the exterior's lights are switched off
        /// are per-machine rendering state, and applying them here — on the server, for whichever
        /// player happened to walk through a door — is what plunged the host into darkness because
        /// somebody else entered a cave.
        /// </summary>
        private static void NotifyViewer(GameObject player, Action<PlayerInteriorTransit> notify)
        {
            var transit = player != null ? player.GetComponent<PlayerInteriorTransit>() : null;
            if (transit != null) notify(transit);
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

            // Activating the interior scene (for its RenderSettings — ambient, fog, skybox) and
            // switching off the exterior's lights are things one PLAYER's screen needs, not things
            // the session needs. They are handed to that player's machine rather than done here.
            NotifyViewer(player, t => t.NotifyEntered(scene.name));
        }

        /// <summary>
        /// Places the player, on whichever machine is allowed to.
        ///
        /// This used to write the transform here, on the server. The player's NetworkTransform is
        /// owner-authoritative, so for anyone but the host that write was overwritten by the owner
        /// within a tick: a remote client walked into a building and stayed exactly where they were,
        /// while the server believed it had moved them inside.
        /// </summary>
        private static void TeleportPlayer(GameObject player, Vector3 position, Quaternion rotation) =>
            NetworkedTeleport.Move(player, position, rotation);

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
                    AnnounceLoaded(sceneName, onLoaded);
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
                op.completed += _ => AnnounceLoaded(sceneName, onLoaded);
            }
        }

        /// <summary>
        /// Announces a loaded interior, then hands it to whoever was waiting for it.
        ///
        /// <para>
        /// The order is the whole point. <see cref="OnInteriorLoaded"/> is what puts the interior's
        /// saved state back into it, and <paramref name="onLoaded"/> is what drops a player into the
        /// middle of it. A player placed first would be standing in the authored cave for the frame
        /// or two before its record arrived, watching the crate they emptied last session refill and
        /// then empty again.
        /// </para>
        /// </summary>
        private static void AnnounceLoaded(string sceneName, Action<Scene> onLoaded)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (scene.IsValid() && scene.isLoaded)
                OnInteriorLoaded?.Invoke(sceneName, scene);

            onLoaded?.Invoke(scene);
        }

        private void UnloadInterior(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            string sceneName = scene.name;

            // Before the unload is issued, not after: everything worth capturing in this cave is
            // about to be destroyed, and a listener that waited for the unload to complete would
            // find nothing left to read.
            OnInteriorWillUnload?.Invoke(sceneName, scene);

            if (Network.IsNetworked)
                NetworkManager.Singleton.SceneManager.UnloadScene(scene);
            else
                SceneManager.UnloadSceneAsync(scene);

            // Announced on issue rather than on completion. The only thing this says is "stop
            // tracking that scene handle", and the capture that mattered has already happened above;
            // waiting for the async unload would mean carrying a second completion handler through
            // two code paths for no additional truth.
            OnInteriorUnloaded?.Invoke(sceneName);
        }

        // ─────────────────────────────────────────────
        //  Interior visits, as saveable state
        // ─────────────────────────────────────────────

        /// <summary>
        /// Where <paramref name="player"/> is, if they are inside an interior right now.
        ///
        /// <para>
        /// Saved per player rather than per world, because it is a fact about a person and not about
        /// the map: two players can be in two different caves, and one of them can quit while the
        /// other stays. Without it a save taken inside a cave records a player at coordinates that
        /// only exist in a scene the load never opens — they come back inside the terrain, with no
        /// record of the door they came through.
        /// </para>
        /// </summary>
        public bool TryGetVisit(GameObject player, out InteriorVisit visit)
        {
            visit = default;
            if (player == null) return false;

            if (!returnInfoByPlayer.TryGetValue(GetPlayerKey(player), out ReturnInfo info) || info == null)
                return false;

            visit = new InteriorVisit
            {
                InteriorScene = player.scene.IsValid() ? player.scene.name : null,
                InsidePosition = player.transform.position,
                InsideRotation = player.transform.rotation,
                ReturnPosition = info.Position,
                ReturnRotation = info.Rotation,
            };

            return !string.IsNullOrEmpty(visit.InteriorScene);
        }

        /// <summary>
        /// Restore-only. Puts a player back inside the interior a save found them in. Called by the
        /// save system; do not call from gameplay.
        ///
        /// <para>
        /// Idempotent, and it must be: the deferred pass that drives it runs once world-wide, again
        /// on every player binding and again for every chunk that streams in afterwards. A player who
        /// already holds a return record is already inside and is left alone.
        /// </para>
        /// <para>
        /// No <c>InteriorAnchor</c> is consulted. The anchor answers "where does a person who has
        /// just walked through this door appear", and this player did not just walk through it — they
        /// were somewhere specific in that cave when the world was written, and that is where they go.
        /// </para>
        /// </summary>
        public void RestoreVisit(GameObject player, InteriorVisit visit)
        {
            if (player == null || string.IsNullOrEmpty(visit.InteriorScene)) return;

            // Interiors are session state, and session state is the server's. A client that placed
            // itself inside a cave nobody else had loaded would be alone in a scene that does not
            // exist for the machine that owns the world.
            if (Network.IsNetworked && !Network.Server) return;

            ulong key = GetPlayerKey(player);
            if (returnInfoByPlayer.ContainsKey(key)) return;

            returnInfoByPlayer[key] = new ReturnInfo
            {
                Position = visit.ReturnPosition,
                Rotation = visit.ReturnRotation,
                ExteriorScene = persistentScene.IsValid() ? persistentScene : player.scene,
                ReturnPin = CreateReturnPin(visit.ReturnPosition),
            };

            string sceneName = visit.InteriorScene;
            Vector3 position = visit.InsidePosition;
            Quaternion rotation = visit.InsideRotation;

            var existing = SceneManager.GetSceneByName(sceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
                PlacePlayerAt(player, existing, position, rotation);
                return;
            }

            GameObject pendingPlayer = player;

            LoadInteriorAdditive(sceneName, scene =>
            {
                interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
                if (pendingPlayer == null) return;
                PlacePlayerAt(pendingPlayer, scene, position, rotation);
            });
        }

        /// <summary>Puts a player at an exact spot in an interior, view and all. See <see cref="PlacePlayerAtAnchor"/>.</summary>
        private void PlacePlayerAt(GameObject player, Scene scene, Vector3 position, Quaternion rotation)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"[InteriorManager] Interior scene for '{player.name}' never loaded; " +
                                 "leaving them outside.", player);
                return;
            }

            if (player.transform.parent != null) player.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(player, scene);
            TeleportPlayer(player, position, rotation);

            NotifyViewer(player, t => t.NotifyEntered(scene.name));
        }
    }
}
