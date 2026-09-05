// The host's half of the two-process run.
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
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
            NetRelay channel = AutotestProbes.LowestIdRelay();
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
            HealthComponent victim = AutotestProbes.FindNetworkedVictim(out ulong victimId);
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
            Report("HOST_PORTALS", AutotestProbes.CountPortals());

            yield return FitShipPartsAsHost();

            yield return WatchTerminalAsHost();

            yield return FireNetGunAtQuarry();

            yield return WearGrappleOnRightArm();

            // The client reads its own net count and the host's worn gear after the host has
            // fired, and cannot do that once the host has taken the session down with it.
            yield return new WaitForSeconds(12f);

            Report("HOST_DONE", true);
            Finish();
        }
    }
}
