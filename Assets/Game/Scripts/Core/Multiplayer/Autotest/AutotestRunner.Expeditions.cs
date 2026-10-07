// A settlement's band across two machines and across a reload. Three modes, none of which touches the main run:
//
//   expedition-host / expedition-client  the host sends one band of the nomad settlement out and brings it home:
//                                        muster, walk-out, hand-off, stand-ins in view, two folded days, homecoming.
//                                        The client, which never leaves the spawn, must see the stand-ins named and
//                                        armed as the residents they stand in for, never a resident twice, and no
//                                        stand-in left once the band has walked back in.
//   expedition-persist                   one process hands a band off, saves with it on the road, reloads and asks
//                                        whether the same band is on the same stage with its residents still away.
//
// Time is driven, not waited out: the clock is set to the departure hour, jumped over the muster, and jumped past the
// walk limit only when the walk-out has not reached the hand-off by itself. The host's player is the observer, moved
// to where the rule under test needs it: behind the settlement while the band walks out (nobody watches, so the
// hand-off is the unobserved one at departRadius), beside the folded band to make its stand-ins real, away again so it
// folds for /exp days, and beside the hand-off point for the walk-in, so the band comes home as stand-ins swapped for
// residents rather than out of nothing. A new world starts its crew strapped into the crash-landed ship, whose seat puts
// its rider back on the chair every frame, so the player first stands up (LeaveArrivalSeat), and every move is checked
// to have stuck (<STEP>_OBSERVER_PLACED).
//
// Every mode ends on <PREFIX>_PASS=True|False, and on a failure <PREFIX>_FAILED names each check that failed with what
// it saw. The client's numbers are also compared with the host's by the caller (STAND_INS, STAND_IN_LINE).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Persistence;
using SpaceGame.World;
using SpaceGame.World.Safety;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>The goal a band is forced for when the rotation has not announced one: the Phase 1 band.</summary>
        private const string ExpeditionGoalId = "scout";

        private const string ExpeditionWorldName = "mptest-expedition";

        private const float MinutesPerHour = DayNightCycle.MinutesPerDay / DayNightCycle.HoursPerDay;

        /// <summary>For <see cref="CheckStandIns"/>: the machine was never told how many to expect, so any is right but none.</summary>
        private const int AnyStandIns = -1;

        /// <summary>Seconds the host lets the client finish syncing the world before anything changes under it.</summary>
        private const float ClientSyncSeconds = 8f;

        /// <summary>Seconds the client gives the host to open the session before joining.</summary>
        private const float HostHeadStartSeconds = 6f;

        /// <summary>Seconds a moved player gets before anything depends on where it stands: the chunk's ground, the sim's player list.</summary>
        private const float ObserverSettleSeconds = 4f;

        /// <summary>Metres above the ground the player is put: its root rides about a metre above its soles.</summary>
        private const float ObserverLiftMetres = 2f;

        /// <summary>Flat metres a moved player may stand from where it was put: the ground probe's slack, never a seat that took it back.</summary>
        private const float ObserverPlacedMetres = 10f;

        /// <summary>Flat metres between two settlements that are one settlement loaded twice: the same authored pose.</summary>
        private const float SameSettlementMetres = 1f;

        /// <summary>Seconds the crash landing gets to gather its crew, fly down and let them stand.</summary>
        private const float ArrivalLandSeconds = 90f;

        /// <summary>Seconds a seat gets to let the player go once asked: the leave-seat event reaching every machine.</summary>
        private const float SeatReleaseSeconds = 5f;

        /// <summary>
        /// Metres past the hand-off observe radius that the player stands behind the muster spot while the band walks out,
        /// so nobody sees it and it is handed off at departRadius. Inside the settlement's 3x3 of chunks, so it stays loaded.
        /// </summary>
        private const float ObserverMarginMetres = 50f;

        /// <summary>Metres from a folded band the player stands to make it real: well inside NpcWorldSim's spawn radius.</summary>
        private const float BandWatchMetres = 120f;

        /// <summary>
        /// Metres from the hand-off point, towards home, the player stands for the walk-in: inside the observe radius, so the
        /// homecoming waits for the stand-ins and swaps them for the residents where they stand.
        /// </summary>
        private const float WalkInWatchMetres = 40f;

        /// <summary>Seconds the band's members get to take up their kit and their row at the muster spot.</summary>
        private const float MusterWaitSeconds = 30f;

        /// <summary>Game minutes the clock is jumped past the end of the muster, so the next pass of the performer walks the band out.</summary>
        private const float ClockJumpMarginMinutes = 5f;

        /// <summary>Real seconds the walk-out gets to reach the hand-off by itself before the clock is jumped past the walk limit.</summary>
        private const float HandOffWaitSeconds = 300f;

        /// <summary>Seconds a clock jump gets to be acted on: the director's and the performer's next passes.</summary>
        private const float ClockJumpSettleSeconds = 10f;

        /// <summary>Seconds the folded band gets to become stand-ins once the player stands beside it.</summary>
        private const float StandInSpawnWaitSeconds = 30f;

        /// <summary>Seconds a stand-in gets to be named and draw its weapon: replicated values and the hand rule's next pass.</summary>
        private const float StandInDressSeconds = 15f;

        /// <summary>Seconds the host keeps the stand-ins in view, so the client can read them.</summary>
        private const float StandInWatchSeconds = 30f;

        /// <summary>Seconds the band gets to fold once the player has left it.</summary>
        private const float FoldWaitSeconds = 30f;

        /// <summary>Game days the folded band is run ahead of the clock (/exp days).</summary>
        private const float SimulatedDays = 2f;

        /// <summary>
        /// Seconds the band gets to arrive at the hand-off point and be swapped for its residents. Long enough for a band the
        /// days left on its way home, within the player's spawn radius, to walk the rest as stand-ins.
        /// </summary>
        private const float SwapWaitSeconds = 240f;

        /// <summary>Real seconds the walk-in gets to reach the muster spot by itself before the clock is jumped past the walk limit.</summary>
        private const float HomecomingWaitSeconds = 240f;

        /// <summary>Seconds the host stays up after its last step: the client reads the homecoming and cannot once the session is down.</summary>
        private const float ClientReadSeconds = 30f;

        /// <summary>Seconds the client waits for the first stand-in: the host's settle, muster and walk-out come first.</summary>
        private const float StandInsAppearSeconds = 720f;

        /// <summary>Seconds the client waits for the members to be shown at home again: the host's watch, fold, days and walk-in.</summary>
        private const float MembersBackSeconds = 600f;

        /// <summary>Seconds between the swap and the client's count of stand-ins left: the despawn has to cross the wire.</summary>
        private const float SwapSettleSeconds = 5f;

        /// <summary>Seconds the client waits for the members to put their kit away: the walk-in to the muster spot.</summary>
        private const float HomecomingSeenSeconds = 600f;

        /// <summary>Seconds the reloaded director gets to adopt the band's group.</summary>
        private const float AdoptWaitSeconds = 30f;

        /// <summary>Seconds the reloaded world gets to tear the old scene down before the new player is asked for.</summary>
        private const float ReloadTeardownSeconds = 4f;

        // The band under test, and where its home and the player's out-of-sight post are. Set by SendBandOut.
        private ExpeditionRecord expedition;
        private Vector3 expeditionHome;
        private Vector3 expeditionAwayPoint;

        /// <summary>
        /// The checks of one mode: each one's line as it is made, and one verdict at the end with every failure named.
        /// </summary>
        private sealed class Verdict
        {
            private readonly string prefix;
            private readonly List<string> failed = new();

            public Verdict(string prefix) => this.prefix = prefix;

            public string Key(string name) => prefix + "_" + name;

            /// <summary>Reports <paramref name="seen"/> under <paramref name="name"/>; a failure is remembered with it. Returns <paramref name="ok"/>.</summary>
            public bool Check(string name, bool ok, object seen)
            {
                Report(Key(name), seen);
                if (!ok) failed.Add($"{name} ({seen})");
                return ok;
            }

            public void Conclude()
            {
                Report(Key("PASS"), failed.Count == 0);
                if (failed.Count > 0) Report(Key("FAILED"), string.Join("; ", failed));
            }
        }

        // ── expedition-host ──────────────────────────────────────────────────

        private IEnumerator RunExpeditionHost()
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
            yield return new WaitForSeconds(ClientSyncSeconds);

            var verdict = new Verdict("HOST_EXP");
            yield return LeaveArrivalSeat(verdict);
            yield return VisitSettlement(verdict.Key("BEFORE"));
            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            ReportSettlement(verdict.Key("BEFORE"));
            List<string> idsBefore = AutotestProbes.ResidentIds(settlement);

            yield return SendBandOut(verdict);
            if (expedition != null) yield return WatchStandIns(verdict);
            if (expedition != null) yield return FoldAndRunDays(verdict);
            if (expedition != null) yield return BringBandHome(verdict);

            CheckResidentsAgree(verdict, AutotestProbes.FindExpeditionSettlement(), idsBefore);
            CheckNoStandInLeft(verdict);
            ReportSettlement(verdict.Key("AFTER"));
            verdict.Conclude();

            yield return new WaitForSeconds(ClientReadSeconds);
            Report("HOST_DONE", true);
            Finish();
        }

        /// <summary>
        /// The settlement's announced band (or a forced Scout band) departs now; the clock is jumped over its muster; the
        /// player stands out of sight while it walks out, and it is handed off. Leaves <see cref="expedition"/> set to the
        /// band on the road, folded, or null when a step failed (the verdict says which).
        /// </summary>
        private IEnumerator SendBandOut(Verdict verdict)
        {
            expedition = null;
            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            ExpeditionDirector director = ExpeditionDirector.Instance;
            ExpeditionProfile profile = settlement != null && settlement.Culture != null ? settlement.Culture.expeditions : null;
            if (!verdict.Check("DIRECTOR", director != null,
                               director != null ? "found" : "none in the world (Tools/SpaceGame/Expeditions/Wire Expedition Director)")) yield break;
            if (!verdict.Check("PROFILE", profile != null, profile != null ? profile.name : "the settlement's culture has no expedition profile"))
                yield break;
            if (!verdict.Check("CLOCK", DayNightCycle.Main != null, DayNightCycle.Main != null ? "found" : "the world has no day-night cycle"))
                yield break;

            ExpeditionTuning tuning = ExpeditionTuning.Instance;
            DayNightCycle.Main.JumpToHour(tuning.departHour);

            string settlementId = settlement.SettlementId;
            string problem = "announced by the rotation";
            ExpeditionRecord band = director.PendingFor(settlementId, ExpeditionPhase.Announced)
                                    ?? director.ForceBand(settlementId, ExpeditionGoalId, out problem);
            if (!verdict.Check("BAND_RAISED", band != null, band != null ? $"{band.id} ({problem})" : problem)) yield break;
            Report(verdict.Key("BAND_MEMBERS"), MemberKeys(band));

            director.DepartNow(band.id);
            if (!verdict.Check("BAND_MUSTERING", band.phase == ExpeditionPhase.Departing,
                               band.phase == ExpeditionPhase.Departing ? "Departing"
                               : $"{band.phase}: the settlement's performer did not take the band (no SettlementExpeditions)")) yield break;

            int living = ExpeditionRules.LivingMembers(band).Count;
            yield return WaitAtMost(() => band.phase != ExpeditionPhase.Departing ||
                                          CountMembers(settlement, band, r => r.IsWithBand) == living, MusterWaitSeconds);
            int withKit = CountMembers(settlement, band, r => r.IsWithBand);
            verdict.Check("MUSTERED_WITH_KIT", withKit == living, $"{withKit}/{living}");
            if (band.phase == ExpeditionPhase.Departing)
            {
                bool jumped = JumpClockAhead(tuning.musterMinutes + ClockJumpMarginMinutes);
                verdict.Check("MUSTER_CLOCK_JUMPED", jumped, jumped ? "over the muster" : "no: the jump would cross midnight");
            }

            expeditionHome = settlement.Society.TryMusterPose(profile.musterUse, out Pose muster) ? muster.position : settlement.transform.position;
            Vector3 road = Flat(band.handoffPoint - expeditionHome).normalized;
            expeditionAwayPoint = expeditionHome - road * (tuning.handoffObserveRadius + ObserverMarginMetres);
            yield return MoveObserver(verdict, "WALKOUT", expeditionAwayPoint);

            yield return WaitAtMost(() => band.phase != ExpeditionPhase.Departing, HandOffWaitSeconds);
            string handedOffBy = "the walk-out";
            if (band.phase == ExpeditionPhase.Departing)
            {
                handedOffBy = JumpClockAhead(tuning.walkLimitMinutes) ? "the walk limit (clock jumped)" : "nothing (the clock jump would cross midnight)";
                yield return WaitAtMost(() => band.phase != ExpeditionPhase.Departing, ClockJumpSettleSeconds);
            }
            Report(verdict.Key("HANDOFF_BY"), handedOffBy);
            Report(verdict.Key("HANDOFF_METRES_FROM_MUSTER"), MembersDistanceFromHome(settlement, band).ToString("F1"));
            if (!verdict.Check("HANDED_OFF", band.phase == ExpeditionPhase.Out, band.phase)) yield break;

            NpcGroup group = NpcWorldSim.Instance.FindGroup(band.groupId);
            if (!verdict.Check("GROUP_OWNER", group != null && group.Owner == NpcGroup.OwnerExpedition, group != null ? group.Owner : "no group"))
                yield break;
            verdict.Check("FOLDED_AT_HANDOFF", !group.Spawned, group.Spawned ? "spawned: somebody was near" : "folded");

            int away = CountMembers(settlement, band, r => r.IsAway && r.Presence != null && r.Presence.Hidden);
            verdict.Check("MEMBERS_AWAY", away == living, $"{away}/{living}");
            expedition = band;
        }

        /// <summary>The player walks up to the folded band: it becomes stand-ins, named and armed as its residents, and is held in view.</summary>
        private IEnumerator WatchStandIns(Verdict verdict)
        {
            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            NpcGroup group = NpcWorldSim.Instance.FindGroup(expedition.groupId);
            int living = ExpeditionRules.LivingMembers(expedition).Count;

            Vector3 towardHome = Flat(SettlementVisitPoint - group.Position);
            yield return MoveObserver(verdict, "BAND_WATCH", group.Position + towardHome.normalized * Mathf.Min(BandWatchMetres, towardHome.magnitude));
            yield return WaitAtMost(() => group.Spawned && AutotestProbes.StandIns().Count >= living, StandInSpawnWaitSeconds);
            yield return WaitUntilStandInsArmed();

            CheckStandIns(verdict, settlement, living);
            CheckResidentBodies(verdict, settlement);
            yield return new WaitForSeconds(StandInWatchSeconds);
        }

        /// <summary>The player leaves; the band folds, its stand-ins go, and it is run two days ahead (/exp days).</summary>
        private IEnumerator FoldAndRunDays(Verdict verdict)
        {
            NpcGroup group = NpcWorldSim.Instance.FindGroup(expedition.groupId);
            yield return MoveObserver(verdict, "FOLD", expeditionAwayPoint);
            yield return WaitAtMost(() => !group.Spawned, FoldWaitSeconds);
            yield return new WaitForSeconds(ObserverSettleSeconds);

            verdict.Check("FOLDED", !group.Spawned, group.Spawned ? "still spawned" : "folded");
            int left = AutotestProbes.StandIns().Count;
            verdict.Check("STAND_INS_AFTER_FOLD", left == 0, left);

            bool simulated = ExpeditionDirector.Instance.SimulateDays(expedition.id, SimulatedDays);
            verdict.Check("DAYS_SIMULATED", simulated, $"{expedition.phase}, {StageOf(expedition)}");
        }

        /// <summary>
        /// The player stands by the hand-off point and the band is sent home (/exp home): it arrives, its stand-ins are swapped
        /// for the residents where they stand, and they walk in to the muster spot.
        /// </summary>
        private IEnumerator BringBandHome(Verdict verdict)
        {
            ExpeditionDirector director = ExpeditionDirector.Instance;
            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            ExpeditionRecord band = expedition;
            int living = ExpeditionRules.LivingMembers(band).Count;

            yield return MoveObserver(verdict, "WALKIN", band.handoffPoint + Flat(expeditionHome - band.handoffPoint).normalized * WalkInWatchMetres);
            if (band.phase == ExpeditionPhase.Out)
            {
                ExpeditionDirector.HomeResult sent = director.SendHome(band.id);
                verdict.Check("SENT_HOME", sent is ExpeditionDirector.HomeResult.AtHandOffPoint or ExpeditionDirector.HomeResult.Walking, sent);
            }

            int standInsSeen = 0;
            yield return WaitAtMost(() =>
            {
                standInsSeen = Math.Max(standInsSeen, AutotestProbes.StandIns().Count);
                return CountMembers(settlement, band, r => !r.IsAway) == living;
            }, SwapWaitSeconds);
            Report(verdict.Key("WALKIN_STAND_INS_SEEN"), standInsSeen);
            int back = CountMembers(settlement, band, r => !r.IsAway);
            verdict.Check("MEMBERS_BACK", back == living, $"{back}/{living}");

            yield return new WaitForSeconds(ObserverSettleSeconds);
            int left = AutotestProbes.StandIns().Count;
            verdict.Check("STAND_INS_AFTER_SWAP", left == 0, left);

            yield return WaitAtMost(() => !IsUnderway(director, band.id), HomecomingWaitSeconds);
            string homeBy = "the walk-in";
            if (IsUnderway(director, band.id))
            {
                homeBy = JumpClockAhead(ExpeditionTuning.Instance.walkLimitMinutes) ? "the walk limit (clock jumped)" : "nothing (the clock jump would cross midnight)";
                yield return WaitAtMost(() => !IsUnderway(director, band.id), ClockJumpSettleSeconds);
            }
            Report(verdict.Key("HOMECOMING_BY"), homeBy);

            ExpeditionRecord record = director.FindBand(band.id);
            verdict.Check("BAND_HOME", record == null || record.phase == ExpeditionPhase.Home, record != null ? record.phase.ToString() : "retired");
            verdict.Check("GROUP_GONE", NpcWorldSim.Instance.FindGroup(band.groupId) == null, band.groupId);
            int home = CountMembers(settlement, band, r => !r.IsAway && !r.IsWithBand);
            verdict.Check("MEMBERS_HOME", home == living, $"{home}/{living}");
        }

        // ── expedition-client ────────────────────────────────────────────────

        private IEnumerator RunExpeditionClient()
        {
            yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");
            yield return new WaitForSeconds(HostHeadStartSeconds);

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

            // The client never leaves the spawn: the chunk and the band reach it because the SERVER has them.
            var verdict = new Verdict("CLIENT_EXP");
            yield return WaitAtMost(() => AutotestProbes.FindExpeditionSettlement() != null, StepTimeout * 2f);
            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            if (!verdict.Check("SETTLEMENT_FOUND", settlement != null, settlement != null))
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            // A chunk scene loaded twice while joining leaves a second, never-spawned copy of the settlement whose residents
            // are always shown at home: every check below would read whichever copy was found first.
            int copies = SettlementCopies();
            if (!verdict.Check("SETTLEMENT_COPIES", copies == 1,
                               copies == 1 ? "1" : $"{copies}: a chunk scene was loaded twice while joining (see Testing.md Gotchas)"))
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            yield return WaitAtMost(() => settlement.Society.Residents.Count > 0, StepTimeout);
            ReportSettlement(verdict.Key("BEFORE"));
            List<string> idsBefore = AutotestProbes.ResidentIds(settlement);

            yield return WaitAtMost(() => AutotestProbes.StandIns().Count > 0, StandInsAppearSeconds);
            yield return WaitUntilStandInsArmed();
            CheckStandIns(verdict, settlement, AnyStandIns);
            CheckResidentBodies(verdict, settlement);

            // The client is never told who is on the band: the stand-ins it saw name them.
            List<string> keys = AutotestProbes.StandIns().ConvertAll(standIn => standIn.Identity.residentKey);
            if (keys.Count == 0)
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            // Shown at home again is the walk-in's swap; the kit put away is the homecoming's end.
            yield return WaitAtMost(() => CountByKey(settlement, keys, Shown) == keys.Count, MembersBackSeconds);
            int back = CountByKey(settlement, keys, Shown);
            verdict.Check("MEMBERS_BACK", back == keys.Count, $"{back}/{keys.Count}");
            yield return new WaitForSeconds(SwapSettleSeconds);
            int left = AutotestProbes.StandIns().Count;
            verdict.Check("STAND_INS_AFTER_SWAP", left == 0, left);

            yield return WaitAtMost(() => CountByKey(settlement, keys, r => r.IsWithBand) == 0, HomecomingSeenSeconds);
            int withBand = CountByKey(settlement, keys, r => r.IsWithBand);
            verdict.Check("HOMECOMING_SEEN", withBand == 0, $"{withBand} still with the band");

            CheckResidentsAgree(verdict, settlement, idsBefore);
            CheckNoStandInLeft(verdict);
            ReportSettlement(verdict.Key("AFTER"));
            verdict.Conclude();
            Report("CLIENT_DONE", true);
            Finish();
        }

        // ── expedition-persist ───────────────────────────────────────────────

        /// <summary>
        /// One process, host of one: a band is handed off and moved on a stage, the world is saved with it on the road and
        /// reloaded. The band must come back once, on the same stage, owning its group, with its residents still away.
        /// </summary>
        private IEnumerator RunExpeditionPersistence()
        {
            yield return WaitFor(() => NetworkManager.Singleton != null, "networkmanager");

            Persistence.WorldSession.StageNew(ExpeditionWorldName, null);
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

            var verdict = new Verdict("PERSIST_EXP");
            yield return LeaveArrivalSeat(verdict);
            yield return VisitSettlement(verdict.Key("BEFORE"));
            List<string> idsBefore = AutotestProbes.ResidentIds(AutotestProbes.FindExpeditionSettlement());

            yield return SendBandOut(verdict);
            if (expedition == null)
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            // On a later stage than the hand-off's, so a reload that restarts the trip shows; never onto the way home.
            ExpeditionDirector director = ExpeditionDirector.Instance;
            if (ExpeditionRules.StageEndsUntil(expedition, StageKind.ReturnHome) > 1) director.AdvanceStage(expedition.id, 1);

            ExpeditionRecord band = expedition;
            string settlementId = band.settlementId;
            ExpeditionPhase phase = band.phase;
            int stage = band.stageIndex;
            List<string> bandsBefore = BandIds(director, settlementId);
            Report(verdict.Key("BAND_BEFORE_SAVE"), $"{band.id}: {phase}, {StageOf(band)}");
            Report(verdict.Key("BANDS_BEFORE_SAVE"), string.Join(",", bandsBefore));
            ReportSettlement(verdict.Key("BEFORE_SAVE"));

            Persistence.SaveManager manager = Persistence.SaveManager.Instance;
            if (!verdict.Check("SAVE_MANAGER", manager != null, manager != null ? "found" : "none in the world scene"))
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            string worldId = Persistence.WorldSession.WorldId;
            verdict.Check("SAVED", manager.Save(worldId, "MPTest", synchronous: true), worldId);
            CheckSavedBand(verdict, File.ReadAllText(manager.Slots.PathFor(worldId)), band);

            if (!Persistence.WorldSession.StageExisting(worldId, null, out string error))
            {
                verdict.Check("STAGED", false, error);
                verdict.Conclude();
                Finish();
                yield break;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(ReloadTeardownSeconds);
            yield return WaitFor(() => AutotestProbes.LocalPlayerObject() != null, "a player after the load");
            yield return VisitSettlement(verdict.Key("AFTER"));

            director = ExpeditionDirector.Instance;
            yield return WaitAtMost(() => director != null && director.FindBand(band.id) != null &&
                                          NpcWorldSim.Instance.FindGroup(director.FindBand(band.id).groupId) != null, AdoptWaitSeconds);

            ExpeditionRecord loaded = director != null ? director.FindBand(band.id) : null;
            if (!verdict.Check("BAND_AFTER_LOAD", loaded != null, loaded != null ? $"{loaded.phase}, {StageOf(loaded)}" : "gone"))
            {
                verdict.Conclude();
                Finish();
                yield break;
            }

            verdict.Check("SAME_PHASE", loaded.phase == phase, $"{phase} -> {loaded.phase}");
            verdict.Check("SAME_STAGE", loaded.stageIndex == stage, $"{stage} -> {loaded.stageIndex}");

            List<string> bandsAfter = BandIds(director, settlementId);
            int pastHandOff = director.BandsOf(settlementId).FindAll(ExpeditionRules.IsPastHandOff).Count;
            verdict.Check("NO_SECOND_BAND", SameSet(bandsBefore, bandsAfter) && pastHandOff == 1,
                          $"{string.Join(",", bandsAfter)}; {pastHandOff} past the hand-off");

            NpcGroup group = NpcWorldSim.Instance.FindGroup(loaded.groupId);
            verdict.Check("GROUP_OWNER", group != null && group.Owner == NpcGroup.OwnerExpedition, group != null ? group.Owner : "no group");

            Settlement settlement = AutotestProbes.FindExpeditionSettlement();
            int living = ExpeditionRules.LivingMembers(loaded).Count;
            int hidden = CountMembers(settlement, loaded, r => r.IsAway && r.Presence != null && r.Presence.Hidden);
            verdict.Check("MEMBERS_NOT_AT_HOME", hidden == living, $"{hidden}/{living} away and hidden");
            int seenTwice = AutotestProbes.SeenTwice(settlement, AutotestProbes.StandIns());
            verdict.Check("SEEN_TWICE", seenTwice == 0, seenTwice);

            CheckResidentsAgree(verdict, settlement, idsBefore);
            ReportSettlement(verdict.Key("AFTER_LOAD"));
            verdict.Conclude();
            Report("PERSIST_DONE", true);
            Finish();
        }

        /// <summary>
        /// The save as written: the <c>expeditions</c> entry holds the band in the phase and on the stage it was saved in, with
        /// its members, and the band's group record (under <c>npcworld</c>) is owned by the expedition director.
        /// </summary>
        private static void CheckSavedBand(Verdict verdict, string saveText, ExpeditionRecord band)
        {
            JObject save = JObject.Parse(saveText);
            var entries = new List<JToken>(save.SelectTokens("$..entries." + Persistence.ExpeditionSaveable.Key));
            if (!verdict.Check("SAVE_HAS_EXPEDITIONS", entries.Count > 0, entries.Count)) return;

            var state = entries[0].ToObject<Persistence.ExpeditionSaveable.State>(SaveSerializer.Serializer);
            ExpeditionRecord saved = Array.Find(state.bands ?? Array.Empty<ExpeditionRecord>(), b => b.id == band.id);
            verdict.Check("SAVED_BAND", saved != null && saved.phase == band.phase && saved.stageIndex == band.stageIndex,
                          saved != null ? $"{saved.phase}, {StageOf(saved)}" : "missing");
            verdict.Check("SAVED_MEMBERS", saved != null && MemberKeys(saved) == MemberKeys(band),
                          saved != null ? MemberKeys(saved) : "missing");

            string owner = "no record";
            foreach (JToken world in save.SelectTokens("$..entries." + Persistence.NpcWorldSaveable.Key))
                foreach (NpcGroup.Record record in world.ToObject<Persistence.NpcWorldSaveable.State>(SaveSerializer.Serializer).groups
                                                   ?? Array.Empty<NpcGroup.Record>())
                    if (record.id == band.groupId) owner = record.owner;
            verdict.Check("SAVED_GROUP_OWNER", owner == NpcGroup.OwnerExpedition, owner);
        }

        // ── Shared checks ────────────────────────────────────────────────────

        /// <summary>
        /// The stand-ins as this machine shows them: <paramref name="expected"/> of them (<see cref="AnyStandIns"/>: at least
        /// one), each named as the resident it stands in for and holding its kit's weapon, and no resident of theirs shown at
        /// home at the same time.
        /// </summary>
        private static void CheckStandIns(Verdict verdict, Settlement settlement, int expected)
        {
            List<ExpeditionMember> standIns = AutotestProbes.StandIns();
            bool counted = expected == AnyStandIns ? standIns.Count > 0 : standIns.Count == expected;
            verdict.Check("STAND_INS", counted, expected == AnyStandIns ? standIns.Count.ToString() : $"{standIns.Count} (expected {expected})");
            int named = AutotestProbes.StandInsNamedRight(settlement, standIns);
            verdict.Check("STAND_INS_NAMED", standIns.Count > 0 && named == standIns.Count, $"{named}/{standIns.Count}");
            int armed = AutotestProbes.StandInsArmed(standIns);
            verdict.Check("STAND_INS_ARMED", standIns.Count > 0 && armed == standIns.Count, $"{armed}/{standIns.Count}");
            int twice = AutotestProbes.SeenTwice(settlement, standIns);
            verdict.Check("SEEN_TWICE", twice == 0, twice);
            Report(verdict.Key("STAND_IN_LINE"), AutotestProbes.StandInLine(standIns));
        }

        /// <summary>Every resident body on this machine is the settlement's own or a stand-in: nothing left over from a swap.</summary>
        private static void CheckResidentBodies(Verdict verdict, Settlement settlement)
        {
            int residents = settlement.Society.Residents.Count, standIns = AutotestProbes.StandIns().Count;
            int bodies = AutotestProbes.ResidentBodies(settlement);
            verdict.Check("RESIDENT_BODIES", bodies == residents + standIns, $"{bodies} bodies, {residents} residents + {standIns} stand-ins");
        }

        /// <summary>The same residents as before, by identity, none twice, and no body besides them.</summary>
        private static void CheckResidentsAgree(Verdict verdict, Settlement settlement, List<string> idsBefore)
        {
            if (!verdict.Check("SETTLEMENT_AFTER", settlement != null, settlement != null)) return;

            List<string> ids = AutotestProbes.ResidentIds(settlement);
            verdict.Check("RESIDENTS_SAME", ids.Count == idsBefore.Count, $"{idsBefore.Count} -> {ids.Count}");
            verdict.Check("RESIDENT_IDS_SAME", SameSet(idsBefore, ids), SameSet(idsBefore, ids) ? "same" : "differ");
            int duplicates = AutotestProbes.Duplicates(ids);
            verdict.Check("DUPLICATE_IDS", duplicates == 0, duplicates);
            CheckResidentBodies(verdict, settlement);
        }

        /// <summary>The band is home: no stand-in of it is left on this machine.</summary>
        private static void CheckNoStandInLeft(Verdict verdict)
        {
            int standIns = AutotestProbes.StandIns().Count;
            verdict.Check("STAND_INS_LEFT", standIns == 0, standIns);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Waits for every stand-in this machine shows to hold its weapon: names and kit arrive with the spawn, the hand on its next pass.</summary>
        private IEnumerator WaitUntilStandInsArmed() =>
            WaitAtMost(() =>
            {
                List<ExpeditionMember> standIns = AutotestProbes.StandIns();
                return AutotestProbes.StandInsArmed(standIns) == standIns.Count;
            }, StandInDressSeconds);

        /// <summary>
        /// Puts the local player on the ground at <paramref name="point"/>, gives the world time to see it there, and checks
        /// that it stayed (<c>&lt;step&gt;_OBSERVER_PLACED</c>): a move undone by a seat, or made to where no chunk of this build
        /// lies, would otherwise leave every later check watching from the wrong place, with nothing said.
        /// </summary>
        private IEnumerator MoveObserver(Verdict verdict, string step, Vector3 point)
        {
            Vector3 at = point;
            if (TerrainProbe.TryGetTerrainHeight(point, out float ground)) at.y = ground;
            GameObject player = AutotestProbes.LocalPlayerObject();
            NetworkedTeleport.Move(player, at + Vector3.up * ObserverLiftMetres, Quaternion.identity);
            yield return new WaitForSeconds(ObserverSettleSeconds);

            bool grounded = TerrainProbe.TryGetTerrainHeight(point, out _);
            float off = player != null ? ExpeditionRules.FlatDistance(player.transform.position, point) : float.PositiveInfinity;
            string seen = !grounded ? $"no ground at {Flat(point):F0}: its chunk is not loaded (is it in this build?)"
                        : off <= ObserverPlacedMetres ? $"{off:F1} m from {Flat(point):F0}"
                        : $"{off:F1} m from {Flat(point):F0}: something put the player back (a seat holding it?)";
            verdict.Check(step + "_OBSERVER_PLACED", grounded && off <= ObserverPlacedMetres, seen);
        }

        /// <summary>
        /// A new world starts its crew strapped into the crash-landed ship, and a seat writes its rider back onto the chair
        /// every frame until the arrival's stranded-seat backstop turfs it out minutes later: until then every
        /// <see cref="MoveObserver"/> is undone on the next frame. The local player waits for the arrival to be over (the
        /// player body exists before it is seated, so "not seated" means nothing until then) and stands up, as a player
        /// pressing the key does. Nothing to stand up from in a world that has had its arrival, or has none.
        /// </summary>
        private IEnumerator LeaveArrivalSeat(Verdict verdict)
        {
            yield return WaitAtMost(ArrivalOver, ArrivalLandSeconds);
            if (!verdict.Check("ARRIVAL_OVER", ArrivalOver(), ArrivalOver() ? "landed" : $"still flying after {ArrivalLandSeconds:F0} s"))
                yield break;

            SeatedRider seat = SeatHoldingLocalPlayer();
            if (seat == null)
            {
                Report(verdict.Key("ARRIVAL_SEAT"), "not seated");
                yield break;
            }

            yield return WaitAtMost(() => SeatedRider.LocalPlayerMayLeave, SeatReleaseSeconds);
            seat.RequestLocalRelease();
            yield return WaitAtMost(() => SeatHoldingLocalPlayer() == null, SeatReleaseSeconds);
            bool standing = SeatHoldingLocalPlayer() == null;
            verdict.Check("ARRIVAL_SEAT", standing, standing ? "stood up after the landing" : "still held in its seat");
        }

        /// <summary>
        /// The settlements on this machine standing where the found one stands: one, unless a chunk scene was loaded twice.
        /// By place, not by id: the save system gives the second copy's identity a new id as it wakes.
        /// </summary>
        private static int SettlementCopies()
        {
            Settlement found = AutotestProbes.FindExpeditionSettlement();
            if (found == null) return 0;

            int copies = 0;
            foreach (Settlement settlement in UnityEngine.Object.FindObjectsByType<Settlement>(FindObjectsSortMode.None))
                if (ExpeditionRules.FlatDistance(settlement.transform.position, found.transform.position) <= SameSettlementMetres) copies++;
            return copies;
        }

        /// <summary>No crash landing is still to come or under way: the world has had its arrival, or has none (server).</summary>
        private static bool ArrivalOver() => ArrivalDirector.Instance == null || !ArrivalDirector.Instance.IsPending;

        /// <summary>The ship's seating that holds this machine's own player; null when it stands.</summary>
        private static SeatedRider SeatHoldingLocalPlayer()
        {
            foreach (SeatedRider rider in UnityEngine.Object.FindObjectsByType<SeatedRider>(FindObjectsSortMode.None))
                if (rider.HoldsLocalPlayer) return rider;
            return null;
        }

        /// <summary>Moves the world's clock on by <paramref name="gameMinutes"/> within the day (server; replicated). False when that would cross midnight.</summary>
        private static bool JumpClockAhead(float gameMinutes)
        {
            DayNightCycle clock = DayNightCycle.Main;
            float hour = clock.HourOfDay + gameMinutes / MinutesPerHour;
            if (hour >= DayNightCycle.HoursPerDay) return false;

            clock.JumpToHour(hour);
            return true;
        }

        /// <summary>The band's living members whose resident at home passes <paramref name="rule"/>.</summary>
        private static int CountMembers(Settlement settlement, ExpeditionRecord band, Func<Resident, bool> rule) =>
            CountByKey(settlement, ExpeditionRules.LivingMembers(band).ConvertAll(member => band.members[member].residentKey), rule);

        /// <summary>The residents at home with <paramref name="keys"/> that pass <paramref name="rule"/> on this machine.</summary>
        private static int CountByKey(Settlement settlement, List<string> keys, Func<Resident, bool> rule)
        {
            int count = 0;
            foreach (string key in keys)
            {
                Resident resident = AutotestProbes.HomeResident(settlement, key);
                if (resident != null && rule(resident)) count++;
            }
            return count;
        }

        /// <summary>Shown at home: its presence is not hidden. What every machine agrees on; whether it is away is the server's.</summary>
        private static bool Shown(Resident resident) => resident.Presence != null && !resident.Presence.Hidden;

        /// <summary>Metres between the members' residents' middle and the band's home: how far the walk-out got.</summary>
        private float MembersDistanceFromHome(Settlement settlement, ExpeditionRecord band)
        {
            var poses = new List<Pose>();
            foreach (int member in ExpeditionRules.LivingMembers(band))
            {
                Resident resident = AutotestProbes.HomeResident(settlement, band.members[member].residentKey);
                if (resident != null) poses.Add(new Pose(resident.transform.position, resident.transform.rotation));
            }
            return ExpeditionRules.FlatDistance(ExpeditionRules.Centroid(poses), expeditionHome);
        }

        private static bool IsUnderway(ExpeditionDirector director, string bandId)
        {
            ExpeditionRecord band = director.FindBand(bandId);
            return band != null && ExpeditionRules.IsUnderway(band);
        }

        private static List<string> BandIds(ExpeditionDirector director, string settlementId) =>
            director.BandsOf(settlementId).ConvertAll(b => b.id);

        private static string MemberKeys(ExpeditionRecord band) =>
            string.Join(",", Array.ConvertAll(band.members, m => m.residentKey + (m.isLeader ? "*" : "")));

        private static string StageOf(ExpeditionRecord band) =>
            band.stageIndex >= 0 && band.stageIndex < band.stages.Length
                ? $"stage {band.stageIndex} {band.stages[band.stageIndex].kind}"
                : $"stage {band.stageIndex} of {band.stages.Length}";

        private static bool SameSet(List<string> a, List<string> b) => a.Count == b.Count && new HashSet<string>(a).SetEquals(b);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
