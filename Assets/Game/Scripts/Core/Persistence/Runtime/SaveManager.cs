using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
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
    /// <remarks>
    /// <b>The quit-save must see the session, not its wreckage.</b> NetworkManager tears the
    /// session down on quit as well, and a save taken after that teardown finds every Rigidbody
    /// already made kinematic (no momentum), every remote player already unbound, and every
    /// dynamically spawned NetworkObject destroyed — which the world store reads as the player
    /// having destroyed them. The execution order below puts this class's <c>OnApplicationQuit</c>
    /// ahead of NetworkManager's in a build, but it is NOT enough in the editor: Netcode runs its
    /// whole shutdown from its own <c>playModeStateChanged</c> hook at ExitingPlayMode, before Unity
    /// delivers <c>OnApplicationQuit</c> to anyone. So the world is captured and sealed at
    /// <c>NetworkManager.OnPreShutdown</c> instead — see <see cref="HandleNetworkShuttingDown"/> —
    /// and the quit-save then writes that capture rather than redoing it.
    ///
    /// The execution order also puts <c>Awake</c> ahead of the rest of the scene, which this class
    /// already depended on informally: both stores must be holding the loaded document before the
    /// first chunk hydrates or a player spawns.
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
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
        /// Fired immediately before a save captures the world, on the frame of the capture.
        ///
        /// <para>
        /// The hook for anything that wants the world to be WORTH capturing first: a system
        /// mid-sequence can finish or normalise what it is doing so the file records an end state
        /// rather than a moment nothing will ever resume — <c>ArrivalDirector</c> grounds a
        /// mid-descent hull here, because the arrival flag is saved as "arrived" either way.
        /// Handlers run synchronously and must not save.
        /// </para>
        /// </summary>
        public static event Action Capturing;

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

        /// <summary>
        /// Whether a background write is still in flight.
        ///
        /// Exposed so a hotkey can tell the player "a save is already being written" rather than the
        /// generic "nothing happened" — the one refusal that is transient and worth waiting out.
        /// </summary>
        public static bool IsWriting => Instance?.activeWrite is { IsCompleted: false };
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
        /// The slot a save with no explicit target goes to: the active world, and nothing else.
        ///
        /// There is deliberately no fallback. Saving with no world chosen used to write an
        /// "autosave" file, and since the world list is every save on disk, that file then showed
        /// up as a world nobody made — one whose name, config id and contents all came from
        /// whatever scene happened to be open in the editor at the time.
        ///
        /// The cost is that a world scene played directly from the editor no longer saves at all;
        /// <see cref="Save"/> refuses and says so. Entering through the main menu is the only way
        /// to get a world, which is also the only way a save has an identity worth writing.
        /// </summary>
        private static string DefaultSlotId => WorldSession.IsActive ? WorldSession.WorldId : null;

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

            // Interiors are hydrated exactly like chunks, and were not hydrated at all before.
            // WorldSaveStore.Hydrate had two callers — the persistent scene and chunks — so every
            // cave in the game was invisible to the save system: a looted container, a killed
            // creature, a moved crate inside one simply did not exist as far as a save was
            // concerned. InteriorManager now raises the same three events WorldStreamer does.
            InteriorManager.OnInteriorLoaded += HandleInteriorLoaded;
            InteriorManager.OnInteriorWillUnload += HandleInteriorWillUnload;
            InteriorManager.OnInteriorUnloaded += HandleInteriorUnloaded;
            worldStore.OnSceneHydrated += HandleSceneHydrated;
            playerService.PlayerBound += HandlePlayerBound;

            // The last moment the world is intact before Netcode destroys its spawned objects. An
            // offline editor session has no NetworkManager and no teardown, so nothing to hook.
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnPreShutdown += HandleNetworkShuttingDown;

            nextAutoSaveTime = Time.time + Mathf.Max(1f, autoSaveIntervalSeconds);
        }

        /// <summary>
        /// Takes the session's final capture the moment Netcode starts shutting down, and seals the
        /// world store behind it.
        ///
        /// <para>
        /// <c>NetworkManager.OnPreShutdown</c> fires at the top of <c>ShutdownInternal</c>; what
        /// follows it is <c>DespawnAndDestroyNetworkObjects</c>, which destroys every dynamically
        /// spawned NetworkObject. In the editor that whole shutdown runs from Netcode's own
        /// <c>playModeStateChanged</c> hook at ExitingPlayMode — BEFORE Unity delivers
        /// <c>OnApplicationQuit</c> to anyone, whatever their execution order — so the quit-save
        /// captured a world whose runtime-spawned objects were already gone, and
        /// <c>WorldSaveStore.DropVanishedRuntime</c> read their absence as destruction. The player
        /// ship, the one runtime-spawned object in the persistent scene, vanished from the file on
        /// every Stop with nothing logged; a timer autosave or a pause-menu exit kept it, which is
        /// what made the loss look intermittent.
        /// </para>
        /// <para>
        /// The capture is the same one a save takes; the seal is what makes the save that follows
        /// write it rather than redo it. Players are left alone: <c>PlayerSaveSync</c> captures each
        /// one as it despawns, and that path is shared with an ordinary disconnect.
        /// </para>
        /// </summary>
        private void HandleNetworkShuttingDown()
        {
            if (Instance != this) return;
            if (worldStore == null || worldStore.IsSealed) return;
            if (!WorldSession.IsActive) return;
            if (Network.IsNetworked && !Network.Server) return;

            try
            {
                Capturing?.Invoke();
                CaptureLoadedScenes();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] The final capture before network shutdown failed: {e}", this);
            }

            // Sealed even after a failed capture: whatever the records hold is closer to the
            // session than what a capture over a torn-down world would replace them with.
            worldStore.Seal();
            Log("Network shutting down: world records captured and sealed.");
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

            InteriorManager.OnInteriorLoaded -= HandleInteriorLoaded;
            InteriorManager.OnInteriorWillUnload -= HandleInteriorWillUnload;
            InteriorManager.OnInteriorUnloaded -= HandleInteriorUnloaded;

            if (worldStore != null) worldStore.OnSceneHydrated -= HandleSceneHydrated;
            if (playerService != null) playerService.PlayerBound -= HandlePlayerBound;

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnPreShutdown -= HandleNetworkShuttingDown;

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

            // No world, no slot. Silent rather than warned: this fires on a timer, so a scene
            // played straight from the editor would otherwise log every interval forever.
            if (!WorldSession.IsActive) return;

            if (Time.time < nextAutoSaveTime) return;

            // Scheduled from the OUTCOME, not before the attempt. Advancing the clock first meant a
            // refusal — an in-flight write, a capture that found no players, a serialization throw —
            // cost the player another full interval before anything tried again. A refused autosave
            // now retries shortly rather than at the back of a five-minute queue.
            bool wrote = Save(DefaultSlotId, "Autosave");

            nextAutoSaveTime = Time.time + (wrote
                ? autoSaveIntervalSeconds
                : Mathf.Min(15f, autoSaveIntervalSeconds));
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
            if (!WorldSession.IsActive) return;

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

            // Refusals are WARNINGS, not verbose logs.
            //
            // Every one of them means a save the player believes happened did not, and routing them
            // through Log() put them behind a `verbose` flag that is off — which is how a quit-save
            // could stop working for two sessions running without anyone noticing. The one refusal
            // that stays quiet is the autosave timer's, below, because it fires on a schedule.
            if (string.IsNullOrEmpty(slotId))
            {
                // Not merely tidy: SaveSlots.Sanitize turns an empty id into "save", so falling
                // through here would quietly invent a save.json — exactly the stray slot this
                // refusal exists to prevent.
                Refuse("no world is active, so there is no slot to write. Enter a world through " +
                       "the main menu — a world scene opened directly in the editor cannot save.");
                return false;
            }

            if (Network.IsNetworked && !Network.Server)
            {
                Refuse("only the server owns world state, so this peer has nothing to write.");
                return false;
            }

            // A save already in flight blocks another ASYNCHRONOUS one, and nothing more.
            //
            // This check used to sit above the synchronous branch, where it was the single worst
            // bug in the system: OnApplicationQuit and SaveOnExit both pass synchronous: true
            // precisely because Unity gives the process no more frames — and both returned false
            // and wrote nothing whenever a background autosave happened to still be writing. An
            // alt-F4 landing in that window lost the whole session since the last autosave, and
            // said so only through a Log() that is gated on `verbose`.
            //
            // A synchronous save now waits the background write out instead of standing down. That
            // is safe because the two write to the same file through SaveFileStore's temp-and-
            // replace, so the only thing being serialised here is the order they land in.
            if (activeWrite is { IsCompleted: false })
            {
                if (!synchronous)
                {
                    Log("Save ignored: the previous write has not finished.");
                    return false;
                }

                try
                {
                    // Bounded: a save we cannot finish is worth failing, but a hang here would take
                    // the whole quit with it.
                    if (!activeWrite.Wait(TimeSpan.FromSeconds(5)))
                        Debug.LogWarning("[Save] The previous write did not finish in time; writing " +
                                         "over it anyway rather than losing this save.", this);
                }
                catch (Exception e)
                {
                    // The previous write faulted. That is its problem, not this save's.
                    Debug.LogWarning($"[Save] The previous write failed ({e.GetBaseException().Message}); " +
                                     "continuing with this save.", this);
                }
            }

            SaveDocument document;
            string json;

            try
            {
                Capturing?.Invoke();
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

            if (SaveFileStore.WouldDowngradeFormat(path, document))
            {
                Fail($"Refused to save '{slotId}': the file already there was written by a NEWER " +
                     "version of the game than this one. Overwriting it would discard whatever that " +
                     "version stored and could not be read back. Run the newer build, or delete the " +
                     "save deliberately.");
                return false;
            }

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
                    SaveFileStore.NotifyWritten(path, document);
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

            // A save happened, so any standing explanation of why one did not is stale.
            LastRefusal = null;

            int held = worldStore?.UnresolvedCount ?? 0;
            string heldNote = held > 0
                ? $", {held} record(s) held back for prefabs that could not be resolved"
                : string.Empty;

            Debug.Log($"[Save] Wrote '{slotId}' — {players} player(s), {entities} entity record(s)" +
                      $"{heldNote} → {path}", this);
        }

        /// <summary>
        /// Writes to the active world.
        ///
        /// Not to a separate quicksave file: with more than one world a global quicksave slot is
        /// silently cross-world — F5 in one world and F9 in another would load the wrong session.
        /// </summary>
        public bool QuickSave()
        {
            // Warned rather than silent, unlike the autosave timer: this one was asked for by a
            // keypress, so the player has to be told why nothing happened.
            if (!WorldSession.IsActive)
            {
                Debug.LogWarning("[Save] Nothing to quicksave: no world is active. " +
                                 "Enter a world through the main menu.", this);
                return false;
            }

            return Save(DefaultSlotId, "Quicksave");
        }

        private void CompleteWrite(Task task, string slotId, string path, SaveDocument document)
        {
            if (task.IsFaulted)
            {
                Exception error = task.Exception?.GetBaseException();
                Fail($"Could not write the save: {error?.Message}");
                if (error != null) Debug.LogException(error, this);
                return;
            }

            SaveFileStore.NotifyWritten(path, document);
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

            // After the captures, never before: compaction removes records that name nothing, and a
            // record refreshed this pass is by definition not one of those. Running it here means
            // the file written is the compacted one rather than the compaction landing a save later.
            worldStore.Compact();
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

        // ─────────────────────────────────────────────
        //  Interiors
        // ─────────────────────────────────────────────
        //
        // Identical in shape to the chunk hooks, and deliberately so: an interior is a scene that
        // comes and goes around a player, which is the same problem streaming already solves. The
        // only difference is the key — SceneKey.ForScene rather than ForChunk — because an interior
        // is named rather than gridded.

        private void HandleInteriorLoaded(string sceneName, Scene scene)
        {
            if (Network.IsNetworked && !Network.Server) return;
            worldStore.Hydrate(SceneKey.ForScene(sceneName), scene);
        }

        private void HandleInteriorWillUnload(string sceneName, Scene scene)
        {
            if (Network.IsNetworked && !Network.Server) return;
            worldStore.Dehydrate(SceneKey.ForScene(sceneName), scene);
        }

        private void HandleInteriorUnloaded(string sceneName) =>
            worldStore.ForgetLoaded(SceneKey.ForScene(sceneName));

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
            LastRefusal = message;
            Debug.LogError($"[Save] {message}", this);
            OnSaveError?.Invoke(message);
        }

        private void Log(string message)
        {
            if (verbose) Debug.Log($"[Save] {message}", this);
        }

        /// <summary>
        /// Reports a save that was asked for and did not happen.
        ///
        /// Always visible, and recorded on <see cref="LastRefusal"/> so a hotkey or a UI can tell
        /// the player rather than leaving them to guess. Refusals used to go through
        /// <see cref="Log"/>, which is behind a <c>verbose</c> flag that ships off — and a save that
        /// silently does nothing is worse than one that fails loudly, because the player finds out
        /// at the wrong end.
        /// </summary>
        private void Refuse(string reason)
        {
            LastRefusal = reason;
            Debug.LogWarning($"[Save] Save ignored: {reason}", this);
            OnSaveError?.Invoke(reason);
        }

        /// <summary>Why the most recent save did not happen, or null if the last one did.</summary>
        public static string LastRefusal { get; private set; }
    }
}
