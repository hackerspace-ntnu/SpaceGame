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

        /// <summary>
        /// Who is inside an interior right now, keyed by the occupant GameObject itself.
        ///
        /// An occupant is anything that can walk into an interior — a player, a mount with a rider
        /// on its back, a creature. The key used to be <c>NetworkObjectId</c>, which collapses to 0
        /// for every body that has no <see cref="NetworkObject"/>, so all of them shared one record.
        /// The map is runtime-only (a save stores the visit itself, not this key), so the object
        /// reference is both the cheapest and the most exact identity available here.
        /// </summary>
        private readonly Dictionary<GameObject, ReturnInfo> returnInfoByOccupant = new();
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

        /// <summary>Per-occupant real-time stamp until which entrance triggers should treat re-entry as locked out.</summary>
        private readonly Dictionary<GameObject, float> entranceLockoutUntil = new();

        /// <summary>
        /// True if <paramref name="player"/> just exited an interior and the post-exit lockout window
        /// has not elapsed. Entrance triggers (InteriorEntrance, volume triggers) should not fire while
        /// this is true — it stops the player yo-yoing straight back into the interior they just left.
        /// </summary>
        public bool IsEntranceLockedOut(GameObject occupant)
        {
            if (occupant == null) return false;
            return entranceLockoutUntil.TryGetValue(occupant, out float until)
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
        public bool IsInsideInterior(GameObject occupant)
        {
            if (occupant == null) return false;
            return returnInfoByOccupant.ContainsKey(occupant);
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
            foreach (var kvp in returnInfoByOccupant)
            {
                var info = kvp.Value;
                if (info?.ReturnPin == null) continue;
                if (worldStreamer != null)
                    worldStreamer.UnregisterTrackedTransform(info.ReturnPin);
                Destroy(info.ReturnPin.gameObject);
            }
            returnInfoByOccupant.Clear();
            entranceLockoutUntil.Clear();

            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────────────────────
        //  Public API — call from interactables
        // ─────────────────────────────────────────────

        /// <summary>
        /// Send <paramref name="occupant"/> into <paramref name="def"/>, from any machine.
        ///
        /// An occupant is anything that can walk through a door: a player, a creature, or a mount
        /// with a rider parented to its back. The two kinds reach the server differently, and that
        /// is the only thing this method decides.
        ///
        /// A player's body is owner-authoritative, so the request has to travel from the machine
        /// that owns it — the routing lives on <see cref="PlayerInteriorTransit"/>, because this
        /// class has no NetworkObject and therefore no RPC channel of its own. It used to declare
        /// RPC methods anyway; see that class for what that silently did.
        ///
        /// Everything else is server-authoritative and is only ever asked for by the server in the
        /// first place (<c>VolumeTrigger</c> fires nowhere else, and an AI body's owner IS the
        /// server), so there is nothing to route: the transition runs here.
        /// </summary>
        public void EnterInterior(GameObject occupant, InteriorScene def)
        {
            if (occupant == null || def == null || string.IsNullOrEmpty(def.SceneName))
            {
                Debug.LogWarning("[InteriorManager] EnterInterior called with invalid args.");
                return;
            }

            if (occupant.TryGetComponent(out PlayerInteriorTransit transit))
            {
                transit.RequestEnter(def);
                return;
            }

            if (!HasAuthorityOver(occupant, "enter an interior")) return;

            ServerEnterInterior(occupant, def.SceneName, def.SpawnAnchorId);
        }

        public void ExitInterior(GameObject occupant)
        {
            if (occupant == null) return;

            if (occupant.TryGetComponent(out PlayerInteriorTransit transit))
            {
                transit.RequestExit();
                return;
            }

            if (!HasAuthorityOver(occupant, "leave an interior")) return;

            ServerExitInterior(occupant);
        }

        /// <summary>
        /// Whether this machine may move <paramref name="occupant"/> between scenes itself.
        ///
        /// Only reached for a body with no <see cref="PlayerInteriorTransit"/> — i.e. one nothing
        /// can route to the server for us. Scene membership is session state, so a client that ran
        /// this would move a creature into a scene no other machine has loaded. Loud rather than
        /// silent: reaching it means a trigger fired somewhere it should not have.
        /// </summary>
        private static bool HasAuthorityOver(GameObject occupant, string what)
        {
            if (!Network.IsNetworked || Network.Server) return true;

            Debug.LogWarning($"[InteriorManager] A client tried to make '{occupant.name}' {what}, but " +
                             "it has no PlayerInteriorTransit to route the request with. Interiors are " +
                             "session state — only the server moves a body that is not a player.", occupant);
            return false;
        }

        // ─────────────────────────────────────────────
        //  Server-side implementation
        //
        //  Called only by PlayerInteriorTransit, which is what guarantees these run on the server.
        // ─────────────────────────────────────────────

        public void ServerEnterInterior(GameObject occupant, string sceneName, string anchorId)
        {
            PruneDestroyedOccupants();

            // Re-entry without a prior exit (interior-to-interior, or duplicate call) — drop the
            // old pin so we don't leak it and don't keep chunks loaded around a stale position.
            if (returnInfoByOccupant.TryGetValue(occupant, out var stale))
                CleanupReturnInfo(occupant, stale);

            Vector3 returnPos = occupant.transform.position;
            Quaternion returnRot = occupant.transform.rotation;
            Scene exteriorScene = persistentScene.IsValid() ? persistentScene : occupant.scene;

            // Drop a pin at the return position and register it with WorldStreamer. While the
            // occupant is inside, WorldStreamer sees them at the interior anchor and would unload
            // the exterior chunks under their origin — the pin keeps those chunks alive.
            Transform pin = CreateReturnPin(returnPos);

            // Remember where they were so ExitInterior can put them back.
            returnInfoByOccupant[occupant] = new ReturnInfo
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
                PlaceOccupantAtAnchor(occupant, existing, anchorId);
                return;
            }

            // The occupant can be destroyed while the load runs — a creature killed on the
            // threshold, a mount despawned by streaming — so the callback re-checks it.
            GameObject pendingOccupant = occupant;

            Action<Scene> onLoaded = scene =>
            {
                interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
                if (pendingOccupant == null) return;
                PlaceOccupantAtAnchor(pendingOccupant, scene, anchorId);
            };

            LoadInteriorAdditive(sceneName, onLoaded);
        }

        /// <summary>
        /// Drop records whose occupant no longer exists.
        ///
        /// The map holds a body only for the length of a visit, and a visit normally ends with an
        /// exit that cleans up after itself. A creature that dies inside a cave never exits, so its
        /// record — and the streamer pin holding a chunk loaded around a body that is gone — would
        /// sit there for the session. Cheap: the map is as long as the number of occupants inside
        /// interiors right now, and this runs once per entry.
        /// </summary>
        private void PruneDestroyedOccupants()
        {
            if (returnInfoByOccupant.Count == 0) return;

            List<GameObject> dead = null;
            foreach (var kvp in returnInfoByOccupant)
            {
                if (kvp.Key != null) continue;
                (dead ??= new List<GameObject>()).Add(kvp.Key);
            }
            if (dead == null) return;

            foreach (var key in dead)
            {
                CleanupReturnInfo(key, returnInfoByOccupant[key]);
                entranceLockoutUntil.Remove(key);
            }
        }

        public void ServerExitInterior(GameObject occupant)
        {
            if (!returnInfoByOccupant.TryGetValue(occupant, out var info))
            {
                Debug.LogWarning($"[InteriorManager] No return info for '{occupant.name}' — cannot exit.", occupant);
                return;
            }

            // Drive the exit on a coroutine so we can wait for the exterior chunks under the
            // return position to be fully loaded before teleporting the player. Otherwise the
            // teleport lands on an unloaded chunk and the player falls through the ground.
            StartCoroutine(ExitInteriorRoutine(occupant, info));
        }

        private IEnumerator ExitInteriorRoutine(GameObject occupant, ReturnInfo info)
        {
            Scene currentInterior = occupant.scene;
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
                    if (occupant == null)
                    {
                        CleanupReturnInfo(occupant, info);
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

            if (occupant == null)
            {
                CleanupReturnInfo(occupant, info);
                yield break;
            }

            // Move the occupant back to the exterior so the interior can safely unload. Scene
            // membership is session state and stays here; what the exterior LOOKS like belongs to
            // the machine of every player riding along, and is handed to them below.
            if (info.ExteriorScene.IsValid() && info.ExteriorScene.isLoaded)
            {
                if (occupant.transform.parent != null) occupant.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(occupant, info.ExteriorScene);
            }
            TeleportOccupant(occupant, info.Position, info.Rotation);

            NotifyViewers(occupant, t => t.NotifyExited(info.ExteriorScene.name));

            // One frame to let Physics.SyncTransforms-ish settling happen, then ground-clamp
            // so a stale Y (e.g., we entered the interior mid-jump or the terrain changed) can't
            // leave the player floating or clipped into the ground.
            yield return null;
            GroundClampOccupant(occupant, info.Position);

            // The player is now standing exactly where they entered — i.e. inside the entrance
            // trigger volume. Arm a lockout so entrance triggers ignore them until they've had a
            // chance to walk clear; otherwise a walk-in entrance fires this same frame and sends
            // them straight back in.
            entranceLockoutUntil[occupant] = Time.unscaledTime + Mathf.Max(0f, postExitEntranceLockout);

            CleanupReturnInfo(occupant, info);

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
        /// Tell the machine of every player who just moved that its view should change.
        ///
        /// Everything about an interior transition splits along this line. Which scene an object
        /// lives in, which scenes are loaded, and where a body stands are session facts and belong
        /// to the server. Which scene is ACTIVE and whether the exterior's lights are switched off
        /// are per-machine rendering state, and applying them here — on the server, for whichever
        /// player happened to walk through a door — is what plunged the host into darkness because
        /// somebody else entered a cave.
        ///
        /// Searched in CHILDREN, not on the occupant alone: a rider is parented to their mount
        /// (<c>MountModule</c> reparents the body, over the wire, to the seat), so when the mount
        /// walks into the cave the person carried inside it is a child of the thing that moved.
        /// Without this they would be standing in the interior looking at the exterior's lighting,
        /// with the exterior scene still active.
        /// </summary>
        private static void NotifyViewers(GameObject occupant, Action<PlayerInteriorTransit> notify)
        {
            if (occupant == null) return;

            // Includes the occupant itself. Inactive included: a rider's body stays enabled while
            // mounted, but nothing here should depend on that.
            var viewers = occupant.GetComponentsInChildren<PlayerInteriorTransit>(true);
            foreach (var transit in viewers) notify(transit);
        }

        private void CleanupReturnInfo(GameObject key, ReturnInfo info)
        {
            if (info != null && info.ReturnPin != null)
            {
                if (worldStreamer != null)
                    worldStreamer.UnregisterTrackedTransform(info.ReturnPin);
                Destroy(info.ReturnPin.gameObject);
                info.ReturnPin = null;
            }
            returnInfoByOccupant.Remove(key);
        }

        private void GroundClampOccupant(GameObject occupant, Vector3 returnPos)
        {
            if (occupant == null) return;
            if (worldStreamer == null) return;

            if (!worldStreamer.TrySampleGroundHeight(returnPos, out float groundY))
                return;

            var pos = occupant.transform.position;
            // Only lift up to ground level if the saved Y is below it (we were clipped under terrain
            // due to a respawn-chunk mismatch). Don't drop the body onto the ground if it is
            // legitimately a bit above — gravity will resolve that.
            if (pos.y < groundY)
            {
                float lifted = Mathf.Min(groundY + 0.05f, pos.y + groundClampMaxLift);
                TeleportOccupant(occupant, new Vector3(pos.x, lifted, pos.z), occupant.transform.rotation);
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

        private void PlaceOccupantAtAnchor(GameObject occupant, Scene scene, string anchorId)
        {
            var anchor = InteriorAnchor.Find(scene, anchorId);
            Vector3 position = anchor != null ? anchor.transform.position : Vector3.zero;
            Quaternion rotation = anchor != null ? anchor.transform.rotation : Quaternion.identity;

            if (anchor == null)
                Debug.LogWarning($"[InteriorManager] No InteriorAnchor '{anchorId}' in {scene.name} — dropping '{occupant.name}' at origin.", occupant);

            // Unparented deliberately, and only the OCCUPANT is: whatever is parented to it — a
            // rider on a mount's seat — is a child of the thing being moved and comes along, both
            // in the scene move and in the teleport. Breaking that link here would leave the rider
            // standing outside while their mount walked into the cave.
            if (occupant.transform.parent != null) occupant.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(occupant, scene);
            TeleportOccupant(occupant, position, rotation);

            // Activating the interior scene (for its RenderSettings — ambient, fog, skybox) and
            // switching off the exterior's lights are things a PLAYER's screen needs, not things
            // the session needs. They are handed to those players' machines rather than done here.
            NotifyViewers(occupant, t => t.NotifyEntered(scene.name));
        }

        /// <summary>
        /// Places the body, on whichever machine is allowed to.
        ///
        /// This used to write the transform here, on the server. A player's NetworkTransform is
        /// owner-authoritative, so for anyone but the host that write was overwritten by the owner
        /// within a tick: a remote client walked into a building and stayed exactly where they were,
        /// while the server believed it had moved them inside. A server-authoritative body (a
        /// creature, a mount) takes the same call and is simply moved here — see
        /// <see cref="NetworkedTeleport.Move"/>.
        /// </summary>
        private static void TeleportOccupant(GameObject occupant, Vector3 position, Quaternion rotation) =>
            NetworkedTeleport.Move(occupant, position, rotation);

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
        public bool TryGetVisit(GameObject occupant, out InteriorVisit visit)
        {
            visit = default;
            if (occupant == null) return false;

            if (!TryFindReturnInfo(occupant, out _, out ReturnInfo info)) return false;

            visit = new InteriorVisit
            {
                InteriorScene = occupant.scene.IsValid() ? occupant.scene.name : null,
                InsidePosition = occupant.transform.position,
                InsideRotation = occupant.transform.rotation,
                ReturnPosition = info.Position,
                ReturnRotation = info.Rotation,
            };

            return !string.IsNullOrEmpty(visit.InteriorScene);
        }

        /// <summary>
        /// The visit record covering <paramref name="occupant"/> — its own, or the one held by
        /// whatever carried it in.
        ///
        /// A rider who went into a cave on a mount is inside on the mount's record, not on one of
        /// their own, and the walk up the parents is what stops their save saying "outside". A
        /// player written as outside while standing in an interior comes back at interior
        /// coordinates in the exterior world — inside the terrain, with no door to walk back out
        /// of. That is the exact failure <see cref="InteriorVisit"/> exists to prevent.
        /// </summary>
        private bool TryFindReturnInfo(GameObject body, out GameObject occupant, out ReturnInfo info)
        {
            for (Transform t = body.transform; t != null; t = t.parent)
            {
                if (returnInfoByOccupant.TryGetValue(t.gameObject, out info) && info != null)
                {
                    occupant = t.gameObject;
                    return true;
                }
            }

            occupant = null;
            info = null;
            return false;
        }

        /// <summary>
        /// The body an exit has to be asked for on behalf of <paramref name="body"/>, or null if it
        /// is not inside an interior at all.
        ///
        /// Answers <paramref name="body"/> itself for anyone who walked in on their own feet, and
        /// the mount for a rider who did not: the record — and therefore the exterior position to
        /// return to — belongs to the thing that crossed the threshold. Asking to exit for a rider
        /// directly would find no record and log that there is nothing to return them to.
        /// </summary>
        public GameObject ResolveOccupant(GameObject body)
        {
            if (body == null) return null;
            return TryFindReturnInfo(body, out GameObject occupant, out _) ? occupant : null;
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
        public void RestoreVisit(GameObject occupant, InteriorVisit visit)
        {
            if (occupant == null || string.IsNullOrEmpty(visit.InteriorScene)) return;

            // Interiors are session state, and session state is the server's. A client that placed
            // itself inside a cave nobody else had loaded would be alone in a scene that does not
            // exist for the machine that owns the world.
            if (Network.IsNetworked && !Network.Server) return;

            if (returnInfoByOccupant.ContainsKey(occupant)) return;

            returnInfoByOccupant[occupant] = new ReturnInfo
            {
                Position = visit.ReturnPosition,
                Rotation = visit.ReturnRotation,
                ExteriorScene = persistentScene.IsValid() ? persistentScene : occupant.scene,
                ReturnPin = CreateReturnPin(visit.ReturnPosition),
            };

            string sceneName = visit.InteriorScene;
            Vector3 position = visit.InsidePosition;
            Quaternion rotation = visit.InsideRotation;

            var existing = SceneManager.GetSceneByName(sceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
                PlaceOccupantAt(occupant, existing, position, rotation);
                return;
            }

            GameObject pendingOccupant = occupant;

            LoadInteriorAdditive(sceneName, scene =>
            {
                interiorRefCount[sceneName] = interiorRefCount.GetValueOrDefault(sceneName) + 1;
                if (pendingOccupant == null) return;
                PlaceOccupantAt(pendingOccupant, scene, position, rotation);
            });
        }

        /// <summary>Puts a body at an exact spot in an interior, view and all. See <see cref="PlaceOccupantAtAnchor"/>.</summary>
        private void PlaceOccupantAt(GameObject occupant, Scene scene, Vector3 position, Quaternion rotation)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"[InteriorManager] Interior scene for '{occupant.name}' never loaded; " +
                                 "leaving them outside.", occupant);
                return;
            }

            if (occupant.transform.parent != null) occupant.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(occupant, scene);
            TeleportOccupant(occupant, position, rotation);

            NotifyViewers(occupant, t => t.NotifyEntered(scene.name));
        }
    }
}
