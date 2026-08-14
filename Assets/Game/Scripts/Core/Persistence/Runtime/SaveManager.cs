using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// The save system's front door: one object in the persistent scene that owns the world store,
    /// the player service, and the file on disk.
    ///
    /// Everything here is server authority. The game always runs hosted — even singleplayer goes
    /// through <c>StartHost</c> — and world and player state live on the server, so a client that
    /// saved would write out its own replicated approximation of a world it does not own. Clients
    /// get loaded state the same way they get every other change: through replication.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        [Header("Autosave")]
        [Tooltip("Save automatically this often, in seconds. Zero or less disables the timer.")]
        [SerializeField] private float autoSaveIntervalSeconds = 300f;

        [Tooltip("Save when the application is quit or loses focus on mobile. Strongly recommended: " +
                 "it is the only thing standing between an alt-F4 and a lost session.")]
        [SerializeField] private bool saveOnQuit = true;

        [Header("Format")]
        [Tooltip("Write indented JSON. Larger on disk, but a save you can open and read is a save " +
                 "you can diagnose.")]
        [SerializeField] private bool prettyPrint = true;

        [Header("Debug")]
        [SerializeField] private bool verbose;

        /// <summary>
        /// A document read from disk that the next world load should start from, handed across the
        /// scene load that stands between pressing Continue and the world existing.
        ///
        /// Static because nothing survives that load: the menu's SaveManager is destroyed with the
        /// menu scene, and the world's has not been created yet.
        /// </summary>
        public static SaveDocument PendingLoad { get; private set; }

        /// <summary>Fired after a save has been written. The argument is the slot id.</summary>
        public static event Action<string> OnSaved;

        /// <summary>Fired once a loaded world has been fully applied.</summary>
        public static event Action OnLoadApplied;

        /// <summary>Fired when a save or load fails, with a message fit to show a player.</summary>
        public static event Action<string> OnSaveError;

        private static readonly List<ISaveable> GlobalSavers = new();

        /// <summary>
        /// Global state from a load that has not found its saver yet.
        ///
        /// Awake order between MonoBehaviours is not defined, so a global saver's OnEnable can run
        /// after this SaveManager's Awake. Restoring only what was registered at Awake time would
        /// therefore silently skip savers depending on scene ordering. Keeping the payloads here and
        /// applying them at registration makes the outcome the same either way.
        /// </summary>
        private static StateBag pendingGlobals;

        private SaveSlots slots;
        private WorldSaveStore worldStore;
        private PlayerSaveService playerService;
        private float nextAutoSaveTime;
        private Task activeWrite;
        private bool loadedFromSave;
        private bool loadAnnounced;

        /// <summary>
        /// Playtime carried in from the loaded save. Time.realtimeSinceStartup only counts THIS
        /// process, so without this a twenty-hour save reads as five minutes old the moment it is
        /// loaded and saved again.
        /// </summary>
        private double inheritedPlaytime;

        private float sessionStartTime;

        private double TotalPlaytime => inheritedPlaytime + (Time.realtimeSinceStartupAsDouble - sessionStartTime);

        public WorldSaveStore World => worldStore;
        public PlayerSaveService Players => playerService;
        public SaveSlots Slots => slots ??= new SaveSlots(DefaultRoot);

        /// <summary>Where saves live. Under persistentDataPath so it survives reinstalls and is writable on every platform.</summary>
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "Saves");

        // ─────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A second SaveManager — this project produces them during scene transitions (the
                // same session's log carries Netcode's own "more than one NetworkManager instance"
                // complaint). It must not take over Instance or subscribe to anything.
                //
                // But it MUST still be safe to call. Destroy is deferred to the end of the frame,
                // and Unity delivers OnApplicationQuit to whatever is alive when the app closes.
                // This branch used to return with null stores, so a quit answered by this instance
                // died with a NullReferenceException inside BuildDocument and wrote nothing at all
                // — the bug that made quit-saves silently stop appearing.
                EnsureStores(null);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Consuming the pending document here — before any chunk has loaded and before
            // NetworkGameManager spawns anyone — is what makes a load work at all. Both stores must
            // be holding the saved state by the time the first OnChunkLoaded fires.
            SaveDocument document = PendingLoad;
            PendingLoad = null;

            EnsureStores(document);

            loadedFromSave = document != null;
            sessionStartTime = Time.realtimeSinceStartup;
            inheritedPlaytime = document?.Header?.PlaytimeSeconds ?? 0d;

            if (document != null)
            {
                RestoreGlobals(document);
                Log($"Loaded save from {document.Header.SavedAtUtc:u} " +
                    $"({document.Players.Count} player record(s), {document.World.Scenes.Count} scene record(s)).");
            }

            WorldStreamer.OnChunkLoaded += HandleChunkLoaded;
            WorldStreamer.OnChunkWillUnload += HandleChunkWillUnload;
            WorldStreamer.OnChunkUnloaded += HandleChunkUnloaded;

            nextAutoSaveTime = Time.time + Mathf.Max(1f, autoSaveIntervalSeconds);
        }

        /// <summary>
        /// Builds the stores if they are not there yet, so no code path can ever find them null.
        ///
        /// Idempotent and safe to call from anywhere. <paramref name="document"/> seeds them on the
        /// first call only — a later call never re-reads a save, so a duplicate instance calling
        /// this cannot swallow a staged load or wipe live state.
        /// </summary>
        private void EnsureStores(SaveDocument document)
        {
            slots ??= new SaveSlots(DefaultRoot);
            worldStore ??= new WorldSaveStore(document?.World);
            playerService ??= new PlayerSaveService(document?.Players);
        }

        private void Start()
        {
            // The persistent scene gets no streaming events — it is never loaded or unloaded — so
            // its record has to be put back by hand. It matters more than it looks: every SceneTracked
            // entity with the Pin policy lives here, which is the population that follows players
            // around and would otherwise be the one part of the world a load silently skipped.
            //
            // In Start rather than Awake so the rest of the scene has finished waking up first.
            if (Network.IsNetworked && !Network.Server) return;

            Scene persistent = gameObject.scene;
            if (persistent.IsValid() && persistent.isLoaded)
                worldStore.Hydrate(SceneKey.Persistent, persistent);
        }

        private void OnDestroy()
        {
            WorldStreamer.OnChunkLoaded -= HandleChunkLoaded;
            WorldStreamer.OnChunkWillUnload -= HandleChunkWillUnload;
            WorldStreamer.OnChunkUnloaded -= HandleChunkUnloaded;

            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (autoSaveIntervalSeconds <= 0f) return;
            if (!Network.Server && Network.IsNetworked) return;
            if (Time.time < nextAutoSaveTime) return;

            nextAutoSaveTime = Time.time + autoSaveIntervalSeconds;
            Save(SaveSlots.AutoSaveSlotId, "Autosave");
        }

        private void OnApplicationQuit()
        {
            if (!saveOnQuit) return;
            if (Network.IsNetworked && !Network.Server) return;

            // Synchronously. Unity gives the process no frames after this, so a background write
            // would be killed with it — which is the one moment a save is most needed.
            Save(SaveSlots.AutoSaveSlotId, "Autosave", synchronous: true);
        }

        // ─────────────────────────────────────────────
        //  Global savers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Registers a saver for state that belongs to the session rather than to any object —
        /// the game timer, world flags. Kept in a static list so a saver can register in OnEnable
        /// without caring whether the SaveManager exists yet.
        /// </summary>
        public static void RegisterGlobalSaver(ISaveable saver)
        {
            if (saver == null || GlobalSavers.Contains(saver)) return;

            GlobalSavers.Add(saver);

            // A load already staged state for this key before the saver existed. Applying it here,
            // and consuming it, means the same result whether the saver registered before or after
            // the SaveManager woke up — and never twice.
            if (pendingGlobals == null || string.IsNullOrEmpty(saver.SaveKey)) return;
            if (!pendingGlobals.TryGetRaw(saver.SaveKey, out var payload)) return;

            pendingGlobals.Remove(saver.SaveKey);

            try
            {
                saver.RestoreState(payload);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Global saver '{saver.SaveKey}' failed to restore: {e}");
            }
        }

        public static void UnregisterGlobalSaver(ISaveable saver) => GlobalSavers.Remove(saver);

        /// <summary>
        /// Announces that a loaded session is now playable — the local player exists and is holding
        /// its restored state. Raised once per load; callers may call it more than once.
        /// </summary>
        public void NotifyLoadApplied()
        {
            if (!loadedFromSave || loadAnnounced) return;

            loadAnnounced = true;
            OnLoadApplied?.Invoke();
        }

        // ─────────────────────────────────────────────
        //  Saving
        // ─────────────────────────────────────────────

        /// <summary>
        /// Writes the current session to <paramref name="slotId"/>.
        ///
        /// Capture runs on the main thread — it touches transforms and components, which Unity only
        /// permits there — and only the finished text is handed to a background write. Returns false
        /// when this peer is not allowed to save or a write is already in flight.
        /// </summary>
        public bool Save(string slotId, string label = null, bool synchronous = false)
        {
            // Whichever instance Unity happens to call, the save is taken by the one holding the
            // session's state. A doomed duplicate answering a quit would otherwise write its own
            // empty stores over a good file.
            if (Instance != null && Instance != this)
                return Instance.Save(slotId, label, synchronous);

            // Belt and braces. Everything above should make this impossible; a save is the one
            // operation where "should be impossible" is not good enough, because the cost of being
            // wrong is the player's session.
            EnsureStores(null);

            if (Network.IsNetworked && !Network.Server)
            {
                Log("Save ignored: only the server owns world state.");
                return false;
            }

            if (activeWrite is { IsCompleted: false })
            {
                Log("Save ignored: the previous write has not finished.");
                return false;
            }

            SaveDocument document;
            string json;

            try
            {
                document = BuildDocument(slotId, label);
                json = SaveSerializer.ToJson(document, prettyPrint);
            }
            catch (Exception e)
            {
                Fail($"Could not prepare the save: {e.Message}");
                Debug.LogException(e, this);
                return false;
            }

            string path = Slots.PathFor(slotId);

            if (SaveFileStore.WouldDiscardAllPlayers(path, document))
            {
                Fail($"Refused to save '{slotId}': the capture found no players, and the file " +
                     "already there has some. Overwriting it would end the session's progress. " +
                     "This usually means the save ran before anyone spawned, or after they were " +
                     "torn down.");
                return false;
            }

            if (synchronous)
            {
                try
                {
                    SaveFileStore.Write(path, json);
                    Announce(slotId, path, document);
                    OnSaved?.Invoke(slotId);
                }
                catch (Exception e)
                {
                    Fail($"Could not write the save: {e.Message}");
                    Debug.LogException(e, this);
                    return false;
                }

                return true;
            }

            activeWrite = Task.Run(() => SaveFileStore.Write(path, json))
                .ContinueWith(task => CompleteWrite(task, slotId, path, document),
                              TaskScheduler.FromCurrentSynchronizationContext());

            return true;
        }

        /// <summary>
        /// Reports a completed save, always — not behind <see cref="verbose"/>.
        ///
        /// A save is a rare, meaningful event, and with the files under persistentDataPath and no
        /// UI listing them there is otherwise NOTHING telling anyone whether saving works. That is
        /// how a quit-save could throw for two sessions running without being noticed. The counts
        /// are here because "saved" and "saved something worth having" are different claims.
        /// </summary>
        private void Announce(string slotId, string path, SaveDocument document)
        {
            int players = document?.Players?.Count ?? 0;
            int scenes = document?.World?.Scenes?.Count ?? 0;

            Debug.Log($"[Save] Wrote '{slotId}' — {players} player(s), {scenes} scene record(s) → {path}", this);
        }

        /// <summary>Writes to the quicksave slot.</summary>
        public bool QuickSave() => Save(SaveSlots.QuickSaveSlotId, "Quicksave");

        private void CompleteWrite(Task task, string slotId, string path, SaveDocument document)
        {
            if (task.IsFaulted)
            {
                Exception error = task.Exception?.GetBaseException();
                Fail($"Could not write the save: {error?.Message}");
                if (error != null) Debug.LogException(error, this);
                return;
            }

            Announce(slotId, path, document);
            OnSaved?.Invoke(slotId);
        }

        /// <summary>
        /// Assembles the whole session into a document.
        ///
        /// The two capture calls are the point: without them a save writes whatever the stores were
        /// last told, which for a session that has not unloaded a chunk since it started is nothing
        /// at all.
        /// </summary>
        public SaveDocument BuildDocument(string slotId, string label = null)
        {
            playerService.CaptureAll();
            CaptureLoadedScenes();

            // After the capture, never before: a chunk that has just been re-read may have gone
            // empty, and a chunk that is about to be read is empty only because nothing has looked
            // at it yet.
            worldStore.PruneEmpty();

            var document = new SaveDocument
            {
                Header = new SaveHeader
                {
                    Version = SaveDocument.CurrentVersion,
                    SavedAtUtc = DateTime.UtcNow,
                    PlaytimeSeconds = TotalPlaytime,
                    GameVersion = Application.version,
                    SlotLabel = string.IsNullOrEmpty(label) ? slotId : label,
                },
                Players = playerService.Snapshot(),
                World = worldStore.Record,
            };

            CaptureGlobals(document);
            return document;
        }

        /// <summary>
        /// Refreshes the record of every loaded scene, plus the persistent scene.
        ///
        /// The persistent scene is included explicitly because it is not a chunk and no streaming
        /// event ever fires for it — and it is where every Pin'd entity lives, which is exactly the
        /// population that follows the player around and would otherwise never be saved.
        /// </summary>
        private void CaptureLoadedScenes()
        {
            worldStore.DehydrateLoaded();

            Scene persistent = gameObject.scene;
            if (persistent.IsValid() && persistent.isLoaded)
                worldStore.Dehydrate(SceneKey.Persistent, persistent);
        }

        private void CaptureGlobals(SaveDocument document)
        {
            StateBag bag = document.World.Global ??= new StateBag();

            foreach (ISaveable saver in GlobalSavers)
            {
                if (saver == null || string.IsNullOrEmpty(saver.SaveKey)) continue;

                try
                {
                    bag.Set(saver.SaveKey, saver.CaptureState());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] Global saver '{saver.SaveKey}' failed to capture: {e}", this);
                }
            }
        }

        private void RestoreGlobals(SaveDocument document)
        {
            StateBag bag = document.World?.Global;
            if (bag == null) return;

            // Everything the loaded document has to say about global state, staged for savers that
            // have not registered yet. Savers already registered are served in the same pass, so
            // registration order stops mattering.
            pendingGlobals = new StateBag();
            pendingGlobals.MergeFrom(bag);

            foreach (ISaveable saver in new List<ISaveable>(GlobalSavers))
            {
                if (saver == null || string.IsNullOrEmpty(saver.SaveKey)) continue;
                if (!pendingGlobals.TryGetRaw(saver.SaveKey, out var payload)) continue;

                pendingGlobals.Remove(saver.SaveKey);

                try
                {
                    saver.RestoreState(payload);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] Global saver '{saver.SaveKey}' failed to restore: {e}", this);
                }
            }
        }

        // ─────────────────────────────────────────────
        //  Loading
        // ─────────────────────────────────────────────

        /// <summary>
        /// Reads a slot and stages it for the next world load. Does NOT itself change scenes —
        /// the caller decides how the world is entered, and the staged document is picked up by
        /// whichever SaveManager wakes up in it.
        /// </summary>
        public static bool StageLoad(string slotId, out string error)
        {
            var slots = new SaveSlots(DefaultRoot);
            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor(slotId));

            switch (result.Outcome)
            {
                case SaveFileStore.ReadOutcome.Ok:
                    error = null;
                    PendingLoad = result.Document;
                    return true;

                case SaveFileStore.ReadOutcome.RecoveredFromBackup:
                    error = result.Error;
                    PendingLoad = result.Document;
                    Debug.LogWarning($"[Save] {result.Error}");
                    return true;

                case SaveFileStore.ReadOutcome.Missing:
                    error = $"There is no save in slot '{slotId}'.";
                    return false;

                default:
                    error = result.Error ?? $"Save slot '{slotId}' could not be read.";
                    OnSaveError?.Invoke(error);
                    return false;
            }
        }

        /// <summary>Drops any staged document, so an abandoned load does not leak into the next session.</summary>
        public static void ClearStagedLoad() => PendingLoad = null;

        // ─────────────────────────────────────────────
        //  Streaming
        // ─────────────────────────────────────────────

        private void HandleChunkLoaded(Vector2Int coord, Scene scene)
        {
            if (Network.IsNetworked && !Network.Server) return;
            worldStore.Hydrate(SceneKey.ForChunk(coord), scene);
        }

        private void HandleChunkWillUnload(Vector2Int coord, Scene scene)
        {
            if (Network.IsNetworked && !Network.Server) return;
            worldStore.Dehydrate(SceneKey.ForChunk(coord), scene);
        }

        private void HandleChunkUnloaded(Vector2Int coord) =>
            worldStore.ForgetLoaded(SceneKey.ForChunk(coord));

        /// <summary>
        /// Records that an object is being removed from the world for good.
        ///
        /// Only matters for authored objects. A runtime object simply stops being captured once it
        /// no longer exists, but an authored one is written into its chunk scene and comes back on
        /// every load — so a looted crate needs an explicit tombstone or it refills itself the next
        /// time the player walks away and returns.
        ///
        /// Safe to call for anything; non-authored objects and objects outside the streamed world
        /// fall straight through.
        /// </summary>
        public static void NotifyDestroyed(GameObject target)
        {
            if (target == null) return;

            SaveManager manager = Instance;
            if (manager?.worldStore == null) return;

            var entity = target.GetComponent<SaveableEntity>();
            if (entity == null || !entity.IsAuthored || !entity.BelongsToWorld) return;

            if (!manager.worldStore.TryGetSceneKey(target.scene, out string sceneKey)) return;

            manager.worldStore.RecordDestroyed(sceneKey, entity);
        }

        // ─────────────────────────────────────────────
        //  Diagnostics
        // ─────────────────────────────────────────────

        private void Fail(string message)
        {
            Debug.LogError($"[Save] {message}", this);
            OnSaveError?.Invoke(message);
        }

        private void Log(string message)
        {
            if (verbose) Debug.Log($"[Save] {message}", this);
        }
    }
}
