// Drives one end of a two-process multiplayer test from the command line.
//
// This exists because the client half of the netcode cannot be tested any other way. Running two
// NetworkManagers in one process — which is how Netcode's own integration tests work — is useless
// here, because this codebase asks NetworkManager.Singleton who it is; a second manager in the same
// process is invisible to Network.IsNetworked/Simulates/Owns, so the test would exercise Netcode
// rather than the game. A real client has to be a real second process.
//
// Inert unless -sgmode is on the command line, so it costs a shipped build nothing but this check.
//
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode host   -logFile host.log
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode client -logFile client.log
//
// Each side prints [MPTEST] key=value lines. The caller asserts across both logs — a fact only one
// side can observe (a client's view of health the server changed) is only meaningful when read
// against the other side's report of what it did.
//
// There is a third mode, `persist`, which runs alone and asks the other half of this project's
// non-negotiables: that a feature survives save, quit and load. It has no peer because none of what
// it asks is a question about one.
//
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode persist -logFile persist.log
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    public static class MultiplayerAutotest
    {
        private const string WorldScene = "persistentScene";
        private const ushort Port = 7897;
        private const float StepTimeout = 120f;

        /// <summary>The net gun, by its Resources path. The only artifact this test fires.</summary>
        private const string NetGunPath = "Items/Artifacts/NetGun";

        /// <summary>
        /// Longest the client waits for a net the host said it fired.
        ///
        /// Generous, and deliberately not a <c>WaitFor</c> deadline: "no net ever arrived" is the
        /// answer this step exists to catch, so it has to be reported rather than end the run.
        /// </summary>
        private const float NetWaitSeconds = 45f;

        /// <summary>The world the save/load run creates for itself. Its own slot, never a player's.</summary>
        private const string PersistWorldName = "mptest";

        // Counts relay traffic arriving from the other process. Static because the listener is
        // registered against a networked object that may be replaced during the run.
        private static int relayFromPeer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string mode = Arg("-sgmode");
            if (string.IsNullOrEmpty(mode)) return;

            var runner = new GameObject("[MultiplayerAutotest]");
            Object.DontDestroyOnLoad(runner);
            runner.AddComponent<AutotestRunner>().Begin(mode);
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];

            return null;
        }

        private static void Report(string key, object value) =>
            Debug.Log($"[MPTEST] {key}={value}");

        /// <summary>
        /// How many portal apertures stand on this machine.
        ///
        /// Counted by finding them rather than asked of a pair, because the question is what this
        /// MACHINE has: a client that never heard about a shooter's portals has no PortalPair to
        /// ask in the first place, which is exactly the state being tested for.
        /// </summary>
        private static int CountPortals() =>
            Object.FindObjectsByType<SpaceGame.Portals.Portal>(FindObjectsSortMode.None).Length;

        /// <summary>
        /// How many nets stand on this machine, and how many bodies they hold between them.
        ///
        /// Found rather than asked of a shooter's <c>SnareReceiver</c>, for the reason
        /// <see cref="CountPortals"/> gives: a machine that was never told about a net has no
        /// receiver entry to ask, which is precisely the state under test. A net that exists but
        /// holds nothing is the other half of the same failure, so both numbers come out together.
        /// </summary>
        private static int CountNets(out int captives)
        {
            SnareCatch[] nets = Object.FindObjectsByType<SnareCatch>(FindObjectsSortMode.None);

            captives = 0;
            foreach (SnareCatch net in nets) captives += net.Captives.Count;

            return nets.Length;
        }

        /// <summary>
        /// Whether a net is holding this creature.
        ///
        /// Asked of the children as well as the root because the two machines can bind different
        /// objects: the server captures whatever collider its <c>OverlapBox</c> returned, while a
        /// peer is told a NetworkObjectId and <c>NetArg.Resolve</c> only ever answers with the
        /// spawned ROOT. Both end up hobbling the same NavMeshAgent, and both count as bound.
        /// </summary>
        private static bool IsNetted(GameObject creature)
        {
            if (creature == null) return false;

            SnareTether tether = creature.GetComponentInChildren<SnareTether>();
            SnaredBody snared = creature.GetComponentInChildren<SnaredBody>();

            return (tether != null && tether.IsBound) || (snared != null && snared.IsBound);
        }

        /// <summary>The MonoBehaviour half — coroutines need one.</summary>
        private class AutotestRunner : MonoBehaviour
        {
            public void Begin(string mode)
            {
                StartCoroutine(mode switch
                {
                    "host" => RunHost(),
                    "persist" => RunPersistence(),
                    _ => RunClient(),
                });
            }

            // ─────────── Host ───────────

            private IEnumerator RunHost()
            {
                yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");

                SessionResult started = SessionLauncher.HostDirect(Port);
                Report("HOST_STARTED", started.Success);
                if (!started.Success)
                {
                    Report("HOST_ERROR", started.Error);
                    Finish();
                    yield break;
                }

                // Through Netcode's scene manager, exactly as MainMenuUI does, so the client is
                // pulled into the same scene rather than loading one of its own.
                NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == WorldScene, "world scene");

                yield return WaitFor(() => NetworkManager.Singleton.SpawnManager.SpawnedObjects.Count > 0, "spawned objects");
                Report("HOST_SPAWNED", NetworkManager.Singleton.SpawnManager.SpawnedObjects.Count);

                // Listen for the client's relay message before it can arrive.
                NetRelay channel = LowestIdRelay();
                if (channel != null)
                {
                    channel.NetOn(NetMsg.Damage, CountRelayFromPeer);
                    Report("HOST_RELAY_LISTENING_ON", channel.name);
                }

                yield return WaitFor(() => NetworkManager.Singleton.ConnectedClientsIds.Count > 1, "a client to connect");
                Report("HOST_CLIENTS", NetworkManager.Singleton.ConnectedClientsIds.Count);

                // Let the client finish syncing the scene before anything is changed under it.
                yield return new WaitForSeconds(8f);

                // The subject of the health test, chosen by a rule both processes can apply
                // independently — passing the id on a command line is impossible when only the
                // running host knows it, and names repeat ("DuneRat" twice in persistentScene).
                HealthComponent victim = FindNetworkedVictim(out ulong victimId);
                if (victim == null)
                {
                    Report("HOST_VICTIM", "none");
                }
                else
                {
                    Report("HOST_VICTIM_ID", victimId);
                    Report("HOST_VICTIM_NAME", victim.name);
                    Report("HOST_HEALTH_BEFORE", victim.GetHealth);

                    NetDamage.Apply(victim.gameObject, 11);
                    Report("HOST_HEALTH_AFTER", victim.GetHealth);
                }

                // Give the client time to send its relay message and read the replicated health.
                yield return new WaitForSeconds(12f);
                Report("HOST_RELAY_FROM_CLIENT", relayFromPeer);

                // The counterpart of CLIENT_LEASHES_SEEN / CLIENT_PORTALS_SEEN. Ropes and portal
                // apertures are not spawned NetworkObjects — every machine builds its own from a
                // message — so nothing in SpawnManager can speak for them, and until SessionSnapshot
                // existed a joining client got neither. The two numbers must match.
                Report("HOST_LEASHES", SpaceGame.Items.Leash.All.Count);
                Report("HOST_PORTALS", CountPortals());

                yield return FireNetGunAtQuarry();

                // The client reads its own net count after the host has fired, and cannot do that
                // once the host has taken the session down with it.
                yield return new WaitForSeconds(12f);

                Report("HOST_DONE", true);
                Finish();
            }

            // ─────────── The net gun ───────────

            /// <summary>
            /// Put a net gun in the host's hands and fire it at a creature.
            ///
            /// <para>
            /// The shot goes through <c>EquipmentController.UseHeldItem</c>, which is
            /// <c>OnUse</c> — the same request/present/hop-to-server path a button press takes. Only
            /// the binding from the Use action to that method is left out, because a C# event
            /// cannot be raised from outside the class that declares it.
            /// </para>
            /// <para>
            /// The creature is MOVED to where the shot comes down rather than aimed at, because the
            /// aim is not this test's to choose: it comes out of the muzzle on the end of an
            /// animated arm. Asking the gun where its next shot would land and putting the quarry
            /// there tests the same thing a player walking into range tests, and it does not depend
            /// on the hold pose happening to point the barrel anywhere in particular.
            /// </para>
            /// </summary>
            private IEnumerator FireNetGunAtQuarry()
            {
                GameObject shooter = LocalPlayerObject();
                AgentController quarry = FindNetworkedQuarry(out ulong quarryId);

                if (shooter == null || quarry == null)
                {
                    Report("HOST_NETGUN", shooter == null ? "no player object" : "no creature to net");
                    yield break;
                }

                Report("HOST_QUARRY_ID", quarryId);
                Report("HOST_QUARRY_NAME", quarry.name);

                var gun = Resources.Load<InventoryItem>(NetGunPath);
                var inventory = shooter.GetComponent<IPlayerInventory>();
                var equipment = shooter.GetComponent<EquipmentController>();

                if (gun == null || inventory == null || equipment == null)
                {
                    Report("HOST_NETGUN", gun == null ? "item asset missing" : "player has no inventory");
                    yield break;
                }

                inventory.TryAddItem(gun);
                yield return WaitAtMost(() => SlotHolding(inventory, gun) >= 0, 10f);

                int slot = SlotHolding(inventory, gun);
                Report("HOST_NETGUN_SLOT", slot);
                if (slot < 0) yield break;

                // Only when it is not already the selection: SelectSlot TOGGLES, so asking for the
                // slot that is already held puts the gun away instead of taking it out.
                if (inventory.SelectedSlotIndex != slot) inventory.SelectSlot(slot);
                yield return WaitAtMost(() => equipment.HeldUsable is NetGunArtifact, 10f);

                var netGun = equipment.HeldUsable as NetGunArtifact;
                Report("HOST_NETGUN_EQUIPPED", netGun != null);
                if (netGun == null) yield break;

                Report("HOST_NETGUN_CHARGES", netGun.ChargesRemaining);

                Vector3 landing = GroundUnder(PredictedLanding(netGun));
                Persistence.SaveTeleport.Move(quarry.gameObject, landing, quarry.transform.rotation);

                Report("HOST_NETGUN_MUZZLE_TO_QUARRY",
                       Vector3.Distance(shooter.transform.position, quarry.transform.position).ToString("F1"));

                equipment.UseHeldItem();
                Report("HOST_NETGUN_FIRED", true);

                // The net flies for MaxFlightSeconds and the capture pass runs on the first frame
                // after that, so this is the flight plus room for the drape to settle.
                yield return WaitAtMost(() => CountNets(out int held) > 0 && held > 0, 10f);

                int nets = CountNets(out int captives);
                Report("HOST_NETS", nets);
                Report("HOST_NET_CAPTIVES", captives);
                Report("HOST_QUARRY_BOUND", IsNetted(quarry.gameObject));
                Report("HOST_NETGUN_CHARGES_AFTER", netGun.ChargesRemaining);

                // Only interesting when nothing was caught, and then it is the whole story: it says
                // whether the net missed or whether the capture pass refused what it found.
                if (captives == 0) Report("HOST_NET_MISS_BY", NetToQuarryDistance(quarry).ToString("F1"));
            }

            // ─────────── Client ───────────

            private IEnumerator RunClient()
            {
                yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");

                // The host needs a head start; a refused connection is not a failure worth reporting.
                yield return new WaitForSeconds(6f);

                Task<SessionResult> join = SessionLauncher.JoinDirectAsync("127.0.0.1", Port);
                yield return WaitFor(() => join.IsCompleted, "join to complete");

                SessionResult result = join.Result;
                Report("CLIENT_JOINED", result.Success);
                if (!result.Success)
                {
                    Report("CLIENT_ERROR", result.Error);
                    Finish();
                    yield break;
                }

                yield return WaitFor(() => NetworkManager.Singleton.IsConnectedClient, "connection");
                Report("CLIENT_CONNECTED", NetworkManager.Singleton.IsConnectedClient);
                Report("CLIENT_IS_SERVER", NetworkManager.Singleton.IsServer);

                yield return WaitFor(() => SceneManager.GetActiveScene().name == WorldScene, "world scene");
                yield return WaitFor(() => NetworkManager.Singleton.SpawnManager.SpawnedObjects.Count > 0, "replicated objects");
                yield return new WaitForSeconds(8f);

                Report("CLIENT_SPAWNED", NetworkManager.Singleton.SpawnManager.SpawnedObjects.Count);

                // THE question this whole process exists to answer: on a machine that owns nothing,
                // does NetAuthority actually stop the entity simulating itself?
                int authorities = 0, suppressed = 0, driversDisabled = 0, driversTotal = 0;
                foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    NetworkObject netObj = pair.Value;
                    if (netObj == null || netObj.GetComponent<NetAuthority>() == null) continue;

                    authorities++;
                    if (!netObj.GetComponent<NetAuthority>().IsSimulatedHere) suppressed++;

                    foreach (Behaviour driver in NetAuthority.Discover(netObj.gameObject))
                    {
                        if (driver == null) continue;
                        driversTotal++;
                        if (!driver.enabled) driversDisabled++;
                    }
                }

                Report("CLIENT_AUTHORITIES", authorities);
                Report("CLIENT_SUPPRESSED", suppressed);
                Report("CLIENT_DRIVERS_TOTAL", driversTotal);
                Report("CLIENT_DRIVERS_DISABLED", driversDisabled);
                Report("CLIENT_PLAYER_OBJECT", NetworkManager.Singleton.LocalClient.PlayerObject != null);

                // Health the SERVER changed, read here. Nothing local produced this number — the
                // same selection rule runs on both sides, so both land on the same entity.
                HealthComponent health = FindNetworkedVictim(out ulong victimId);
                if (health == null)
                {
                    Report("CLIENT_VICTIM", "none");
                }
                else
                {
                    Report("CLIENT_VICTIM_ID", victimId);
                    Report("CLIENT_VICTIM_NAME", health.name);
                    Report("CLIENT_HEALTH_SEEN", health.GetHealth);
                }

                // Client → server over the real wire, on an object the client does not own.
                // A = 0 so NetworkedHealthComponent's own handler ignores it as a damage request;
                // the point is only that the message crosses, which the host counts.
                NetRelay channel = LowestIdRelay();
                if (channel != null)
                {
                    channel.NetToServer(NetMsg.Damage, new NetArg { A = 0, B = 31337 });
                    Report("CLIENT_RELAY_SENT_ON", channel.name);
                }

                yield return new WaitForSeconds(6f);

                // What a joiner was never told about. Neither a rope nor a portal aperture is a
                // spawned NetworkObject — every machine builds its own copy from a message it had
                // to be present for — so before SessionSnapshot a client that joined after the
                // event had none of either, and no way to ever learn. Compared against
                // HOST_LEASHES / HOST_PORTALS.
                Report("CLIENT_LEASHES_SEEN", SpaceGame.Items.Leash.All.Count);
                Report("CLIENT_PORTALS_SEEN", CountPortals());

                // The net gun, and the reason the whole two-process apparatus exists for it. A net
                // is not a spawned NetworkObject: every machine draws its own from the origin, aim
                // and seed that came with the press, and is then TOLD by the server what that net
                // caught. So a client seeing no net means the shot never crossed, and a client
                // seeing a net that holds nothing means the catch never did — two different
                // failures that both look like a working feature on the host.
                yield return WaitAtMost(() => CountNets(out int held) > 0 && held > 0, NetWaitSeconds);

                int nets = CountNets(out int captives);
                Report("CLIENT_NETS_SEEN", nets);
                Report("CLIENT_NET_CAPTIVES", captives);

                AgentController quarry = FindNetworkedQuarry(out ulong quarryId);
                if (quarry == null)
                {
                    Report("CLIENT_QUARRY", "none");
                }
                else
                {
                    Report("CLIENT_QUARRY_ID", quarryId);
                    Report("CLIENT_QUARRY_NAME", quarry.name);
                    Report("CLIENT_QUARRY_BOUND", IsNetted(quarry.gameObject));
                }

                Report("CLIENT_DONE", true);
                Finish();
            }

            // ─────────── Save and load ───────────

            /// <summary>
            /// One process, host of one, netting a creature and then saving and reloading the world.
            ///
            /// <para>
            /// Alone rather than alongside a client, because none of what it asks is a question
            /// about a peer. Being netted is deliberately NOT persisted — thirty seconds of state is
            /// not worth a record — so what has to hold is the pair of things that would break
            /// silently if it were persisted by accident: the creature comes back FREE and able to
            /// move, and the gun comes back with the charges it had actually spent.
            /// </para>
            /// <para>
            /// The charge count is read the moment the gun is back in the hand rather than at the
            /// end. It refills on a timer while held, so a report taken a few seconds later says
            /// nothing about what was loaded.
            /// </para>
            /// </summary>
            private IEnumerator RunPersistence()
            {
                yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");

                // A world has to be ACTIVE before anything can be written: SaveManager saves to
                // WorldSession.WorldId and refuses outright when there is none, which is what a
                // world scene opened straight from the editor gets. This is what the main menu's
                // "create world" does, with no config because this build has no chunk scenes to
                // belong to — an empty id on a save is accepted by WorldIdentity.AcceptsConfig.
                Persistence.WorldSession.StageNew(PersistWorldName, null);

                SessionResult started = SessionLauncher.HostDirect(Port);
                Report("PERSIST_STARTED", started.Success);
                if (!started.Success)
                {
                    Report("PERSIST_ERROR", started.Error);
                    Finish();
                    yield break;
                }

                NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == WorldScene, "world scene");
                yield return WaitFor(() => LocalPlayerObject() != null, "a player");
                yield return new WaitForSeconds(6f);

                // The creature's own numbers before a net has touched it. Everything after the load
                // is compared against these rather than against a guess: a leaked hobble is only
                // visible as a speed that does not come back to what the creature authored.
                AgentController before = FindNetworkedQuarry(out _);
                Report("PERSIST_QUARRY_SPEED_AUTHORED", NavSpeedOf(before));
                Report("PERSIST_QUARRY_DRIVERS_AUTHORED", DriversEnabled(before));

                yield return FireNetGunAtQuarry();

                var equipment = LocalPlayerObject() != null
                    ? LocalPlayerObject().GetComponent<EquipmentController>()
                    : null;

                var gun = equipment != null ? equipment.HeldUsable as NetGunArtifact : null;
                if (gun == null)
                {
                    Report("PERSIST_GUN", "not in hand");
                    Finish();
                    yield break;
                }

                // Twice, so the loaded count can only be right by being loaded: one shot leaves a
                // number that a fresh gun with an off-by-one would also produce.
                equipment.UseHeldItem();
                yield return new WaitForSeconds(2f);

                AgentController quarry = FindNetworkedQuarry(out _);
                Report("PERSIST_CHARGES_BEFORE_SAVE", gun.ChargesRemaining);
                Report("PERSIST_QUARRY_BOUND_BEFORE_SAVE", IsNetted(quarry != null ? quarry.gameObject : null));
                Report("PERSIST_QUARRY_SPEED_BEFORE_SAVE", NavSpeedOf(quarry));

                string worldId = Persistence.WorldSession.WorldId;
                Persistence.SaveManager manager = Persistence.SaveManager.Instance;
                if (manager == null)
                {
                    Report("PERSIST_SAVE", "no SaveManager in the world scene");
                    Finish();
                    yield break;
                }

                Report("PERSIST_SAVED", manager.Save(worldId, "MPTest", synchronous: true));
                Report("PERSIST_SAVE_PATH", manager.Slots.PathFor(worldId));

                if (!Persistence.WorldSession.StageExisting(worldId, null, out string error))
                {
                    Report("PERSIST_STAGE_ERROR", error);
                    Finish();
                    yield break;
                }

                NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);

                // Long enough for the old scene to actually go. Asking for the player straight away
                // finds the one that has not been despawned yet, and reads the charge count off the
                // gun that is about to be destroyed rather than off the one the save produced.
                yield return new WaitForSeconds(4f);
                yield return WaitFor(() => LocalPlayerObject() != null, "a player after the load");

                // Read before the recharge clock can reach twelve seconds. See the summary.
                yield return WaitAtMost(() => HeldNetGun() != null, 30f);
                NetGunArtifact loaded = HeldNetGun();
                Report("PERSIST_CHARGES_AFTER_LOAD", loaded != null ? loaded.ChargesRemaining : -1);

                AgentController freed = FindNetworkedQuarry(out _);
                if (freed == null)
                {
                    Report("PERSIST_QUARRY_AFTER_LOAD", "none");
                    Finish();
                    yield break;
                }

                Report("PERSIST_QUARRY_BOUND_AFTER_LOAD", IsNetted(freed.gameObject));
                Report("PERSIST_QUARRY_SPEED_AFTER_LOAD", NavSpeedOf(freed));
                Report("PERSIST_QUARRY_DRIVERS_AFTER_LOAD", DriversEnabled(freed));
                Report("PERSIST_NETS_AFTER_LOAD", CountNets(out _));

                // A creature that reloads with a hobbled speed or a switched-off motor still stands
                // there looking perfectly normal, so the only honest question is whether it moves.
                Vector3 from = freed.transform.position;
                yield return new WaitForSeconds(12f);
                Report("PERSIST_QUARRY_TRAVELLED",
                       Vector3.Distance(from, freed.transform.position).ToString("F2"));

                Report("PERSIST_DONE", true);
                Finish();
            }

            private static NetGunArtifact HeldNetGun()
            {
                GameObject player = LocalPlayerObject();
                var equipment = player != null ? player.GetComponent<EquipmentController>() : null;

                return equipment != null ? equipment.HeldUsable as NetGunArtifact : null;
            }

            /// <summary>
            /// The creature's navigation speed, or -1 when it does not steer by one.
            ///
            /// The number the hobble writes to and the number a leaked hobble would come back with.
            /// A -1 is not a failure — several of this game's creatures are driven by legs rather
            /// than by an agent — it means this particular hazard does not apply to this creature,
            /// and PERSIST_QUARRY_TRAVELLED is then the only witness left.
            /// </summary>
            private static float NavSpeedOf(AgentController creature)
            {
                if (creature == null) return -1f;

                var agent = creature.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
                return agent != null ? agent.speed : -1f;
            }

            /// <summary>
            /// How many of a creature's motors and brains are switched on, as "on/total".
            ///
            /// The other half of "came back able to move", and the half a speed cannot answer: a
            /// creature whose driver was switched off by a runtime effect and then captured that way
            /// reloads standing perfectly still with its authored speed intact. Read through the
            /// same <c>NetAuthority.Discover</c> the client step counts, so the two numbers mean the
            /// same thing.
            /// </summary>
            private static string DriversEnabled(AgentController creature)
            {
                if (creature == null) return "none";

                int on = 0, total = 0;
                foreach (Behaviour driver in NetAuthority.Discover(creature.gameObject))
                {
                    if (driver == null) continue;
                    total++;
                    if (driver.enabled) on++;
                }

                return $"{on}/{total}";
            }

            // ─────────── Helpers ───────────

            /// <summary>
            /// A networked entity with health that is NOT a player — an AI, which is the case that
            /// never replicated before this work.
            ///
            /// Lowest NetworkObjectId wins, deliberately. Ids are assigned by the server and
            /// replicated, so this rule picks the same entity in both processes; dictionary order
            /// does not, and neither does name (persistentScene holds two "DuneRat").
            /// </summary>
            private static HealthComponent FindNetworkedVictim(out ulong id)
            {
                HealthComponent best = null;
                id = 0;

                foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    NetworkObject netObj = pair.Value;
                    if (netObj == null || netObj.IsPlayerObject) continue;
                    if (netObj.GetComponent<NetAuthority>() == null) continue;

                    HealthComponent health = netObj.GetComponentInChildren<HealthComponent>();
                    if (health == null || !health.Alive) continue;

                    if (best == null || pair.Key < id)
                    {
                        best = health;
                        id = pair.Key;
                    }
                }

                return best;
            }

            /// <summary>
            /// A networked creature, chosen by the same lowest-id rule
            /// <see cref="FindNetworkedVictim"/> uses and for the same reason: both processes have
            /// to land on the same one without either being told which.
            ///
            /// An <c>AgentController</c> rather than merely something with health, because that is
            /// exactly what <c>SnareCatch.Capture</c> will accept — anything that is neither a
            /// player nor a creature is refused however wide the gun's layer mask is.
            /// </summary>
            private static AgentController FindNetworkedQuarry(out ulong id)
            {
                AgentController best = null;
                id = 0;

                foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    NetworkObject netObj = pair.Value;
                    if (netObj == null || netObj.IsPlayerObject) continue;

                    var agent = netObj.GetComponentInChildren<AgentController>();
                    if (agent == null) continue;

                    if (best == null || pair.Key < id)
                    {
                        best = agent;
                        id = pair.Key;
                    }
                }

                return best;
            }

            private static GameObject LocalPlayerObject()
            {
                NetworkObject player = NetworkManager.Singleton.LocalClient?.PlayerObject;
                return player != null ? player.gameObject : null;
            }

            /// <summary>Which hotbar slot an item ended up in, or -1.</summary>
            private static int SlotHolding(IPlayerInventory inventory, InventoryItem item)
            {
                for (int i = 0; i < inventory.GetInventorySize(); i++)
                {
                    InventorySlot slot = inventory.GetSlot(i);
                    if (slot != null && slot.Item == item) return i;
                }

                return -1;
            }

            /// <summary>
            /// Where this gun's next shot would come down.
            ///
            /// Asked of the gun itself — <c>OnRequestUse</c> is what fills in the muzzle and the
            /// aim, and it is the only thing that knows where the barrel is on the end of an
            /// animated arm. The seed it rolls here is not the one the real shot rolls, and that
            /// does not matter: the seed scatters the aim by a fraction of a degree, and the net is
            /// six metres across.
            /// </summary>
            private static Vector3 PredictedLanding(NetGunArtifact gun)
            {
                var probe = new NetArg();
                gun.OnRequestUse(ref probe);

                return NetGunFlight.PositionAt(probe.P, probe.R * Vector3.forward, probe.B,
                                               NetGunFlight.MaxFlightSeconds);
            }

            /// <summary>
            /// The ground under a point, or the point itself where there is none.
            ///
            /// The fallback is not defensive padding. This build's scene set is Bootstrap, MainMenu
            /// and persistentScene — the terrain lives in the chunk scenes, which are left out
            /// because they are most of the build time and none of the netcode. With no ground to
            /// hit, <c>SnareCatch</c>'s own height sample also falls back to the net's position, so
            /// putting the quarry at the raw landing point is what keeps the two agreeing.
            /// </summary>
            private static Vector3 GroundUnder(Vector3 point)
            {
                const float probeUp = 200f;
                const float probeRange = 400f;

                return Physics.Raycast(point + Vector3.up * probeUp, Vector3.down,
                                       out RaycastHit hit, probeRange,
                                       ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : point;
            }

            /// <summary>How far the nearest net's footprint is from a creature. For a miss report.</summary>
            private static float NetToQuarryDistance(AgentController quarry)
            {
                float nearest = float.PositiveInfinity;

                foreach (SnareCatch net in Object.FindObjectsByType<SnareCatch>(FindObjectsSortMode.None))
                {
                    float distance = Vector3.Distance(net.Footprint.center, quarry.transform.position);
                    if (distance < nearest) nearest = distance;
                }

                return nearest;
            }

            /// <summary>The relay on the lowest-id spawned object — same answer in both processes.</summary>
            private static NetRelay LowestIdRelay()
            {
                NetRelay best = null;
                ulong bestId = 0;

                foreach (var pair in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    NetRelay relay = pair.Value != null ? pair.Value.GetComponent<NetRelay>() : null;
                    if (relay == null) continue;

                    if (best == null || pair.Key < bestId)
                    {
                        best = relay;
                        bestId = pair.Key;
                    }
                }

                return best;
            }

            private static void CountRelayFromPeer(in NetArg arg, ulong sender)
            {
                if (arg.B == 31337) NoteRelayFromPeer();
            }

            /// <summary>
            /// Waits, but never forever. A hung step has to end the process with a report saying
            /// which step hung, or a batch-mode run just sits there and the caller learns nothing.
            /// </summary>
            private IEnumerator WaitFor(System.Func<bool> condition, string what)
            {
                float deadline = Time.realtimeSinceStartup + StepTimeout;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Report("TIMEOUT_WAITING_FOR", what);
                        Report("DONE", false);
                        Finish();
                        yield break;
                    }

                    yield return null;
                }
            }

            /// <summary>
            /// Waits, and gives up quietly.
            ///
            /// The counterpart of <see cref="WaitFor"/>, for the steps where not arriving is an
            /// ANSWER rather than a broken run. "The client never saw the net" is the finding the
            /// net gun step exists to produce, and ending the process on it would throw away the
            /// numbers that say how badly it failed.
            /// </summary>
            private IEnumerator WaitAtMost(System.Func<bool> condition, float seconds)
            {
                float deadline = Time.realtimeSinceStartup + seconds;
                while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            }

            private void Finish()
            {
                Debug.Log("[MPTEST] EXIT");
                Application.Quit();
            }
        }

        /// <summary>Counts relay messages this process received from its peer.</summary>
        internal static void NoteRelayFromPeer() => relayFromPeer++;
    }
}
