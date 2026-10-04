// The people of one settlement at runtime: who lives there, the places its buildings brought along, how
// long it takes to walk between them, and the day plans built from all of it. Owned by the Settlement
// component — the one script a settlement needs — and driven by its Unity callbacks. A plain class, so a
// settlement's layout and its life stay two modules behind one component.
//
// Places are gathered, never baked: every Dwelling's door, a camp for each resident with no dwelling,
// every SettlementSpot the generated buildings and decorations brought along — in that order, so an index
// names the same place on every machine — and, only where plans are built, seeded trip points around the
// settlement and a ring of patrol points just outside its buildings, last. Walking times are measured on the
// NavMesh the first time a plan asks for them. Who walks with whom is derived here too, from the roster alone.
//
// Plans are never saved and never patched. Today's and yesterday's (a night shift belongs to the day it
// started) are rebuilt from the places and seed ⊕ day whenever the answer could have changed — start, a
// loaded save, a new day, a time jump, residents leaving with a band or coming home (OnAbsenceChanged) — so
// reload, late join and skipping time are all one operation.
// Plans are built where the server decides; everything else here is cheap lookups.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    public sealed class SettlementSociety
    {
        // Attempts per wanted trip point before the settlement makes do with fewer.
        private const int TripAttemptsPerPoint = 8;
        // How far from a seeded trip point the NavMesh may lie — mostly height, on rolling dunes.
        private const float TripSnapRadius = 30f;
        // How far from a ring point the NavMesh may lie, and the nearest two kept ring points may be.
        private const float PerimeterSnapRadius = 8f, PerimeterMinGap = 3f;
        // Fewer ring points than this is no route to walk: the guards stay on the plan's other activities.
        private const int MinPerimeterPoints = 3;
        // A body this close (in 3D) to an elevated post is standing on it; a deck is metres above the ground beneath.
        private const float DeckReach = 3f;
        // The world NavMesh counts as loaded when the heart is on it within this many metres.
        private const float HeartProbeRadius = 2f;

        private readonly Settlement settlement;
        private readonly SettlementCulture culture;
        private readonly Conversations conversations;
        private readonly Rumours rumours;
        private readonly Dictionary<int, DayPlan[]> plansByDay = new();
        private readonly Dictionary<ulong, float> lastRemark = new();
        private readonly Dictionary<long, float> travelCache = new();
        private readonly Dictionary<Dwelling, int> doorOf = new();
        private readonly Dictionary<Resident, int> campOf = new();
        private readonly Dictionary<Resident, int> bedOf = new();
        private readonly Dictionary<SettlementSpot, int> placeOfSpot = new();
        private readonly Dictionary<int, SettlementSpot> spotOfPlace = new();
        private readonly List<int> spotPlaces = new();
        private readonly Dictionary<CharacterCue, float?> reachOfCue = new();
        private readonly HashSet<int> reportedUnusable = new();
        private readonly HashSet<int> reportedSeatless = new();
        private readonly NavMeshPath path = new();
        private List<SettlementPlace> places;
        private Resident[] residents;
        private LineTable lineTable;
        private int builtDay = int.MinValue;
        private bool tripsFound, perimeterFound;
        private bool standsUnsettled;
        private float nextStandCheck;
        private readonly List<int> patrolPoints = new();
        private Dictionary<int, int> leaderOf = new();
        private Dictionary<int, int> patrolSlot = new();
        private int patrolSlots;
        private float ringRadius;

        public static event Action<SettlementSociety> PlansRebuilt;

        /// <summary>
        /// Server: is this resident (by settlement id and <see cref="ResidentKey"/>) out with a band? Set by whatever keeps the
        /// bands, which outlives every settlement, so the residents never name it; null = nobody is away. An away resident
        /// gets no day and walks with nobody.
        /// </summary>
        public static Func<string, string, bool> AbsenceQuery;

        private static event Action<string> AbsenceReported;

        public SettlementSociety(Settlement settlement, SettlementCulture culture)
        {
            this.settlement = settlement;
            this.culture = culture;
            conversations = new Conversations(this);
            rumours = new Rumours(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlansRebuilt = null;
            AbsenceQuery = null;
            AbsenceReported = null;
        }

        /// <summary>Whoever answers <see cref="AbsenceQuery"/> reports here that its answer changed for a settlement, which re-plans.</summary>
        public static void OnAbsenceChanged(string settlementId) => AbsenceReported?.Invoke(settlementId);

        /// <summary>How a band's record names a resident of a settlement: <c>r:</c> and its roster index.</summary>
        public static string ResidentKey(Resident resident) => Expeditions.ResidentKey.ForAuthored(resident.index);

        public Settlement Settlement => settlement;
        public SettlementCulture Culture => culture;
        public string Name => settlement.name;
        public IReadOnlyList<Resident> Residents => EnsureResidents();
        public int Day => DayNightCycle.Main ? DayNightCycle.Main.Day : 0;
        public double NowMinutes => DayNightCycle.Main ? DayNightCycle.Main.GameMinutesNow : 0d;
        public LineTable Lines => EnsureLines();
        /// <summary>Resident-to-resident talks opened so far (server only; 0 elsewhere).</summary>
        public int ConversationsOpened => conversations.Opened;

        public void Enable()
        {
            DayNightCycle.AnchorMoved += HandleAnchorMoved;
            SaveManager.OnLoadApplied += RebuildWhereDecided;
            AbsenceReported += HandleAbsenceChanged;
        }

        public void Disable()
        {
            DayNightCycle.AnchorMoved -= HandleAnchorMoved;
            SaveManager.OnLoadApplied -= RebuildWhereDecided;
            AbsenceReported -= HandleAbsenceChanged;
        }

        public void Start() => RebuildWhereDecided();

        public void Tick()
        {
            if (!Network.Decides) return;

            if (Day != builtDay)
            {
                int ended = builtDay;
                RebuildPlans();
                if (EndsADay(ended, Day)) EndDay(ended);
            }
            RecheckStands();
            conversations.Tick();
            rumours.Tick();
        }

        // Stand points are measured on the NavMesh, which streams in: while some are unmeasured or unusable, look again
        // now and then, and re-plan when one changed its mind — a spot that was only waiting for its ground is not lost.
        private void RecheckStands()
        {
            if (!standsUnsettled || Time.time < nextStandCheck) return;

            nextStandCheck = Time.time + ResidentTuning.Instance.standRecheckSeconds;
            if (RefreshStands()) RebuildPlans();
        }

        /// <summary>
        /// True when plans built for <paramref name="builtDay"/> are giving way to
        /// <paramref name="today"/> because that day ran out — not the first build (built for
        /// <see cref="int.MinValue"/>) and not a multi-day skip. A load and a time jump rebuild outside
        /// <see cref="Tick"/> and never get here.
        /// </summary>
        public static bool EndsADay(int builtDay, int today) => builtDay == today - 1;

        // The day's close: kin at bedtime and friends who shared the hearth retell what they saw that
        // day. Yesterday's plans were just rebuilt, so who sat at the hearth is there to be read.
        private void EndDay(int day)
        {
            Gossip.SpreadAtBedtime(this, day);
            Gossip.SpreadAtHearth(this, day);
        }

        /// <summary>How many places there are — trip points included once plans have been built here.</summary>
        public int PlaceCount
        {
            get
            {
                EnsurePlaces();
                return places.Count;
            }
        }

        public SettlementPlace Place(int index)
        {
            if (index >= HouseRoom.PlaceBase) return HouseRoom.PlaceAt(index);
            EnsurePlaces();
            return index >= 0 && index < places.Count ? places[index] : null;
        }

        /// <summary>True when <paramref name="point"/> is within a body's reach of an elevated post: standing up on a tower deck.</summary>
        public bool OnDeck(Vector3 point)
        {
            EnsurePlaces();
            foreach (SettlementPlace place in places)
                if (place.Elevated && (place.Position - point).sqrMagnitude <= DeckReach * DeckReach) return true;
            return false;
        }

        /// <summary>The perimeter ring's place indices in walking order; empty until plans have been built here.</summary>
        public IReadOnlyList<int> PatrolPoints => patrolPoints;

        /// <summary>Metres from the heart an amble may roam: the ring's mean radius times the tuning's reach; 0 with no ring.</summary>
        public float AmbleRadius => ringRadius * ResidentTuning.Instance.ambleReach;

        /// <summary>The patrol pair this resident belongs to, in roster order; 0 for a resident that does not patrol.</summary>
        public int PatrolSlotOf(Resident resident) => resident != null && patrolSlot.TryGetValue(resident.index, out int slot) ? slot : 0;

        /// <summary>Every shared stop of this use, in place order — the wells, the plants, the piles.</summary>
        public List<int> ErrandPlaces(SpotUse use)
        {
            EnsurePlaces();
            var stops = new List<int>();
            for (int i = 0; i < places.Count; i++)
                if (places[i].Kind == PlaceKind.Errand && places[i].Use == use && places[i].Usable) stops.Add(i);
            return stops;
        }

        /// <summary>The first usable assembly place of this use — where a band musters — or null when the settlement has none.</summary>
        public SettlementPlace AssemblyPlace(SpotUse use)
        {
            int index = AssemblyIndex(use);
            return index >= 0 ? places[index] : null;
        }

        /// <summary>
        /// Where a band of this use musters: the assembly place's stand point, turned out along its spot's +Z (the way the band
        /// leaves). False when the settlement has no usable one — a missing muster spot must not read as the world origin.
        /// </summary>
        public bool TryMusterPose(SpotUse use, out Pose pose)
        {
            int index = AssemblyIndex(use);
            if (index < 0)
            {
                pose = default;
                return false;
            }

            pose = new Pose(places[index].Position, Quaternion.LookRotation(SettlementPlaces.OutwardOf(spotOfPlace[index].transform)));
            return true;
        }

        private int AssemblyIndex(SpotUse use)
        {
            EnsurePlaces();
            for (int i = 0; i < places.Count; i++)
                if (places[i].Kind == PlaceKind.Assembly && places[i].Use == use && places[i].Usable &&
                    spotOfPlace.TryGetValue(i, out SettlementSpot spot) && spot != null)
                    return i;
            return -1;
        }

        /// <summary>False for a spot whose stand point was measured and cannot be stood at; the planner never sends anyone there.</summary>
        public bool IsUsable(int index)
        {
            SettlementPlace place = Place(index);
            return place != null && place.Usable;
        }

        /// <summary>The place index of a generated spot, or -1 when it is not one of this settlement's places.</summary>
        public int PlaceOf(SettlementSpot spot)
        {
            EnsurePlaces();
            return spot != null && placeOfSpot.TryGetValue(spot, out int index) ? index : -1;
        }

        /// <summary>True the first time it is asked about this place: a sit with no Seat is reported once, not by every resident every half second.</summary>
        public bool ReportOnce(int seatlessPlace) => reportedSeatless.Add(seatlessPlace);

        /// <summary>The spot behind a place index, or null for a door, camp, trip or ring point.</summary>
        public SettlementSpot SpotAt(int place)
        {
            if (place >= HouseRoom.PlaceBase) return HouseRoom.SpotAt(place);
            EnsurePlaces();
            return spotOfPlace.TryGetValue(place, out SettlementSpot spot) ? spot : null;
        }

        /// <summary>The resident this one walks beside and follows, or null when it leads or walks alone.</summary>
        public Resident LeaderOf(Resident resident) =>
            resident != null && leaderOf.TryGetValue(resident.index, out int leader) ? ResidentAt(leader) : null;

        /// <summary>The resident that follows this one, or null when none does.</summary>
        public Resident FollowerOf(Resident resident)
        {
            if (resident == null) return null;
            foreach (KeyValuePair<int, int> pair in leaderOf)
                if (pair.Value == resident.index) return ResidentAt(pair.Key);
            return null;
        }

        /// <summary>The index of the resident's door, or of its camp when it has no dwelling; -1 for a stranger.</summary>
        public int HomeOf(Resident resident)
        {
            EnsurePlaces();
            if (resident == null) return -1;
            if (resident.home != null && doorOf.TryGetValue(resident.home, out int door)) return door;
            return campOf.TryGetValue(resident, out int camp) ? camp : -1;
        }

        /// <summary>
        /// The place index of the bed this resident sleeps in, or -1 when its dwelling has no bed spots, or its bed cannot be stood at (it
        /// goes to its door and indoors instead, as a nomad does).
        /// </summary>
        public int BedOf(Resident resident)
        {
            EnsurePlaces();
            return resident != null && bedOf.TryGetValue(resident, out int bed) && places[bed].Usable ? bed : -1;
        }

        /// <summary>The building a spot's place belongs to: the child of the generated root that holds it. Null for a door, camp, trip or ring point.</summary>
        public Transform BuildingOf(int place)
        {
            SettlementSpot spot = SpotAt(place);
            Transform generated = settlement.GeneratedRoot;
            if (spot == null || generated == null) return null;

            Transform building = spot.transform;
            while (building.parent != null && building.parent != generated) building = building.parent;
            return building;
        }

        /// <summary>The resident with this <see cref="Resident.index"/>, or null when there is none.</summary>
        public Resident ResidentAt(int index)
        {
            Resident[] roster = EnsureResidents();
            if (index >= 0 && index < roster.Length && roster[index] != null && roster[index].index == index) return roster[index];
            return Array.Find(roster, r => r != null && r.index == index);
        }

        /// <summary>Game minutes a resident takes to walk between two places, measured along the NavMesh once.</summary>
        public float TravelMinutes(int from, int to)
        {
            if (from == to) return 0f;

            long key = from < to ? ((long)from << 32) | (uint)to : ((long)to << 32) | (uint)from;
            if (travelCache.TryGetValue(key, out float minutes)) return minutes;

            SettlementPlace a = Place(from), b = Place(to);
            if (a == null || b == null) return 0f;

            ResidentTuning tuning = ResidentTuning.Instance;
            float metresPerGameMinute = tuning.walkSpeed * tuning.GameMinutesToSeconds(1f);
            minutes = WalkedMetres(a.Position, b.Position, tuning.detourFactor) / metresPerGameMinute;
            travelCache[key] = minutes;
            return minutes;
        }

        /// <summary>Today's plan, or yesterday's while today's has not started (the night shift).</summary>
        public DayPlan PlanFor(Resident resident)
        {
            if (!resident) return null;

            DayPlan today = PlanFor(resident.index, Day);
            if (today != null && today.segments.Count > 0 && NowMinutes < today.FirstStart)
                return PlanFor(resident.index, Day - 1) ?? today;
            return today;
        }

        public DayPlan PlanFor(int residentIndex, int day)
        {
            if (!plansByDay.TryGetValue(day, out DayPlan[] plans)) plans = Build(day);
            foreach (DayPlan plan in plans)
                if (plan != null && plan.residentIndex == residentIndex) return plan;
            return null;
        }

        public void RebuildPlans()
        {
            RefreshStands();
            EnsureTrips();
            EnsurePerimeter();
            EnsureCompanions();
            plansByDay.Clear();
            travelCache.Clear();
            builtDay = Day;
            Build(builtDay);
            Build(builtDay - 1);
            PlansRebuilt?.Invoke(this);
        }

        /// <summary>True (and starts the gap) when this player has not been remarked at within <paramref name="gap"/>.</summary>
        public bool TryRemark(ulong playerId, float now, float gap)
        {
            if (lastRemark.TryGetValue(playerId, out float last) && now - last < gap) return false;
            lastRemark[playerId] = now;
            return true;
        }

        private void HandleAnchorMoved(DayNightCycle cycle)
        {
            if (cycle == DayNightCycle.Main) RebuildWhereDecided();
        }

        private void RebuildWhereDecided()
        {
            if (Network.Decides) RebuildPlans();
        }

        private void HandleAbsenceChanged(string settlementId)
        {
            if (!string.IsNullOrEmpty(settlementId) && settlementId == settlement.SettlementId) RebuildWhereDecided();
        }

        // Out with a band, by the word of whoever keeps the bands. Asked only when something answers.
        private bool IsAway(Resident resident) =>
            AbsenceQuery != null && AbsenceQuery(settlement.SettlementId, ResidentKey(resident));

        private DayPlan[] Build(int day)
        {
            DayPlan[] plans = DayPlanner.BuildAll(settlement.Seed, day, PlannerResidents(), PlannerPlaces(), TravelMinutes,
                                                  ResidentTuning.Instance);
            plansByDay[day] = plans;
            return plans;
        }

        // A destroyed resident is left out: the planner treats the gap as a vacancy, like a dead one.
        private List<PlannerResident> PlannerResidents()
        {
            Resident[] roster = EnsureResidents();
            var rows = new List<PlannerResident>(roster.Length);
            foreach (Resident resident in roster)
            {
                if (!resident) continue;
                ResidentArchetype archetype = resident.archetype;
                leaderOf.TryGetValue(resident.index, out int leader);
                bool follows = leaderOf.ContainsKey(resident.index);
                Resident lead = follows ? ResidentAt(leader) : null;
                bool paired = follows || FollowerOf(resident) != null;
                patrolSlot.TryGetValue(resident.index, out int slot);
                rows.Add(new PlannerResident
                {
                    index = resident.index,
                    homeIndex = HomeOf(resident),
                    bedPlusOne = BedOf(resident) + 1,
                    freeTimeReach = culture != null ? culture.freeTimeReachMinutes : 0f,
                    seed = resident.seed,
                    lifestyle = resident.Lifestyle,
                    post = archetype ? archetype.post : null,
                    trips = archetype ? archetype.trips : TripKind.None,
                    chore = archetype ? archetype.chore : null,
                    duty = archetype ? archetype.duty : ResidentDuty.None,
                    paired = paired,
                    slot = slot,
                    slots = patrolSlots,
                    shareSeed = lead ? lead.seed : 0,
                    bedtimeOffset = lead ? lead.bedtimeOffset : resident.bedtimeOffset,
                    dead = resident.IsDead,
                    away = IsAway(resident),
                    friends = resident.CloseTo(),
                });
            }
            return rows;
        }

        private List<PlannerPlace> PlannerPlaces()
        {
            EnsurePlaces();
            var rows = new List<PlannerPlace>(places.Count);
            for (int i = 0; i < places.Count; i++)
            {
                SettlementPlace place = places[i];
                rows.Add(new PlannerPlace
                {
                    index = i, kind = place.Kind, post = place.Kind is PlaceKind.Post or PlaceKind.Errand ? place.Use : null,
                    seatIndex = place.SeatIndex, group = place.Group, unusable = !place.Usable,
                });
            }
            return rows;
        }

        private Resident[] EnsureResidents()
        {
            if (residents != null) return residents;

            var found = new List<Resident>();
            foreach (Resident resident in settlement.GetComponentsInChildren<Resident>(true))
                if (resident.Settlement == settlement) found.Add(resident);
            found.Sort((a, b) => a.index.CompareTo(b.index));
            residents = found.ToArray();
            return residents;
        }

        private void EnsurePlaces()
        {
            if (places != null) return;

            places = new List<SettlementPlace>();
            Transform generated = settlement.GeneratedRoot;
            if (generated != null)
                foreach (Dwelling dwelling in generated.GetComponentsInChildren<Dwelling>())
                {
                    doorOf[dwelling] = places.Count;
                    places.Add(DoorPlace(dwelling));
                }

            foreach (Resident resident in EnsureResidents())
            {
                if (resident == null || (resident.home != null && doorOf.ContainsKey(resident.home))) continue;
                campOf[resident] = places.Count;
                places.Add(new SettlementPlace(PlaceKind.Camp, null, 0, 0, resident.campPosition, null));
            }

            if (generated != null)
            {
                AddSpots(generated);
                AssignBeds();
            }
            RefreshStands();
        }

        // Measures every spot's stand point and every door's threshold on the NavMesh (server only: it is the only
        // machine that walks). True when a spot became usable or unusable since the last look. Leaves things as they
        // were, and tries again later, while the world NavMesh is not under the settlement yet.
        private bool RefreshStands()
        {
            if (!Network.Decides || places == null || !NavMesh.SamplePosition(settlement.WalkableHeart, out _, HeartProbeRadius, NavMesh.AllAreas))
                return false;

            Vector3 heart = settlement.WalkableHeart;
            foreach (KeyValuePair<Dwelling, int> door in doorOf)
                places[door.Value].SetThreshold(SettlementPlaces.TryThreshold(door.Key, heart, out Vector3 threshold) ? threshold : null);

            bool flipped = false;
            standsUnsettled = false;
            foreach (int index in spotPlaces)
            {
                SettlementSpot spot = spotOfPlace[index];
                if (spot == null) continue;

                bool usable = TryStandAt(places[index], spot, heart, out Vector3 stand, out string why);
                flipped |= places[index].Resolve(usable ? stand : spot.Position, usable);
                if (usable) continue;

                standsUnsettled = true;
                if (Application.isPlaying && reportedUnusable.Add(index))
                    Debug.LogWarning($"[Settlement] {Name}: {SettlementPlaces.PathBelow(settlement.GeneratedRoot, spot.transform)} " +
                                     $"({spot.Use.name}) is unusable and skipped by the day planner: {why}.", spot);
            }
            return flipped;
        }

        // The stand point of a spot: where its work's measured reach lands the tool on the thing it names, when it names one and
        // some of its loops have a measured reach (StationStand), else where it was authored. The derived point is only taken when
        // it is walkable and no farther from the target than the authored one (the NavMesh may have pushed it back out of the prop).
        private bool TryStandAt(SettlementPlace place, SettlementSpot spot, Vector3 heart, out Vector3 stand, out string why)
        {
            if (place.HasTarget && !place.Seated && StationReachOf(place.HoldCue, out float forward))
            {
                ResidentTuning tuning = ResidentTuning.Instance;
                Vector3 derived = StationStand.Derive(spot.Position, spot.FacePoint, forward, tuning.stationMaxShift, tuning.stationMinDistance);
                if (derived != spot.Position
                    && SettlementPlaces.TryStand(derived, spot.Use.elevated, heart, out stand, out why)
                    && Flat(stand - spot.FacePoint) <= Flat(spot.Position - spot.FacePoint))
                    return true;
            }
            return SettlementPlaces.TryStand(spot.Position, spot.Use.elevated, heart, out stand, out why);
        }

        private bool StationReachOf(CharacterCue cue, out float forward)
        {
            forward = 0f;
            if (cue == null) return false;
            if (!reachOfCue.TryGetValue(cue, out float? known))
            {
                CharacterActionCatalog catalog = CharacterActionCatalog.Default;
                known = catalog != null && StationStand.TryForwardReach(catalog.ActionsFor(cue), out float mean, out _) ? mean : (float?)null;
                reachOfCue[cue] = known;
            }
            forward = known ?? 0f;
            return known.HasValue;
        }

        private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        // The first point straight out from the doorway a resident can walk to from the settlement's heart.
        private SettlementPlace DoorPlace(Dwelling dwelling)
        {
            bool reached = SettlementPlaces.TryDoorStand(dwelling, settlement.WalkableHeart,
                                                         ResidentTuning.Instance.doorStandDistances, out Vector3 stand);
            // Only a running game has the world NavMesh to judge by; edit-mode tools add it themselves.
            if (!reached && Application.isPlaying && Network.Decides)
                Debug.LogWarning($"[Settlement] {Name}: nothing walkable out from the door of {dwelling.name}; " +
                                 "its residents are snapped home there. Regenerate, or fix the prefab's entrance.", dwelling);
            Vector3 outward = SettlementPlaces.OutwardOf(SettlementPlaces.DoorwayOf(dwelling));
            var door = new SettlementPlace(PlaceKind.Door, null, 0, 0, stand, stand + outward);
            door.SetThreshold(SettlementPlaces.TryThreshold(dwelling, settlement.WalkableHeart, out Vector3 threshold) ? threshold : null);
            return door;
        }

        private void AddSpots(Transform generated)
        {
            var circles = new Dictionary<(Transform, string), int>();
            var seatsOfUse = new Dictionary<SpotUse, int>();
            foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>())
            {
                if (spot.Use == null) continue;   // reported by Generate

                int group = 0;
                if (!string.IsNullOrEmpty(spot.Group))
                {
                    var circle = (spot.transform.parent, spot.Group);
                    if (!circles.TryGetValue(circle, out group)) circles[circle] = group = circles.Count + 1;
                }
                seatsOfUse.TryGetValue(spot.Use, out int seat);
                seatsOfUse[spot.Use] = seat + 1;
                placeOfSpot[spot] = places.Count;
                spotOfPlace[places.Count] = spot;
                spotPlaces.Add(places.Count);
                PlaceKind kind = spot.Use.sleeps ? PlaceKind.Bed : KindOf(spot.Use.role);
                places.Add(new SettlementPlace(kind, spot.Use, group, seat, spot.Position, spot.FacePoint, spot.HoldCue, spot.HasTarget));
            }
        }

        // Each resident of a dwelling that has bed spots takes one, in roster order. A dwelling with fewer beds than people shares the
        // last ones round (the validator names it); a dwelling with none keeps its residents on the door-then-offstage bedtime.
        private void AssignBeds()
        {
            var bedsOfDwelling = new Dictionary<Dwelling, List<int>>();
            foreach (int index in spotPlaces)
            {
                if (places[index].Kind != PlaceKind.Bed) continue;

                Dwelling dwelling = spotOfPlace[index].GetComponentInParent<Dwelling>();
                if (dwelling == null) continue;
                if (!bedsOfDwelling.TryGetValue(dwelling, out List<int> beds)) bedsOfDwelling[dwelling] = beds = new List<int>();
                beds.Add(index);
            }

            var taken = new Dictionary<Dwelling, int>();
            foreach (Resident resident in EnsureResidents())
            {
                if (resident == null || resident.home == null || !bedsOfDwelling.TryGetValue(resident.home, out List<int> beds)) continue;

                taken.TryGetValue(resident.home, out int count);
                bedOf[resident] = beds[count % beds.Count];
                taken[resident.home] = count + 1;
            }
        }

        /// <summary>The kind of place a spot of this role is gathered as; Leisure (and anything unmapped) is a stroll.</summary>
        public static PlaceKind KindOf(SpotRole role) => role switch
        {
            SpotRole.Work => PlaceKind.Post,
            SpotRole.Gathering => PlaceKind.Hearth,
            SpotRole.Errand => PlaceKind.Errand,
            SpotRole.Assembly => PlaceKind.Assembly,
            _ => PlaceKind.Stroll,
        };

        // Seeded points on a ring around the settlement that its heart can walk to. Only plans need them, and
        // only the deciding machine plans, so a client never pays for the path queries; they come last, so
        // every other place keeps one index on every machine.
        private void EnsureTrips()
        {
            EnsurePlaces();
            if (tripsFound) return;
            tripsFound = true;

            ResidentTuning tuning = ResidentTuning.Instance;
            Vector3 heart = settlement.WalkableHeart;
            var rng = new System.Random(settlement.Seed);
            for (int attempt = 0, found = 0; found < tuning.tripPoints && attempt < tuning.tripPoints * TripAttemptsPerPoint; attempt++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2d);
                float distance = Mathf.Lerp(tuning.tripDistance.x, tuning.tripDistance.y, (float)rng.NextDouble());
                Vector3 candidate = heart + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, TripSnapRadius, NavMesh.AllAreas)) continue;
                if (!NavMeshReach.CanWalk(heart, hit.position)) continue;

                places.Add(new SettlementPlace(PlaceKind.Trip, null, 0, 0, hit.position, null));
                found++;
            }
        }

        // A ring just outside the outermost buildings (never the streets that run off into the desert, nor the people),
        // snapped onto the NavMesh and kept only where the heart can walk to it. Built where plans are built, after
        // the trip points, so every other place keeps its index on every machine; a patrol never holds a place, so
        // no machine but the deciding one needs to know it.
        private void EnsurePerimeter()
        {
            EnsurePlaces();
            if (perimeterFound) return;
            perimeterFound = true;

            Transform generated = settlement.GeneratedRoot;
            if (generated == null) return;

            List<Bounds> footprints = SettlementPlaces.BuildingBounds(generated);
            if (footprints.Count == 0) return;

            var centers = Vector3.zero;
            foreach (Bounds box in footprints) centers += box.center;

            ResidentTuning tuning = ResidentTuning.Instance;
            Vector3 heart = settlement.WalkableHeart;
            Vector3 center = centers / footprints.Count;
            Vector3 last = default;
            bool any = false;
            foreach (Vector3 point in PerimeterRing.Compute(center, footprints, tuning.perimeterOffset, tuning.perimeterPoints))
            {
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, PerimeterSnapRadius, NavMesh.AllAreas)) continue;
                if (any && (hit.position - last).sqrMagnitude < PerimeterMinGap * PerimeterMinGap) continue;
                if (!NavMeshReach.CanWalk(heart, hit.position)) continue;

                patrolPoints.Add(places.Count);
                places.Add(new SettlementPlace(PlaceKind.Patrol, null, 0, 0, hit.position, null));
                last = hit.position;
                any = true;
            }

            foreach (int point in patrolPoints) ringRadius += Vector3.Distance(places[point].Position, heart) / patrolPoints.Count;
            if (patrolPoints.Count < MinPerimeterPoints)
            {
                ringRadius = 0f;
                if (Application.isPlaying)
                    Debug.LogWarning($"[Settlement] {Name}: only {patrolPoints.Count} perimeter points are walkable from the heart — " +
                                     "guards will not patrol. Check the NavMesh around the outermost buildings.", settlement);
                places.RemoveRange(places.Count - patrolPoints.Count, patrolPoints.Count);
                patrolPoints.Clear();
            }
        }

        // Guards pair off in roster order; roamers pair with a friend or relative who is also a free roamer. The dead and the
        // away walk with nobody, so their partners walk alone (or with the next guard) until they are back.
        private void EnsureCompanions()
        {
            var candidates = new List<CompanionCandidate>();
            foreach (Resident resident in EnsureResidents())
            {
                if (!resident || resident.IsDead || IsAway(resident)) continue;

                ResidentArchetype archetype = resident.archetype;
                bool patrols = archetype && archetype.duty == ResidentDuty.Patrol;
                bool roams = archetype && archetype.Lifestyle == Lifestyle.Roamer && archetype.chore == null;
                candidates.Add(new CompanionCandidate(resident.index, patrols, roams, resident.CloseTo()));
            }
            leaderOf = Companions.Pair(candidates);
            patrolSlot = Companions.PatrolSlots(candidates, out patrolSlots);
        }

        private float WalkedMetres(Vector3 from, Vector3 to, float detourFactor)
        {
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                return Vector3.Distance(from, to) * detourFactor;

            float metres = 0f, transitSeconds = 0f;
            Vector3[] corners = path.corners;
            for (int c = 1; c < corners.Length; c++)
            {
                metres += Vector3.Distance(corners[c - 1], corners[c]);
                transitSeconds += NavLinkGates.TransitSecondsBetween(corners[c - 1], corners[c]);
            }
            // A gated link (an airlock) holds a walker for its crossing: charged as the walking it would have covered in that time.
            return metres + transitSeconds * ResidentTuning.Instance.walkSpeed;
        }

        private LineTable EnsureLines()
        {
            if (lineTable != null) return lineTable;

            TextAsset text = culture != null ? culture.lines : null;
            if (!text) Debug.LogError($"[Settlement] {Name}: the culture has no line table — residents here will not speak.", settlement);
            lineTable = LineTable.Parse(text ? text.text : string.Empty);
            if (lineTable.Errors.Count > 0)
                Debug.LogError($"[Settlement] {Name}: {lineTable.Errors.Count} bad line rows:\n{string.Join("\n", lineTable.Errors)}", settlement);
            return lineTable;
        }
    }
}
