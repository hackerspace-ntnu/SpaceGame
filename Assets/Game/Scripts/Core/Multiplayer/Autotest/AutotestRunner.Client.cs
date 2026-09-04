// The client's half of the two-process run — the only client-side proof this project has.
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>
        /// Longest the client waits for a net the host said it fired.
        ///
        /// Generous, and deliberately not a <c>WaitFor</c> deadline: "no net ever arrived" is the
        /// answer this step exists to catch, so it has to be reported rather than end the run.
        /// </summary>
        private const float NetWaitSeconds = 45f;

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

                foreach (Behaviour driver in SimulationDrivers.Discover(netObj.gameObject))
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
            HealthComponent health = AutotestProbes.FindNetworkedVictim(out ulong victimId);
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
            NetRelay channel = AutotestProbes.LowestIdRelay();
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
            Report("CLIENT_PORTALS_SEEN", AutotestProbes.CountPortals());

            yield return ReadShipPartsAsClient();

            // The net gun, and the reason the whole two-process apparatus exists for it. A net
            // is not a spawned NetworkObject: every machine draws its own from the origin, aim
            // and seed that came with the press, and is then TOLD by the server what that net
            // caught. So a client seeing no net means the shot never crossed, and a client
            // seeing a net that holds nothing means the catch never did — two different
            // failures that both look like a working feature on the host.
            yield return WaitAtMost(() => AutotestProbes.CountNets(out int held) > 0 && held > 0, NetWaitSeconds);

            int nets = AutotestProbes.CountNets(out int captives);
            Report("CLIENT_NETS_SEEN", nets);
            Report("CLIENT_NET_CAPTIVES", captives);

            AgentController quarry = AutotestProbes.FindNetworkedQuarry(out ulong quarryId);
            if (quarry == null)
            {
                Report("CLIENT_QUARRY", "none");
            }
            else
            {
                Report("CLIENT_QUARRY_ID", quarryId);
                Report("CLIENT_QUARRY_NAME", quarry.name);
                Report("CLIENT_QUARRY_BOUND", AutotestProbes.IsNetted(quarry.gameObject));
            }

            yield return ReadWornGearAsClient();

            Report("CLIENT_DONE", true);
            Finish();
        }
    }
}
