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
        /// Whether the world's deferred pass has already run, so a scene hydrated afterwards knows it
        /// has to run its own rather than wait for a pass that has been and gone.
        /// </summary>
        private bool worldDeferredRan;

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

        /// <summary>
        /// The slot a save with no explicit target goes to: the active world.
        ///
        /// Falls back to the autosave slot only when no world has been chosen — a world scene
        /// opened directly in the editor, with no menu run behind it. That fallback is what keeps
        /// the existing editor workflow working; in normal play a world is always active.
        /// </summary>
        private static string DefaultSlotId =>
            WorldSession.IsActive ? WorldSession.WorldId : SaveSlots.AutoSaveSlotId;

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
            SaveDocument document = WorldSession.Consume();

            EnsureStores(document);

            loadedFromSave = document != null;
            sessionStartTime = Time.realtimeSinceStartup;
            inheritedPlaytime = document?.Header?.PlaytimeSeconds ?? 0d;

            if (document != null)
            {
                RestoreGlobals(document);
                Log($"Loaded save from {document.Header.SavedAtUtc:u} " +
                    $"({document.Players.Count} player record(s), {document.World.Count} entity record(s)).");
            }

            WorldStreamer.OnChunkLoaded += HandleChunkLoaded;
            WorldStreamer.OnChunkWillUnload += HandleChunkWillUnload;
            WorldStreamer.OnChunkUnloaded += HandleChunkUnloaded;
            worldStore.OnSceneHydrated += HandleSceneHydrated;
            playerService.PlayerBound += HandlePlayerBound;

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

            // Only the instance holding the session's state may answer SaveRefs. A duplicate
            // SaveManager (this project produces them during scene transitions) installing its own
            // empty binder would make every rider and target reference in the save unresolvable —
            // and unresolvable reads as "nobody was riding", which is silent, plausible data loss.
            if (Instance == this) SaveRefBinding.Active = new SaveRefBinder(playerService);
        }

        /// <summary>
        /// Writes the world's file the moment it is entered, before anything can go wrong.
        ///
        /// Without this a brand-new world exists only in memory until the first autosave 300 s
        /// later, so a player who creates a world, looks around and returns to the menu finds no
        /// world in the list — the session is simply gone. A save here also means the world shows
        /// up in the list immediately, which is what makes "I made a world" and "I have a world"
        /// the same statement.
        /// </summary>
        private void SaveNewWorld()
        {
            if (!WorldSession.IsActive || !WorldSession.IsNew) return;
            if (Network.IsNetworked && !Network.Server) return;

            // Not synchronous: nothing is waiting on it, and the world has only just loaded.
            Save(WorldSession.WorldId, WorldSession.DisplayName);
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

            // After hydration, so the file records the world as it actually is. Safe this early
            // even though no player has spawned yet: WouldDiscardAllPlayers only refuses when the
            // file already on disk has players, and a new world has no file at all.
            SaveNewWorld();
        }

        private void OnDestroy()
        {
            WorldStreamer.OnChunkLoaded -= HandleChunkLoaded;
            WorldStreamer.OnChunkWillUnload -= HandleChunkWillUnload;
            WorldStreamer.OnChunkUnloaded -= HandleChunkUnloaded;

            if (worldStore != null) worldStore.OnSceneHydrated -= HandleSceneHydrated;
            if (playerService != null) playerService.PlayerBound -= HandlePlayerBound;

            if (Instance != this) return;

            // Left installed, the binder would answer for a session that no longer exists and hand
            // out references into a torn-down world.
            if (SaveRefBinding.Active is SaveRefBinder) SaveRefBinding.Active = null;
            Instance = null;
        }

        private void Update()
        {
            if (autoSaveIntervalSeconds <= 0f) return;
            if (!Network.Server && Network.IsNetworked) return;
            if (Time.time < nextAutoSaveTime) return;

            nextAutoSaveTime = Time.time + autoSaveIntervalSeconds;
            Save(DefaultSlotId, "Autosave");
        }

        /// <summary>
        /// Writes the active world before the session is torn down.
        ///
        /// Returning to the main menu is not OnApplicationQuit — it shuts the network down and
        /// loads a scene — so without this the whole session since the last autosave is lost, and
        /// a world younger than the autosave interval is lost entirely. Synchronous because the
        /// scene load that follows destroys the stores this reads from.
        ///
        /// Safe to call from anywhere; does nothing when no world is active or this peer is not
        /// the server.
        /// </summary>
        public static void SaveOnExit()
        {
            SaveManager manager = Instance;
            if (manager == null) return;
            if (!WorldSession.IsActive) return;
            if (Network.IsNetworked && !Network.Server) return;

            manager.Save(WorldSession.WorldId, WorldSession.DisplayName, synchronous: true);
        }

        private void OnApplicationQuit()
        {
            if (!saveOnQuit) return;
            if (Network.IsNetworked && !Network.Server) return;

            // Synchronously. Unity gives the process no frames after this, so a background write
            // would be killed with it — which is the one moment a save is most needed.
            Save(DefaultSlotId, "Autosave", synchronous: true);
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

            // Before the event, not after. Anything listening to OnLoadApplied is entitled to see a
            // world whose mounts are mounted. Usually a no-op by now — HandlePlayerBound has already
            // run the pass — but a load with no restored player must not skip it.
            RunWorldDeferredPass();

            OnLoadApplied?.Invoke();
        }

        /// <summary>
        /// Runs the world's deferred pass as soon as the first player exists.
        ///
        /// The precondition every deferred world saver is waiting on is "a player is here to be
        /// referenced", and that is what binding means — not that the player had a record to restore.
        /// Keying this off a successful restore instead (which is what <see cref="NotifyLoadApplied"/>
        /// reports) would leave a client joining a saved world for the first time in a world whose
        /// mounts never re-seat anyone: the host's own mount reference resolves to a profile that is
        /// here, but nothing ever fires the pass that would look.
        ///
        /// Run on EVERY bind, not only the first. In multiplayer the players arrive one at a time, and a
        /// pass that fired once would resolve the first player's mount and permanently give up on the
        /// second's. Savers that resolved on an earlier pass have consumed their state and do nothing;
        /// a saver still waiting on an absent player gets another chance each time somebody arrives.
        /// </summary>
        private void HandlePlayerBound(string profileId, GameObject player)
        {
            if (!loadedFromSave) return;

            RunWorldDeferredPass();
        }

        /// <summary>
        /// Runs the deferred pass for every world entity: the second chance a saver gets once the
        /// whole world exists.
        ///
        /// <b>Why world entities need this at all.</b> <see cref="IDeferredSaveable"/> was reachable
        /// only through <see cref="PlayerSaveService.Bind"/>, so it served players and nothing else.
        /// But the savers that need it most are on world objects: a mount restored during hydration
        /// cannot re-seat its rider, because Netcode spawns players at a time this system does not
        /// control and the rider does not exist yet. Every <see cref="SaveRef"/> resolves here.
        ///
        /// Entities are copied out first — a deferred saver may mount, spawn or destroy, and any of
        /// those mutates the registry being walked.
        /// </summary>
        private void RunWorldDeferredPass()
        {
            worldDeferredRan = true;

            var entities = new List<SaveableEntity>(SaveableEntity.LiveEntities.Values);

            foreach (SaveableEntity entity in entities)
            {
                // Players are served by PlayerSaveService.Bind, which has already run by now; running
                // them again here would re-apply a restore on top of live state.
                if (entity == null || !entity.BelongsToWorld) continue;

                entity.NotifyLoadComplete();
            }
        }

        /// <summary>
        /// Gives a late-arriving scene its deferred pass.
        ///
        /// Chunks stream in for as long as the session lasts, so entities keep appearing after
        /// <see cref="RunWorldDeferredPass"/> has already fired. Without this, a mount restored by a
        /// chunk that loaded a minute into the session would hold a rider reference it never resolves —
        /// persistence that works only for the chunks that happened to be resident at load.
        /// </summary>
        private void HandleSceneHydrated(string sceneKey, Scene scene)
        {
            if (!worldDeferredRan) return;

            foreach (SaveableEntity entity in new List<SaveableEntity>(WorldSaveStore.EntitiesIn(scene)))
            {
                if (entity == null || !entity.BelongsToWorld) continue;
                entity.NotifyLoadComplete();
            }
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
            int entities = document?.World?.Count ?? 0;

            Debug.Log($"[Save] Wrote '{slotId}' — {players} player(s), {entities} entity record(s) → {path}", this);
        }

        /// <summary>
        /// Writes to the active world.
        ///
        /// Not to a separate quicksave file: with more than one world a global quicksave slot is
        /// silently cross-world — F5 in one world and F9 in another would load the wrong session.
        /// </summary>
        public bool QuickSave() => Save(DefaultSlotId, "Quicksave");

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

            var document = new SaveDocument
            {
                Header = new SaveHeader
                {
                    Version = SaveDocument.CurrentVersion,
                    SavedAtUtc = DateTime.UtcNow,
                    PlaytimeSeconds = TotalPlaytime,
                    GameVersion = Application.version,
                    SlotLabel = string.IsNullOrEmpty(label) ? slotId : label,
                    WorldName = WorldSession.IsActive ? WorldSession.DisplayName : slotId,
                    WorldConfigId = WorldSession.WorldConfigId ?? string.Empty,
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

        // Staging a load lives on WorldSession: what to load is a property of which world is being
        // entered, and keeping it here meant "new game" could only be expressed as the absence of
        // a staged document.

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
        /// Safe to call for anything; non-authored objects fall straight through.
        ///
        /// No longer asks which scene the object was in. The tombstone is keyed by the object's own
        /// identity, so a creature killed in a chunk it had wandered into stays dead when the scene
        /// it was authored in loads it again — and an object whose scene the store had not been told
        /// about is no longer silently un-buriable.
        /// </summary>
        public static void NotifyDestroyed(GameObject target)
        {
            if (target == null) return;

            SaveManager manager = Instance;
            if (manager?.worldStore == null) return;

            var entity = target.GetComponent<SaveableEntity>();
            if (entity == null || !entity.IsAuthored || !entity.BelongsToWorld) return;

            manager.worldStore.RecordDestroyed(entity);
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
