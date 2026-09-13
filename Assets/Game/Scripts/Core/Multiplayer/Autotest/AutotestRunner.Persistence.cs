// The save/quit/load run. Alone, because none of what it asks is a question about a peer.
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>The world the save/load run creates for itself. Its own slot, never a player's.</summary>
        private const string PersistWorldName = "mptest";

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
            yield return WaitFor(() => AutotestProbes.LocalPlayerObject() != null, "a player");
            yield return new WaitForSeconds(6f);

            // The creature's own numbers before a net has touched it. Everything after the load
            // is compared against these rather than against a guess: a leaked hobble is only
            // visible as a speed that does not come back to what the creature authored.
            AgentController before = AutotestProbes.FindNetworkedQuarry(out _);
            Report("PERSIST_QUARRY_SPEED_AUTHORED", NavSpeedOf(before));
            Report("PERSIST_QUARRY_DRIVERS_AUTHORED", DriversEnabled(before));

            yield return FireNetGunAtQuarry();

            var equipment = AutotestProbes.LocalPlayerObject() != null
                ? AutotestProbes.LocalPlayerObject().GetComponent<EquipmentController>()
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

            AgentController quarry = AutotestProbes.FindNetworkedQuarry(out _);
            Report("PERSIST_CHARGES_BEFORE_SAVE", gun.ChargesRemaining);
            Report("PERSIST_QUARRY_BOUND_BEFORE_SAVE", AutotestProbes.IsNetted(quarry != null ? quarry.gameObject : null));
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
            yield return WaitFor(() => AutotestProbes.LocalPlayerObject() != null, "a player after the load");

            // Read before the recharge clock can reach twelve seconds. See the summary.
            yield return WaitAtMost(() => HeldNetGun() != null, 30f);
            NetGunArtifact loaded = HeldNetGun();
            Report("PERSIST_CHARGES_AFTER_LOAD", loaded != null ? loaded.ChargesRemaining : -1);

            AgentController freed = AutotestProbes.FindNetworkedQuarry(out _);
            if (freed == null)
            {
                Report("PERSIST_QUARRY_AFTER_LOAD", "none");
                Finish();
                yield break;
            }

            Report("PERSIST_QUARRY_BOUND_AFTER_LOAD", AutotestProbes.IsNetted(freed.gameObject));
            Report("PERSIST_QUARRY_SPEED_AFTER_LOAD", NavSpeedOf(freed));
            Report("PERSIST_QUARRY_DRIVERS_AFTER_LOAD", DriversEnabled(freed));
            Report("PERSIST_NETS_AFTER_LOAD", AutotestProbes.CountNets(out _));

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
            GameObject player = AutotestProbes.LocalPlayerObject();
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
        /// same <c>SimulationDrivers.Discover</c> the client step counts, so the two numbers mean
        /// the same thing.
        /// </summary>
        private static string DriversEnabled(AgentController creature)
        {
            if (creature == null) return "none";

            int on = 0, total = 0;
            foreach (Behaviour driver in SimulationDrivers.Discover(creature.gameObject))
            {
                if (driver == null) continue;
                total++;
                if (driver.enabled) on++;
            }

            return $"{on}/{total}";
        }
    }
}
