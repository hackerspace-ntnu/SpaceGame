// The settlement's half of expeditions: it plays, with the residents' own bodies, the parts of a band's life that
// happen at home while the settlement's chunk is loaded (spec §4.1, §10.1–10.2; Phase 1 has no ceremony, crowd or
// speech). The director decides; this only performs, and keeps no state of its own.
//
// - Muster: a band of this settlement turns Departing (at the departure hour, or at once by /exp). Its members walk to a
//   row at the muster spot (a Scripted override each) with their kit in hand (ExpeditionMember.CarryKit, which makes the
//   presence show Activity.Expedition) and stand there musterMinutes.
// - Walk-out: the leader says its departure line and the band walks out along the road, past the hand-off point. Each
//   pass asks ExpeditionRules.HandOffNow — how far the band's middle is from the spot, whether anybody sees one of them,
//   how long it has walked. When it says so, the director takes the band (BeginHandOff, every member's pose) and the
//   residents go Away IN THE SAME CALL, so the stand-ins replace them on one server tick.
// - Walk-in: the band is Returning (its group reached the hand-off point). Spawned, each resident comes home where its
//   stand-in stands and the group goes, in one call; folded, the residents come home in a row at the road point once
//   nobody watches it. They walk to the muster spot, kit in hand, and the homecoming is completed (CompleteHomecoming).
// - Away and the dead: when this starts, and whenever the director says the absent changed, a resident the director has
//   out goes Away where it stands (no walk). A finished band's dead die at home as a restored death (no loot, no fall to
//   replay), and the band is retired.
//
// Server only. Nothing here is saved: a save mid-muster or mid-walk loads as the director resolves it (a Departing band
// as departed, a Returning one as home), and the residents are then made to agree with it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents.Residents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Agents.Expeditions
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Settlement))]
    public sealed class SettlementExpeditions : MonoBehaviour
    {
        /// <summary>What a band is doing at home.</summary>
        private enum Act
        {
            /// <summary>Standing in its row at the muster spot.</summary>
            Muster,
            /// <summary>Walking out down the road until it is handed off.</summary>
            WalkOut,
            /// <summary>Returning, folded: waiting for nobody to watch the road point to come home there.</summary>
            Appear,
            /// <summary>Home again and walking in to the muster spot.</summary>
            WalkIn,
        }

        /// <summary>One member of the band, played by its resident.</summary>
        private sealed class Performer
        {
            public int Member;
            public Resident Resident;
            public ExpeditionMember Kit;
        }

        private sealed class Performance
        {
            public ExpeditionRecord Band;
            public Act Act;
            /// <summary>Game minutes (DayNightCycle.GameMinutesNow) the act began.</summary>
            public double Since;
            public Pose Muster;
            /// <summary>Where each performer is walking to or standing, by its place in <see cref="Cast"/>.</summary>
            public Pose[] Stands = System.Array.Empty<Pose>();
            public readonly List<Performer> Cast = new();
        }

        private readonly Dictionary<string, Performance> performances = new();
        private readonly List<Pose> poses = new();
        private readonly HashSet<string> warned = new();
        private Settlement settlement;
        private ExpeditionProfile profile;
        private string settlementId;
        private bool registered;
        private bool began;
        private float timer;
        private int reportedLiving = -1;

        private static ExpeditionTuning Tuning => ExpeditionTuning.Instance;
        private SettlementSociety Society => settlement.Society;
        private static double Now => DayNightCycle.Main != null ? DayNightCycle.Main.GameMinutesNow : 0d;

        private void Awake() => settlement = GetComponent<Settlement>();

        private void OnEnable()
        {
            if (!Network.Decides) return;

            settlementId = settlement.SettlementId;
            profile = settlement.Culture != null ? settlement.Culture.expeditions : null;
            if (string.IsNullOrEmpty(settlementId)) return;
            if (profile == null)
            {
                Debug.LogWarning($"[Expedition] {name} has a SettlementExpeditions but its culture has no expedition profile, so it " +
                                 "sends no band. Remove the component or give the culture a profile.", this);
                return;
            }

            ExpeditionDirector.RegisterPerformer(settlementId);
            ExpeditionDirector.AbsenceChanged += OnAbsenceChanged;
            registered = true;
        }

        private void OnDisable()
        {
            if (!registered) return;

            registered = false;
            began = false;
            ExpeditionDirector.UnregisterPerformer(settlementId);
            ExpeditionDirector.AbsenceChanged -= OnAbsenceChanged;
            foreach (Performance performance in performances.Values) Release(performance);
            performances.Clear();
        }

        private void Update()
        {
            ExpeditionDirector director = ExpeditionDirector.Instance;
            if (!registered || director == null || !Network.Decides) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Tuning.decisionInterval;

            // In Update, not Start: every Start of the chunk (the society's included) has run. A resident not yet network-spawned
            // may be sent Away here: its presence hands what it shows to clients when it spawns.
            if (!began)
            {
                began = true;
                ApplyAbsence();
            }

            Step(director);
        }

        private void OnAbsenceChanged(string changedSettlementId)
        {
            if (changedSettlementId != settlementId) return;

            // A load replaces the director's roster with the saved one: report the live one again.
            reportedLiving = -1;
            if (began) ApplyAbsence();
        }

        private void Step(ExpeditionDirector director)
        {
            ReportRoster(director);
            ApplyFinished(director);

            foreach (ExpeditionRecord band in director.BandsOf(settlementId))
            {
                if (performances.ContainsKey(band.id)) continue;
                if (band.phase == ExpeditionPhase.Departing) Muster(director, band);
                else if (band.phase == ExpeditionPhase.Returning) WalkIn(band);
            }

            foreach (Performance performance in new List<Performance>(performances.Values))
                Play(director, performance);
        }

        private void Play(ExpeditionDirector director, Performance performance)
        {
            // A load replaces every record, so the band is looked up again rather than trusted.
            ExpeditionRecord band = director.FindBand(performance.Band.id);
            bool leaving = performance.Act is Act.Muster or Act.WalkOut;
            ExpeditionPhase expected = leaving ? ExpeditionPhase.Departing : ExpeditionPhase.Returning;
            if (band != performance.Band || band.phase != expected)
            {
                Abandon(performance);
                return;
            }

            switch (performance.Act)
            {
                case Act.Muster: Mustering(performance); break;
                case Act.WalkOut: WalkingOut(director, performance); break;
                case Act.Appear: Appear(performance); break;
                case Act.WalkIn: WalkingIn(director, performance); break;
            }
        }

        // ── Muster (spec §10.1, Phase 1: no ceremony) ─────────────────────────

        /// <summary>The band's members leave what they are doing for a row at the muster spot, kit in hand.</summary>
        private void Muster(ExpeditionDirector director, ExpeditionRecord band)
        {
            if (!Society.TryMusterPose(profile.musterUse, out Pose muster))
            {
                WarnOnce(band.id + "/muster", $"[Expedition] {name} has no usable muster place (SpotUse " +
                         $"'{(profile.musterUse ? profile.musterUse.name : "none")}'), so band '{band.id}' leaves without a muster, " +
                         "from where its members stand. Run Tools/SpaceGame/Expeditions/Place Muster Spots.");
                LeaveWithoutMuster(director, band);
                return;
            }

            Performance performance = Cast(band, ExpeditionPhase.Departing);
            if (performance.Cast.Count == 0)
            {
                performances.Remove(band.id);
                LeaveWithoutMuster(director, band);
                return;
            }

            performance.Act = Act.Muster;
            performance.Since = Now;
            performance.Muster = muster;
            performance.Stands = ExpeditionRules.MusterRow(muster, performance.Cast.Count, Tuning.musterSpacing);

            foreach (Performer performer in performance.Cast)
            {
                // A sleeper indoors is got up: the routine ticks nobody offstage, so the override would wait for its morning.
                performer.Resident.ComeOnstage();
                CarryKit(performance.Band, performer);
            }
            Mustering(performance);
        }

        /// <summary>No muster place, or nobody here to play the band: the director takes it at its road point, and the residents go at once.</summary>
        private void LeaveWithoutMuster(ExpeditionDirector director, ExpeditionRecord band)
        {
            if (!director.BeginHandOff(band.id, null)) return;

            foreach (MemberRecord member in band.members)
                if (TryFind(member.residentKey, out Resident resident)) resident.GoAway();
        }

        private void Mustering(Performance performance)
        {
            double minutes = Now - performance.Since;
            if (minutes < Tuning.musterMinutes)
            {
                Hold(performance, Tuning.musterMinutes - minutes);
                return;
            }

            BeginWalkOut(performance);
        }

        // ── Walk-out and hand-off (spec §4.1) ─────────────────────────────────

        private void BeginWalkOut(Performance performance)
        {
            var road = new Pose(WalkOutPoint(performance), performance.Muster.rotation);
            performance.Act = Act.WalkOut;
            performance.Since = Now;
            performance.Stands = ExpeditionRules.MusterRow(road, performance.Cast.Count, Tuning.musterSpacing);
            Hold(performance, Tuning.walkLimitMinutes);

            Performer leader = performance.Cast.Find(p => performance.Band.members[p.Member].isLeader);
            if (leader != null && leader.Resident.TryGetComponent(out ResidentVoice voice))
                voice.Say(Topic.Farewell, null, Observation.None);
        }

        /// <summary>Out along the road, past the hand-off point; the hand-off point itself when nothing walkable lies there.</summary>
        private Vector3 WalkOutPoint(Performance performance)
        {
            Vector3 far = ExpeditionRules.WalkOutPoint(performance.Muster, performance.Band.handoffPoint, Tuning);
            if (OnGround(far, out Vector3 ground) || OnGround(performance.Band.handoffPoint, out ground)) return ground;
            return far;
        }

        private void WalkingOut(ExpeditionDirector director, Performance performance)
        {
            CollectPoses(performance);
            float distance = ExpeditionRules.FlatDistance(ExpeditionRules.Centroid(poses), performance.Muster.position);
            // A watcher too far off for the hand-off to spawn stand-ins counts as nobody: held to the swap distance, they
            // would see the residents vanish there anyway.
            bool observed = AnyoneSees(performance, director.HandOffObserveRadius);
            double minutes = Now - performance.Since;

            if (!ExpeditionRules.HandOffNow(distance, observed, minutes, Tuning)) Hold(performance, Tuning.walkLimitMinutes - minutes);
            else if (!HandOff(director, performance)) Hold(performance, Tuning.walkLimitMinutes);
        }

        /// <summary>
        /// The director takes the band at its members' poses (the stand-ins spawn there when a player is near), and the
        /// residents go Away in this same call: the swap is one server tick. False when the director refused (it says why): the
        /// band keeps to its walk-out and is handed off again on the next pass.
        /// </summary>
        private bool HandOff(ExpeditionDirector director, Performance performance)
        {
            ExpeditionRecord band = performance.Band;
            var middle = new Pose(ExpeditionRules.Centroid(poses), performance.Muster.rotation);
            var byMember = new Pose[band.members.Length];
            for (int i = 0; i < byMember.Length; i++) byMember[i] = middle;
            foreach (Performer performer in performance.Cast)
                byMember[performer.Member] = new Pose(performer.Resident.transform.position, performer.Resident.transform.rotation);

            if (!director.BeginHandOff(band.id, byMember)) return false;

            performances.Remove(band.id);
            Release(performance);
            foreach (Performer performer in performance.Cast) performer.Resident.GoAway();
            return true;
        }

        // ── Walk-in (spec §10.2, Phase 1: no news, no horn) ───────────────────

        private void WalkIn(ExpeditionRecord band)
        {
            if (!Society.TryMusterPose(profile.musterUse, out Pose muster))
            {
                // Nowhere to walk to: the band is home where it comes back, at the road point.
                WarnOnce(band.id + "/home", $"[Expedition] {name} has no usable muster place, so band '{band.id}' is home at its " +
                         "road point without walking in.");
                muster = new Pose(OnGroundOrAsIs(band.handoffPoint), Quaternion.identity);
            }

            Performance performance = Cast(band, ExpeditionPhase.Returning);
            performance.Act = Act.Appear;
            performance.Since = Now;
            performance.Muster = muster;
            Appear(performance);
        }

        /// <summary>
        /// The residents take the stand-ins' places: where each stand-in stands when the band is spawned, in a row at the road
        /// point when it is folded and nobody watches that point (else the next pass looks again). The band's group goes in the
        /// same call, so nobody is seen twice; the director re-creates no group for a performed band walking in.
        /// </summary>
        private void Appear(Performance performance)
        {
            NpcWorldSim sim = NpcWorldSim.Instance;
            NpcGroup group = sim != null ? sim.FindGroup(performance.Band.groupId) : null;
            bool spawned = group != null && group.Spawned;
            if (!spawned && AnyoneSees(OnGroundOrAsIs(performance.Band.handoffPoint), Tuning.observeRadius)) return;

            Dictionary<string, Transform> standIns = spawned ? StandInsOf(group) : null;
            Pose[] road = RoadRow(performance);
            for (int i = 0; i < performance.Cast.Count; i++)
            {
                Performer performer = performance.Cast[i];
                string key = performance.Band.members[performer.Member].residentKey;
                Pose at = standIns != null && standIns.TryGetValue(key, out Transform body) ? new Pose(body.position, body.rotation) : road[i];

                CarryKit(performance.Band, performer);
                performer.Resident.ComeHome(at.position, at.rotation);
                // Shown with the kit drawn at once, as its stand-in was, not after the routine's next pass.
                if (performer.Resident.Presence != null) performer.Resident.Presence.Publish(Activity.Expedition, 0, false);
            }
            if (group != null) sim.DisbandGroup(group.Id);

            performance.Act = Act.WalkIn;
            performance.Since = Now;
            performance.Stands = ExpeditionRules.MusterRow(performance.Muster, performance.Cast.Count, Tuning.musterSpacing);
            Hold(performance, Tuning.walkLimitMinutes);
        }

        private static Dictionary<string, Transform> StandInsOf(NpcGroup group)
        {
            var byKey = new Dictionary<string, Transform>();
            foreach (GameObject body in group.Live)
                if (body != null && body.TryGetComponent(out ExpeditionMember member) && member.IsStandIn)
                    byKey[member.Identity.residentKey] = body.transform;
            return byKey;
        }

        /// <summary>A row across the road at the hand-off point, facing home.</summary>
        private Pose[] RoadRow(Performance performance)
        {
            Vector3 centre = OnGroundOrAsIs(performance.Band.handoffPoint);
            Vector3 home = performance.Muster.position - centre;
            home.y = 0f;
            Quaternion facing = home.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(home) : performance.Muster.rotation;

            Pose[] row = ExpeditionRules.MusterRow(new Pose(centre, facing), performance.Cast.Count, Tuning.musterSpacing);
            for (int i = 0; i < row.Length; i++) row[i].position = OnGroundOrAsIs(row[i].position);
            return row;
        }

        private void WalkingIn(ExpeditionDirector director, Performance performance)
        {
            CollectPoses(performance);
            float distance = ExpeditionRules.FlatDistance(ExpeditionRules.Centroid(poses), performance.Muster.position);
            double minutes = Now - performance.Since;

            if (performance.Cast.Count > 0 && !ExpeditionRules.WalkedIn(distance, minutes, Tuning))
            {
                Hold(performance, Tuning.walkLimitMinutes - minutes);
                return;
            }

            performances.Remove(performance.Band.id);
            Release(performance);
            director.CompleteHomecoming(performance.Band.id);
            ApplyFinished(director);
        }

        // ── Away, the dead and the roster ─────────────────────────────────────

        /// <summary>
        /// Every resident the director has out is Away where it stands, with no walk; one back from a band that ended without
        /// a walk-in (a load resolved it) comes out where it stands. The band's own performers are left to their performance.
        /// </summary>
        private void ApplyAbsence()
        {
            foreach (Resident resident in Society.Residents)
            {
                if (resident == null || IsPerforming(resident)) continue;

                if (ExpeditionDirector.IsAway(settlementId, SettlementSociety.ResidentKey(resident))) resident.GoAway();
                else if (resident.IsAway && !resident.IsDead) resident.ComeHome(resident.transform.position, resident.transform.rotation);
            }
        }

        /// <summary>
        /// A finished band's dead die at home, as a restored death (nothing dropped, no fall replayed); then the band is
        /// retired. A dead member with no resident here is reported: the band is kept while the world's catalog still lists
        /// that resident, so a load that finds the body still applies the death, and retired once it does not (the resident
        /// is gone, ExpeditionRules.MayRetire).
        /// </summary>
        private void ApplyFinished(ExpeditionDirector director)
        {
            foreach (ExpeditionRecord band in director.BandsOf(settlementId))
            {
                if (ExpeditionRules.IsUnderway(band)) continue;

                IReadOnlyList<RosterEntry> roster = director.CatalogRosterOf(settlementId);
                foreach (MemberRecord member in band.members)
                {
                    if (!member.dead) continue;

                    if (TryFind(member.residentKey, out Resident resident)) DieAtHome(resident);
                    else if (ExpeditionRules.RosterLists(roster, member.residentKey))
                        WarnOnce(band.id + "/dead/" + member.residentKey, $"[Expedition] Band '{band.id}': member " +
                                 $"'{member.residentKey}' died, but {name} has no such resident to die at home. The band is kept " +
                                 "until a load finds the body: the world's site catalog still lists the resident.");
                    else
                        WarnOnce(band.id + "/dead/" + member.residentKey, $"[Expedition] Band '{band.id}': member " +
                                 $"'{member.residentKey}' died, but {name} has no such resident and the world's site catalog " +
                                 "lists none either: the resident is gone, and the band is retired without its death.");
                }

                if (ExpeditionRules.MayRetire(band, key => TryFind(key, out _), roster)) director.Retire(band.id);
            }
        }

        private void DieAtHome(Resident resident)
        {
            if (resident.IsDead) return;

            if (!resident.TryGetComponent(out HealthComponent health))
            {
                Debug.LogError($"[Expedition] {resident.name} died with its band but has no HealthComponent to record it; it stays away.", resident);
                return;
            }

            resident.GoAway();
            health.RestoreHealth(0);
        }

        /// <summary>The living residents, as roster rows, whenever their number changed (or a load replaced the director's copy).</summary>
        private void ReportRoster(ExpeditionDirector director)
        {
            int living = 0;
            foreach (Resident resident in Society.Residents)
                if (resident != null && !resident.IsDead) living++;
            if (living == reportedLiving) return;
            reportedLiving = living;

            var rows = new List<RosterEntry>(living);
            ExpeditionCatalog catalog = ExpeditionCatalog.Instance;
            foreach (Resident resident in Society.Residents)
                if (resident != null && !resident.IsDead)
                    rows.Add(RosterEntry.Of(resident, settlement.Culture, catalog.GuidOf(resident.sourcePrefab)));
            director.ReportRoster(settlementId, rows.ToArray());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>A performance of <paramref name="band"/> by the residents of its living members, registered under its id.</summary>
        private Performance Cast(ExpeditionRecord band, ExpeditionPhase phase)
        {
            var performance = new Performance { Band = band };
            for (int i = 0; i < band.members.Length; i++)
            {
                MemberRecord member = band.members[i];
                if (member.dead) continue;
                if (!TryFind(member.residentKey, out Resident resident) || resident.IsDead)
                {
                    WarnOnce(band.id + "/" + member.residentKey, $"[Expedition] Band '{band.id}' ({phase}): no living resident " +
                             $"'{member.residentKey}' in {name}, so nobody plays that member here.");
                    continue;
                }

                if (!resident.TryGetComponent(out ExpeditionMember kit))
                    WarnOnce("kit/" + resident.name, $"[Expedition] {resident.name} has no ExpeditionMember, so it musters and walks " +
                             "without its kit. Run Tools/SpaceGame/Expeditions/Prepare Resident Prefabs.");
                performance.Cast.Add(new Performer { Member = i, Resident = resident, Kit = kit });
            }

            performances[band.id] = performance;
            return performance;
        }

        private void CarryKit(ExpeditionRecord band, Performer performer)
        {
            if (performer.Kit != null) performer.Kit.CarryKit(profile, band.goalId, band.members[performer.Member].kitIndex);
        }

        /// <summary>Keeps every performer walking to, or standing at, its stand for <paramref name="minutes"/> more game minutes.</summary>
        private static void Hold(Performance performance, double minutes)
        {
            float seconds = ResidentTuning.Instance.GameMinutesToSeconds((float)minutes);
            for (int i = 0; i < performance.Cast.Count; i++)
                performance.Cast[i].Resident.SetOverride(OverrideKind.Scripted, performance.Stands[i].position, seconds);
        }

        /// <summary>The performers go back to their day: no override, kit put away. Bodies gone with the chunk are skipped.</summary>
        private static void Release(Performance performance)
        {
            foreach (Performer performer in performance.Cast)
            {
                if (performer.Resident == null) continue;

                performer.Resident.SetOverride(OverrideKind.None, performer.Resident.transform.position, 0f);
                if (performer.Kit != null) performer.Kit.PutKitAway();
            }
        }

        /// <summary>A performance whose band changed under it (a load, a debug command): its residents go back to what the director says.</summary>
        private void Abandon(Performance performance)
        {
            performances.Remove(performance.Band.id);
            Release(performance);
            ApplyAbsence();
        }

        private bool IsPerforming(Resident resident)
        {
            foreach (Performance performance in performances.Values)
                if (performance.Cast.Exists(p => p.Resident == resident)) return true;
            return false;
        }

        private void CollectPoses(Performance performance)
        {
            poses.Clear();
            foreach (Performer performer in performance.Cast)
                poses.Add(new Pose(performer.Resident.transform.position, performer.Resident.transform.rotation));
        }

        private static bool AnyoneSees(Performance performance, float radius)
        {
            foreach (Performer performer in performance.Cast)
                if (AnyoneSees(performer.Resident.transform.position, radius)) return true;
            return false;
        }

        private static bool AnyoneSees(Vector3 feet, float radius) => ObserverCheck.AnyPlayerSees(feet + Vector3.up * ObserverCheck.ChestHeight, radius);

        private bool TryFind(string residentKey, out Resident found)
        {
            foreach (Resident resident in Society.Residents)
                if (resident != null && SettlementSociety.ResidentKey(resident) == residentKey)
                {
                    found = resident;
                    return true;
                }

            found = null;
            return false;
        }

        private static bool OnGround(Vector3 point, out Vector3 ground)
        {
            bool found = NavMesh.SamplePosition(point, out NavMeshHit hit, Tuning.destinationSampleDistance, NavMesh.AllAreas);
            ground = found ? hit.position : point;
            return found;
        }

        private static Vector3 OnGroundOrAsIs(Vector3 point)
        {
            OnGround(point, out Vector3 ground);
            return ground;
        }

        private void WarnOnce(string key, string message)
        {
            if (warned.Add(key)) Debug.LogWarning(message, this);
        }
    }
}
