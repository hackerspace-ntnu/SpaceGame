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
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    public static class MultiplayerAutotest
    {
        private const string WorldScene = "persistentScene";
        private const ushort Port = 7897;
        private const float StepTimeout = 120f;

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

        /// <summary>The MonoBehaviour half — coroutines need one.</summary>
        private class AutotestRunner : MonoBehaviour
        {
            public void Begin(string mode) => StartCoroutine(mode == "host" ? RunHost() : RunClient());

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
                Report("HOST_DONE", true);
                Finish();
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
                Report("CLIENT_DONE", true);
                Finish();
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
