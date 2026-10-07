// The play-mode half of the residents baseline (the editor half is ResidentsBaseline under
// Editor/Agents, which opens the world scene, enters play mode and starts this). It plays one
// in-game day of a real settlement and records, per resident and per plan segment, whether the
// resident got where its day said and what it did there — so a rewrite of the residents can be held
// to the day the old code produced, rather than to a feeling.
//
// The run, in order:
//   1. wait for the game's Bootstrap chain to finish, then load the world scene Single unless that is
//      what was played (offline: no session) — the editor's own scenes are never touched;
//   2. anchor streaming at the settlement's chunk and wait for its society to have residents;
//   3. once the ground under the offline player has loaded (until then UnderTerrainGuard parks it, and
//      a park puts it straight back), stand it near the settlement's heart as the observer — residents
//      only talk to each other with a player in earshot — with its suit oxygen off so a day cannot kill it;
//   4. fix the step (captureDeltaTime), shorten the day, anchor the clock at startDay/startHour —
//      which rebuilds every plan — and optionally seed one first-hand deed for the day-end gossip;
//   5. sample every resident each sampleSeconds for one in-game day, appending a CSV row at each plan
//      segment boundary as it happens, and the day-end gossip counts after midnight;
//   6. write the summary (last line DONE) and tell the editor to leave play mode. A run cut short —
//      play mode stopped by someone, a throw — keeps the rows so far and says PARTIAL and why.
//
// Errors anything logs during the run are counted and listed in the summary: a clean day is part of
// the baseline.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SpaceGame.Agents
{
    public sealed class ResidentsBaselineRun : MonoBehaviour
    {
        private const string HarnessName = "ResidentsBaseline";
        private const string FaultSite = "ResidentsBaseline.Run";
        private const string SeededDeedProfile = "residents-baseline-observer";
        private const string StatusOk = "OK";
        private const int MaxErrorsListed = 10;

        // How far the observer's stand point may be snapped onto the NavMesh, and its pivot's height
        // above the soles (the player's root rides ~1 m above its feet).
        private const float ObserverSnapDistance = 8f;
        private const float ObserverPivotHeight = 1f;
        // Ground loaded this far round the player's spawn means UnderTerrainGuard has let go of it.
        private const float ObserverGroundRadius = 1f;

        private ResidentsBaselineSettings settings;
        private Action finished;
        private string failure;
        private string status = StatusOk;
        private bool done;

        private WorldStreamer streamer;
        private Transform anchor;
        private Settlement settlement;
        private SettlementSociety society;
        private readonly List<ResidentBaselineTrack> tracks = new();
        private readonly ResidentBaselineCounts counts = new();
        private readonly StringBuilder csv = new();

        private bool tracking;
        private string settlementLabel;
        private int placeCount, usablePlaces;
        private double lastNow;

        private string observerNote = "none";
        private Transform observer;
        private Vector3 observerStand;
        private string seededNote = "off";

        private double startMinutes, endMinutes;
        private int frames;
        private readonly Stopwatch wallClock = new();
        private int linesAtStart, conversationsAtStart;
        private int lastDay;
        private int dayEndsPending;
        private string gossipNote = "midnight not reached";

        private short[] propStates;
        private int propsMoved;

        private int errorCount;
        private readonly List<string> errorsListed = new();
        private readonly HashSet<string> errorsSeen = new();

        /// <summary>
        /// Starts a run. <paramref name="onFinished"/> is called once, after the files are written,
        /// whether the run succeeded or failed — it is how the editor knows to leave play mode.
        /// </summary>
        public static ResidentsBaselineRun Begin(ResidentsBaselineSettings settings, Action onFinished)
        {
            var host = new GameObject(nameof(ResidentsBaselineRun));
            DontDestroyOnLoad(host);

            var run = host.AddComponent<ResidentsBaselineRun>();
            run.settings = settings;
            run.finished = onFinished;
            Application.logMessageReceived += run.OnLog;

            run.StartCoroutine(Fault.Coroutine(run, FaultSite, run.Execute(),
                                               () => run.Finish("the run threw; see the [Fault] line in the console")));
            return run;
        }

        /// <summary>A summary that says only why there is no baseline. For the editor, when it refuses to start.</summary>
        public static void WriteFailure(ResidentsBaselineSettings settings, string reason) =>
            HarnessRun.WriteFailure(HarnessName, settings.summaryPath, reason);

        private IEnumerator Execute()
        {
            yield return HarnessRun.WaitForBootstrap(settings.settleTimeoutFrames, reason => failure = reason);
            if (failure == null) yield return EnterWorld();
            if (failure == null) yield return FindSettlement();
            if (failure != null)
            {
                Finish(failure);
                yield break;
            }

            Time.captureDeltaTime = 1f / Mathf.Max(settings.simulatedFrameRate, 1f);
            observer = GameplayMenuScope.LocalPlayerTransform;
            if (observer != null)
            {
                yield return WaitForObserverGround();
                PlaceObserver();
            }
            else observerNote = "none — no local player, so no resident pair ever has a player in earshot";

            for (int i = 0; i < settings.warmupFrames; i++)
                yield return null;

            if (!SetClock())
            {
                Finish(failure);
                yield break;
            }

            SeedDeed();
            BeginTracking();

            float sinceSample = 0f;
            while (society.NowMinutes < endMinutes)
            {
                yield return null;
                frames++;

                if (wallClock.Elapsed.TotalMinutes > settings.maxWallClockMinutes)
                {
                    status = "PARTIAL — stopped after " + settings.maxWallClockMinutes.ToString("0.#", CultureInfo.InvariantCulture) +
                             " wall-clock minutes at " + lastNow.ToString("0", CultureInfo.InvariantCulture) + " game minutes";
                    break;
                }

                sinceSample += Time.deltaTime;
                if (sinceSample < settings.sampleSeconds) continue;
                sinceSample = 0f;
                SampleAll();
                FlushRows();
            }

            foreach (ResidentBaselineTrack track in tracks)
                track.Cut(society, lastNow, counts, csv);
            FlushRows();

            HarnessRun.WriteText(settings.summaryPath, BuildSummary());
            Finish(null);
        }

        // Rows go to disk as they are made, so a run stopped halfway still leaves them.
        private void FlushRows()
        {
            if (csv.Length == 0) return;
            HarnessRun.AppendText(settings.csvPath, csv.ToString());
            csv.Clear();
        }

        private IEnumerator EnterWorld()
        {
            if (SceneManager.GetActiveScene().name != settings.worldScene)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(settings.worldScene, LoadSceneMode.Single);
                if (load == null)
                {
                    failure = "the world scene '" + settings.worldScene + "' could not be loaded: is it in the build settings?";
                    yield break;
                }
                while (!load.isDone) yield return null;
            }

            // Whatever the scene played before the world logged is not the world's doing.
            errorCount = 0;
            errorsListed.Clear();
            errorsSeen.Clear();
        }

        private IEnumerator FindSettlement()
        {
            for (int frame = 0; frame < settings.settlementTimeoutFrames; frame++)
            {
                if (streamer == null) streamer = FindFirstObjectByType<WorldStreamer>();
                if (streamer != null && streamer.IsReady && anchor == null && !AnchorAtChunk()) yield break;

                settlement = FindSettlementInChunk();
                if (settlement != null) yield break;
                yield return null;
            }

            failure = streamer == null
                ? "no WorldStreamer came up in the world scene '" + settings.worldScene + "'"
                : "no settlement with residents appeared in " + settings.settlementScene + " after " +
                  settings.settlementTimeoutFrames + " frames";
        }

        // Streaming follows anchors, so one at the chunk's centre loads it whoever else is about.
        private bool AnchorAtChunk()
        {
            WorldStreamingConfig config = streamer.Config;
            if (config != null && config.chunks != null)
                foreach (ChunkInfo chunk in config.chunks)
                {
                    if (chunk.sceneName != settings.settlementScene) continue;

                    anchor = new GameObject("[ResidentsBaseline Anchor]").transform;
                    anchor.position = chunk.worldBounds.center;
                    DontDestroyOnLoad(anchor.gameObject);
                    streamer.RegisterTrackedTransform(anchor);
                    return true;
                }

            failure = "the streaming config has no chunk named " + settings.settlementScene;
            return false;
        }

        private Settlement FindSettlementInChunk()
        {
            foreach (Settlement candidate in FindObjectsByType<Settlement>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene.name != settings.settlementScene || !candidate.HasResidents) continue;

                SettlementSociety candidateSociety = candidate.Society;
                if (candidateSociety == null || candidateSociety.Residents.Count == 0) continue;

                society = candidateSociety;
                return candidate;
            }
            return null;
        }

        private IEnumerator WaitForObserverGround()
        {
            for (int frame = 0; frame < settings.settlementTimeoutFrames; frame++)
            {
                if (streamer.IsGroundLoadedAround(observer.position, ObserverGroundRadius)) break;
                yield return null;
            }

            // The guard lets go of a park on its own check, a few frames after the ground arrives.
            for (int i = 0; i < settings.warmupFrames; i++)
                yield return null;
        }

        private void PlaceObserver()
        {
            var oxygen = observer.GetComponentInChildren<SuitOxygen>();
            if (oxygen != null) oxygen.enabled = false;

            Vector3 heart = settlement.WalkableHeart;
            Vector3 stand = heart + settings.observerOffset;
            if (NavMesh.SamplePosition(stand, out NavMeshHit hit, ObserverSnapDistance, NavMesh.AllAreas))
                stand = hit.position;
            observerStand = stand + Vector3.up * ObserverPivotHeight;

            Vector3 look = Vector3.ProjectOnPlane(heart - stand, Vector3.up);
            Quaternion facing = look.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(look) : observer.rotation;
            SaveTeleport.Move(observer.gameObject, observerStand, facing);
            observerNote = "'" + observer.name + "' at " + Point(observerStand) + ", " +
                           Vector3.Distance(stand, heart).ToString("0.0", CultureInfo.InvariantCulture) + " m from the heart";
        }

        private bool SetClock()
        {
            DayNightCycle cycle = DayNightCycle.Main;
            if (cycle == null)
            {
                failure = "no DayNightCycle is live, so residents have no clock";
                return false;
            }

            cycle.cycleDuration = settings.dayLengthSeconds;
            // An anchor move rebuilds every settlement's plans for the day it names.
            cycle.AnchorTo(settings.startDay, settings.startHour / DayNightCycle.HoursPerDay, DayNightCycle.Now);

            startMinutes = society.NowMinutes;
            endMinutes = startMinutes + DayNightCycle.MinutesPerDay;
            lastDay = society.Day;
            wallClock.Start();
            return true;
        }

        // A first-hand "the outsider hit me" in a resident with family, under a profile no player has: the kin
        // hear it at bedtime, hearth friends at the hearth, and nobody's attitude to a real player changes.
        private void SeedDeed()
        {
            if (!settings.seedDeed) return;

            foreach (Resident resident in society.Residents)
            {
                if (resident == null || resident.Memory == null || !HasFamily(resident)) continue;

                resident.Memory.AddDeed(SeededDeedProfile, ActKind.Hit, resident.index, society.Day, settings.seededDeedDays);
                seededNote = "#" + resident.index + " " + resident.DisplayName + " holds a first-hand Hit on day " + society.Day;
                return;
            }

            seededNote = "on, but no resident has family to tell";
        }

        private static bool HasFamily(Resident resident)
        {
            if (resident.bonds == null) return false;
            foreach (ResidentBond bond in resident.bonds)
                if (bond.kind == BondKind.Family) return true;
            return false;
        }

        private void BeginTracking()
        {
            HarnessRun.WriteText(settings.csvPath, ResidentBaselineTrack.CsvHeader + Environment.NewLine);
            tracking = true;

            // Read now, while every object is alive: a summary written as play mode ends cannot ask the scene.
            settlementLabel = "'" + settlement.name + "' in " + settlement.gameObject.scene.name;
            placeCount = society.PlaceCount;
            for (int i = 0; i < placeCount; i++)
                if (society.IsUsable(i)) usablePlaces++;

            double now = lastNow = society.NowMinutes;
            foreach (Resident resident in society.Residents)
            {
                if (resident == null) continue;

                var track = new ResidentBaselineTrack(resident, settings.reachedWithin);
                tracks.Add(track);
                track.Sample(society, now, counts, csv);
                track.WriteSnapshot(society, now, counts, csv, ResidentBaselineTrack.BoundaryStart);
                linesAtStart += track.LinesSaid;
            }
            conversationsAtStart = society.ConversationsOpened;

            IReadOnlyList<SettlementProp> props = settlement.Props.Props;
            propStates = new short[props.Count];
            for (int i = 0; i < props.Count; i++)
                propStates[i] = settlement.Props.StateOf(props[i]);
        }

        private void SampleAll()
        {
            double now = lastNow = society.NowMinutes;
            foreach (ResidentBaselineTrack track in tracks)
                track.Sample(society, now, counts, csv);

            SampleProps();

            // The society ends the day on its own tick, which may run after this one in the frame that
            // crosses midnight: the gossip is read one sample later.
            if (dayEndsPending > 0 && --dayEndsPending == 0) WriteDayEnd(now);
            if (society.Day != lastDay)
            {
                lastDay = society.Day;
                dayEndsPending = 1;
            }
        }

        private void SampleProps()
        {
            IReadOnlyList<SettlementProp> props = settlement.Props.Props;
            for (int i = 0; i < propStates.Length && i < props.Count; i++)
            {
                short state = settlement.Props.StateOf(props[i]);
                if (state == propStates[i]) continue;
                propStates[i] = state;
                propsMoved++;
            }
        }

        private void WriteDayEnd(double now)
        {
            int known = 0, heard = 0, knowing = 0;
            foreach (ResidentBaselineTrack track in tracks)
            {
                track.WriteSnapshot(society, now, counts, csv, ResidentBaselineTrack.BoundaryDayEnd);

                ResidentMemory memory = track.Resident.Memory;
                if (memory == null || memory.Deeds.Count == 0) continue;
                knowing++;
                foreach (ResidentMemory.Deed deed in memory.Deeds)
                {
                    known++;
                    if (deed.heard) heard++;
                }
            }

            gossipNote = known + " deeds held (" + heard + " heard second-hand) by " + knowing + " of " + tracks.Count +
                         " residents, read after midnight of day " + (lastDay - 1);
        }

        private string BuildSummary()
        {
            StringBuilder report = HarnessRun.BeginReport(HarnessName);
            report.Append("status: ").AppendLine(status);

            int alive = 0, offstage = 0, linesSaid = 0, started = 0, finishedErrands = 0, stops = 0, carries = 0;
            foreach (ResidentBaselineTrack track in tracks)
            {
                if (!track.WasDead) alive++;
                if (track.WasOffstage) offstage++;
                linesSaid += track.LinesSaid;
                started += track.ErrandsStarted;
                finishedErrands += track.ErrandsFinished;
                stops += track.ErrandStops;
                carries += track.Carries;
            }

            float wallSeconds = (float)wallClock.Elapsed.TotalSeconds;
            report.Append("settlement: ").Append(settlementLabel)
                  .Append(" | residents ").Append(tracks.Count).Append(" (alive at end ").Append(alive)
                  .Append(", offstage at end ").Append(offstage).Append(") | places ").Append(placeCount)
                  .Append(" (usable ").Append(usablePlaces).AppendLine(")");
            report.Append("clock: day ").Append(settings.startDay).Append(" from ")
                  .Append(settings.startHour.ToString("0.##", CultureInfo.InvariantCulture)).Append(" h, game minutes ")
                  .Append(startMinutes.ToString("0", CultureInfo.InvariantCulture)).Append(" → ")
                  .Append(lastNow.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" | day length ").Append(settings.dayLengthSeconds.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" s | captureDeltaTime 1/").Append(settings.simulatedFrameRate.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" | frames ").Append(frames).Append(" in ").Append(wallSeconds.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" s wall (").Append((frames / Mathf.Max(wallSeconds, 1f)).ToString("0.0", CultureInfo.InvariantCulture))
                  .AppendLine(" fps)");
            report.Append("observer: ").Append(observerNote);
            if (observer != null)
                report.Append(" | moved ").Append(Vector3.Distance(observer.position, observerStand).ToString("0.0", CultureInfo.InvariantCulture))
                      .Append(" m by the end");
            report.AppendLine();

            counts.AppendRows(report);
            counts.AppendSegments(report, settings.reachedWithin);
            report.Append("errands: started ").Append(started).Append(", finished ").Append(finishedErrands)
                  .Append(", stops reached ").Append(stops).Append(" | carries ").Append(carries)
                  .Append(" | props moved ").Append(propsMoved).AppendLine();
            report.Append("conversations opened: ").Append(society.ConversationsOpened - conversationsAtStart)
                  .Append(" | lines said: ").Append(linesSaid - linesAtStart).AppendLine();
            report.Append("seeded deed: ").AppendLine(seededNote);
            report.Append("gossip at day end: ").AppendLine(gossipNote);

            report.Append("errors logged: ").Append(errorCount).Append(" (").Append(errorsSeen.Count).AppendLine(" distinct)");
            foreach (string error in errorsListed)
                report.Append("  ").AppendLine(error);

            report.Append("csv: ").AppendLine(settings.csvPath);
            report.AppendLine(HarnessRun.DoneLine);
            return report.ToString();
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            errorCount++;
            int newline = condition.IndexOf('\n');
            string line = newline >= 0 ? condition.Substring(0, newline) : condition;
            if (errorsSeen.Add(line) && errorsListed.Count < MaxErrorsListed) errorsListed.Add(line);
        }

        private static string Point(Vector3 p) =>
            "(" + p.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + p.y.ToString("0.0", CultureInfo.InvariantCulture) +
            ", " + p.z.ToString("0.0", CultureInfo.InvariantCulture) + ")";

        // null reason: the files are already written. Otherwise the run failed and the summary says why — with
        // the counts so far when sampling had begun.
        private void Finish(string reason)
        {
            if (done) return;
            done = true;

            Time.captureDeltaTime = 0f;
            Application.logMessageReceived -= OnLog;
            if (streamer != null && anchor != null) streamer.UnregisterTrackedTransform(anchor);

            if (reason != null)
            {
                Debug.LogError("[" + HarnessName + "] " + reason, this);
                if (tracking)
                {
                    FlushRows();
                    status = "PARTIAL — " + reason;
                    HarnessRun.WriteText(settings.summaryPath, BuildSummary());
                }
                else WriteFailure(settings, reason);
            }

            finished?.Invoke();
        }

        // Play mode ended under the run — the user pressed stop, or something exited it.
        private void OnDestroy()
        {
            if (done) return;
            Finish("play mode ended before the run finished");
        }
    }
}
