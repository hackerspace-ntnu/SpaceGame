// The settlement across two machines and across a reload. Three modes, none of which touches the main run:
//
//   settlement-host / settlement-client  the host stands in the settlement and opens a pen gate; the client,
//                                        which never left the spawn, must see the same residents, animals and gate
//   settlement-persist                   one process opens a gate, saves, reloads the world and counts again
//
// The residents, the stock and the gates are scene content that every machine loads from the same chunk
// scene, so what a client sees is the server's NetworkVariables on top of identical bytes. A census taken on
// each side is the whole proof; the caller compares the two logs.
using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>
        /// Where the host's player is put: the middle of the nomad settlement in Chunk_6_3. Nothing of a chunk is
        /// loaded unless somebody stands near it, so the test has to walk there.
        /// </summary>
        private static readonly Vector3 SettlementVisitPoint = new Vector3(2989f, 118f, 602f);

        /// <summary>Seconds the residents get to leave their doors and reach their places before they are counted.</summary>
        private const float SettlementSettleSeconds = 40f;

        /// <summary>Seconds a pen gate stays open before the stock is counted again.</summary>
        private const float GateWatchSeconds = 40f;

        private const string SettlementWorldName = "mptest-settlement";

        private IEnumerator RunSettlementHost()
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

            NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == WorldScene, "world scene");
            yield return WaitFor(() => AutotestProbes.LocalPlayerObject() != null, "a player");
            yield return WaitFor(() => NetworkManager.Singleton.ConnectedClientsIds.Count > 1, "a client to connect");
            Report("HOST_CLIENTS", NetworkManager.Singleton.ConnectedClientsIds.Count);
            yield return new WaitForSeconds(8f);

            yield return VisitSettlement("HOST");
            ReportSettlement("HOST");

            SettlementPen pen = FirstGatedPen();
            if (pen == null)
            {
                Report("HOST_GATE", "no pen with a gate");
            }
            else
            {
                pen.Gate.Interact(null);
                yield return new WaitForSeconds(4f);
                Report("HOST_GATE_OPEN", pen.Gate.IsOpen);
                yield return new WaitForSeconds(GateWatchSeconds);
                ReportSettlement("HOST_AFTER_GATE");
            }

            // The client reads after the host has opened the gate and cannot once the host takes the session down.
            yield return new WaitForSeconds(GateWatchSeconds);
            Report("HOST_DONE", true);
            Finish();
        }

        private IEnumerator RunSettlementClient()
        {
            yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");
            yield return new WaitForSeconds(6f);

            System.Threading.Tasks.Task<SessionResult> join = SessionLauncher.JoinDirectAsync("127.0.0.1", Port);
            yield return WaitFor(() => join.IsCompleted, "join to complete");
            Report("CLIENT_JOINED", join.Result.Success);
            if (!join.Result.Success)
            {
                Report("CLIENT_ERROR", join.Result.Error);
                Finish();
                yield break;
            }

            yield return WaitFor(() => SceneManager.GetActiveScene().name == WorldScene, "world scene");

            // The client never leaves the spawn: the chunk reaches it because the SERVER loaded it for the host.
            yield return WaitAtMost(() => AutotestProbes.FindSettlement() != null, StepTimeout * 2f);
            Report("CLIENT_SETTLEMENT_FOUND", AutotestProbes.FindSettlement() != null);
            if (AutotestProbes.FindSettlement() == null)
            {
                Finish();
                yield break;
            }

            yield return WaitAtMost(() => AutotestProbes.FindSettlement().Society.Residents.Count > 0, StepTimeout);
            yield return new WaitForSeconds(SettlementSettleSeconds);
            ReportSettlement("CLIENT");

            yield return WaitAtMost(() => AutotestProbes.TakeSettlementCensus(AutotestProbes.FindSettlement()).gatesOpen > 0, StepTimeout);
            Report("CLIENT_GATE_OPEN_SEEN", AutotestProbes.TakeSettlementCensus(AutotestProbes.FindSettlement()).gatesOpen > 0);
            yield return new WaitForSeconds(GateWatchSeconds);
            ReportSettlement("CLIENT_AFTER_GATE");

            Report("CLIENT_DONE", true);
            Finish();
        }

        /// <summary>
        /// One process, host of one: stand in the settlement, open a pen gate, save, reload the world, stand in it
        /// again and count. Residents, stock and the gate have to come back as they were.
        /// </summary>
        private IEnumerator RunSettlementPersistence()
        {
            yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");

            Persistence.WorldSession.StageNew(SettlementWorldName, null);
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

            yield return VisitSettlement("PERSIST_BEFORE");
            SettlementPen pen = FirstGatedPen();
            if (pen == null)
            {
                Report("PERSIST_GATE", "no pen with a gate");
                Finish();
                yield break;
            }

            pen.Gate.Interact(null);
            yield return new WaitForSeconds(GateWatchSeconds);
            ReportSettlement("PERSIST_BEFORE_SAVE");

            string worldId = Persistence.WorldSession.WorldId;
            Persistence.SaveManager manager = Persistence.SaveManager.Instance;
            if (manager == null)
            {
                Report("PERSIST_SAVE", "no SaveManager in the world scene");
                Finish();
                yield break;
            }

            Report("PERSIST_SAVED", manager.Save(worldId, "MPTest", synchronous: true));
            string saveText = File.ReadAllText(manager.Slots.PathFor(worldId));
            Report("PERSIST_SAVE_BYTES", saveText.Length);
            Report("PERSIST_SAVE_RESIDENT_RECORDS", CountOccurrences(saveText, $"\"{SpaceGame.Agents.Residents.ResidentSaveable.Key}\""));
            Report("PERSIST_SAVE_DOOR_RECORDS", CountOccurrences(saveText, $"\"{Persistence.DoorSaveable.Key}\""));

            if (!Persistence.WorldSession.StageExisting(worldId, null, out string error))
            {
                Report("PERSIST_STAGE_ERROR", error);
                Finish();
                yield break;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(4f);
            yield return WaitFor(() => AutotestProbes.LocalPlayerObject() != null, "a player after the load");

            yield return VisitSettlement("PERSIST_AFTER");
            yield return new WaitForSeconds(GateWatchSeconds);
            ReportSettlement("PERSIST_AFTER_LOAD");

            Report("PERSIST_DONE", true);
            Finish();
        }

        /// <summary>Puts the local player in the settlement and waits for its chunk, its residents and the time they need to settle.</summary>
        private IEnumerator VisitSettlement(string side)
        {
            GameObject player = AutotestProbes.LocalPlayerObject();
            NetworkedTeleport.Move(player, SettlementVisitPoint, Quaternion.identity);

            yield return WaitFor(() => AutotestProbes.FindSettlement() != null, "the settlement chunk");
            Report(side + "_SETTLEMENT_FOUND", true);
            yield return WaitAtMost(() => AutotestProbes.FindSettlement().Society.Residents.Count > 0, StepTimeout);
            yield return new WaitForSeconds(SettlementSettleSeconds);
        }

        private static SettlementPen FirstGatedPen()
        {
            Settlement settlement = AutotestProbes.FindSettlement();
            if (settlement == null) return null;

            foreach (SettlementPen pen in settlement.GetComponentsInChildren<SettlementPen>())
                if (pen.Gate != null) return pen;
            return null;
        }

        private static void ReportSettlement(string prefix)
        {
            Settlement settlement = AutotestProbes.FindSettlement();
            if (settlement == null)
            {
                Report(prefix + "_SETTLEMENT", "none");
                return;
            }

            AutotestProbes.SettlementCensus census = AutotestProbes.TakeSettlementCensus(settlement);
            Report(prefix + "_RESIDENTS", census.residents);
            Report(prefix + "_RESIDENTS_HIDDEN", census.hidden);
            Report(prefix + "_RESIDENTS_HOLDING_A_PLACE", census.holdingAPlace);
            Report(prefix + "_RESIDENTS_ON_STAND_POINT", census.onStandPoint);
            Report(prefix + "_RESIDENTS_CLIMBING", census.climbing);
            Report(prefix + "_STOCK", census.stock);
            Report(prefix + "_STOCK_IN_PENS", census.stockInPens);
            Report(prefix + "_GATES", census.gates);
            Report(prefix + "_GATES_OPEN", census.gatesOpen);
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            for (int at = text.IndexOf(needle, System.StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}
