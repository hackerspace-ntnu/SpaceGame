// People calling in on a house while a player is inside it: they come in by the door, take a free seat, sit and talk
// for a while, and leave by the door when they please.
//
// The guests are the settlement's own residents, not extras. A resident free to idle (its plan says break, hearth,
// stroll or amble), unseen where it stands, is moved to the doorway of the room, and from then on this hands the routine
// a plan segment of its own: a seat in the room until it is time to go, then the doorway. Everything else is the stock
// resident — the routine walks it there, ResidentSeating sits it on the Seat, Conversations talk between two seated
// guests. On leaving it is put back outside the dwelling's door and its day plan takes over from there, so it walks on
// to wherever it was due.
//
// A resident fighting a player who went indoors follows them in: a few seconds after the player, by the time it would have
// run to the door, it steps through it and carries on the fight. A guest that is hurt or provoked stays in the room, gets
// up and fights; it leaves by the door only once it is calm again. A guest that dies stays where it fell.
//
// Server only. Other machines see a resident that moved, sat and spoke; nothing about a visit is replicated of its own.
// Nothing is saved: a visit that a quit or a reload cut short leaves a resident far from its plan, which the routine's
// settle step puts right while nobody is looking. The room being unloaded sends every guest home first.
using System;
using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SpaceGame.Agents.Residents
{
    [RequireComponent(typeof(HouseRoom))]
    public sealed class HouseVisits : MonoBehaviour
    {
        [Header("Who comes")]
        [Tooltip("Real seconds between one invitation and the next while a seat is free.")]
        [SerializeField] private Vector2 arrivalGapSeconds = new Vector2(20f, 60f);
        [Tooltip("Real seconds a guest stays seated before it gets up to leave.")]
        [SerializeField] private Vector2 staySeconds = new Vector2(90f, 240f);
        [Tooltip("Guests already sitting when the room loads, so it is lived in the moment a player walks in.")]
        [SerializeField] private Vector2Int guestsOnEntry = new Vector2Int(1, 2);

        [Header("Pursuit")]
        [Tooltip("A resident fighting the player follows them in from this far from the dwelling's door (m).")]
        [SerializeField, Min(0f)] private float pursuitRange = 30f;
        [Tooltip("How fast a pursuer is taken to cover the distance to the door (m/s): it steps in after distance / this seconds.")]
        [SerializeField, Min(0.5f)] private float pursuitSpeed = 6f;

        [Header("Coming and going")]
        [Tooltip("A player this close with line of sight counts as watching a resident vanish from, or appear in, the settlement.")]
        [SerializeField, Min(1f)] private float observedWithin = 60f;
        [Tooltip("Real seconds a guest waits for watchers to look away from the door it leaves by before it goes anyway.")]
        [SerializeField, Min(0f)] private float watchedWaitSeconds = 8f;
        [Tooltip("A guest does not arrive while a player stands this close to the doorway: it would step in on top of them.")]
        [SerializeField, Min(0f)] private float arrivalClearance = 2f;
        [Tooltip("How close to the doorway counts as at the door.")]
        [SerializeField, Min(0.3f)] private float doorArriveRadius = 1.5f;
        [Tooltip("A guest still short of the door this long after getting up is let out anyway.")]
        [SerializeField, Min(1f)] private float leaveTimeoutSeconds = 40f;
        [Tooltip("Real seconds between looks at who is here and who should come or go.")]
        [SerializeField, Min(0.1f)] private float thinkSeconds = 1f;

        private sealed class Visit
        {
            public Resident resident;
            public ResidentRoutine routine;
            public int seatPlace;
            public bool leaving;
            public float leaveAt;
            public float leavingSince;
            public float waitingSince = -1f;
        }

        private static readonly Dictionary<Resident, Visit> Visitors = new();
        private static readonly List<Transform> Players = new();

        private readonly List<Resident> gone = new();
        private readonly List<GuestCandidate> candidates = new();
        private readonly List<Resident> candidateResidents = new();
        private readonly HashSet<int> household = new();
        private readonly Dictionary<Resident, float> following = new();
        private readonly System.Random draw = new();
        private HouseRoom room;
        private Settlement host;
        private Dwelling dwelling;
        private Transform insidePlayer;
        private float nextThinkAt;
        private float nextArrivalAt;
        private bool filled;

        /// <summary>Residents calling at the house right now.</summary>
        public static int GuestCount => Visitors.Count;

        /// <summary>
        /// The plan segment a visiting resident is on: a seat until it is time to go, then the doorway. False for a resident
        /// that is not visiting. Only the deciding machine asks.
        /// </summary>
        public static bool TryGetSegment(Resident resident, out PlanSegment segment)
        {
            segment = default;
            if (resident == null || !Visitors.TryGetValue(resident, out Visit visit)) return false;

            segment = new PlanSegment
            {
                leave = float.MaxValue,
                activity = visit.leaving ? Activity.Walking : Activity.Break,
                place = visit.leaving ? HouseRoom.Active.DoorPlace : visit.seatPlace,
            };
            return true;
        }

        /// <summary>True while the resident is a guest in the room: its day plan, and any override to shelter at its own door, do not apply.</summary>
        public static bool IsVisiting(Resident resident) => resident != null && Visitors.ContainsKey(resident);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Visitors.Clear();

        private void Awake() => room = GetComponent<HouseRoom>();

        private void OnEnable() => InteriorManager.OnInteriorWillUnload += HandleWillUnload;

        private void OnDisable()
        {
            InteriorManager.OnInteriorWillUnload -= HandleWillUnload;
            SendAllHome();
            following.Clear();
            host = null;
        }

        private void HandleWillUnload(string interiorName, UnityEngine.SceneManagement.Scene scene)
        {
            if (scene == gameObject.scene) SendAllHome();
        }

        private void Update()
        {
            if (!Network.Decides || Time.time < nextThinkAt || !room.Settled) return;
            nextThinkAt = Time.time + thinkSeconds;

            if (!TryFindHost()) { SendAllHome(); return; }

            DropGone();
            MoveAlong();
            AdmitPursuers();
            if (!filled) { filled = true; SeatInitialGuests(); }
            else if (Time.time >= nextArrivalAt && Visitors.Count < room.SeatCount && TryInviteAtDoor())
                nextArrivalAt = Time.time + RandomBetween(arrivalGapSeconds);
        }

        // The settlement whose dwelling a player walked in by. Found when a player is inside and no guest holds it already.
        private bool TryFindHost()
        {
            Vector3? returnedTo = FirstPlayerInside();
            if (returnedTo == null) { following.Clear(); return false; }
            if (host != null) return true;

            Settlement best = null;
            Dwelling nearest = null;
            float bestSqr = float.PositiveInfinity;
            foreach (Settlement settlement in FindObjectsByType<Settlement>(FindObjectsSortMode.None))
            {
                Transform generated = settlement.GeneratedRoot;
                if (generated == null || !settlement.HasResidents) continue;

                foreach (Dwelling candidate in generated.GetComponentsInChildren<Dwelling>())
                {
                    float sqr = (SettlementPlaces.DoorwayOf(candidate).position - returnedTo.Value).sqrMagnitude;
                    if (sqr >= bestSqr) continue;

                    best = settlement;
                    nearest = candidate;
                    bestSqr = sqr;
                }
            }
            if (best == null) return false;

            host = best;
            dwelling = nearest;
            household.Clear();
            foreach (Resident resident in host.Society.Residents)
                if (resident != null && resident.home == dwelling) household.Add(resident.index);
            filled = false;
            nextArrivalAt = Time.time + RandomBetween(arrivalGapSeconds);
            return true;
        }

        // Where the first player standing in this interior came in from: the exterior position their visit records.
        private Vector3? FirstPlayerInside()
        {
            InteriorManager interiors = InteriorManager.Instance;
            if (interiors == null) return null;

            SessionPlayers.Collect(Players);
            foreach (Transform player in Players)
                if (interiors.TryGetVisit(player.gameObject, out InteriorManager.InteriorVisit visit) && visit.InteriorScene == gameObject.scene.name)
                {
                    insidePlayer = player;
                    return visit.ReturnPosition;
                }
            return null;
        }

        // A guest that died, is gone or went offstage is no longer a guest. It is not moved: a body stays where it fell.
        private void DropGone()
        {
            gone.Clear();
            foreach (KeyValuePair<Resident, Visit> pair in Visitors)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.IsDead || pair.Key.IsOffstage)
                    gone.Add(pair.Key);
            foreach (Resident resident in gone) Visitors.Remove(resident);
        }

        private void MoveAlong()
        {
            gone.Clear();
            foreach (KeyValuePair<Resident, Visit> pair in Visitors)
            {
                Visit visit = pair.Value;
                bool calm = IsCalm(visit.resident);
                // Time to go; or a fight or an alarm in the room, which sends a guest to the door once it is over.
                if (!visit.leaving && (Time.time >= visit.leaveAt || !calm || visit.resident.Override != OverrideKind.None))
                {
                    visit.leaving = true;
                    visit.leavingSince = Time.time;
                }
                if (visit.leaving && calm && AtDoor(visit) && MayStepOut(visit)) gone.Add(pair.Key);
            }
            foreach (Resident resident in gone) SendHome(resident);
        }

        private bool AtDoor(Visit visit) =>
            Time.time - visit.leavingSince > leaveTimeoutSeconds ||
            Vector3.ProjectOnPlane(visit.resident.transform.position - room.DoorPosition, Vector3.up).magnitude <= doorArriveRadius;

        // Out of the door when nobody outside would see it; after a wait when somebody keeps looking.
        private bool MayStepOut(Visit visit)
        {
            if (visit.waitingSince < 0f) visit.waitingSince = Time.time;
            return Time.time - visit.waitingSince >= watchedWaitSeconds || !Watched(ExitPoint(out _));
        }

        private bool TryInviteAtDoor()
        {
            int seatPlace = FreeSeatPlace();
            if (seatPlace < 0 || PlayerNear(room.DoorPosition)) return false;

            Resident guest = ChooseGuest();
            if (guest == null) return false;

            PutAt(guest, room.DoorPosition, room.DoorFacing);
            Admit(guest, seatPlace);
            return true;
        }

        // The room is lived in the moment a player walks in: guests are simply there, at their seats.
        private void SeatInitialGuests()
        {
            int wanted = Mathf.Min(Random.Range(guestsOnEntry.x, guestsOnEntry.y + 1), room.SeatCount);
            for (int i = 0; i < wanted; i++)
            {
                int seatPlace = FreeSeatPlace();
                Resident guest = seatPlace < 0 ? null : ChooseGuest();
                SettlementPlace stand = HouseRoom.PlaceAt(seatPlace);
                if (guest == null) return;

                PutAt(guest, stand.Position, guest.transform.rotation);
                Admit(guest, seatPlace);
            }
        }

        private void Admit(Resident guest, int seatPlace)
        {
            Visitors[guest] = new Visit
            {
                resident = guest,
                routine = guest.GetComponent<ResidentRoutine>(),
                seatPlace = seatPlace,
                leaveAt = Time.time + RandomBetween(staySeconds),
            };
        }

        // Residents fighting the player who went indoors: each steps through the door after the time it would have taken to run there.
        private void AdmitPursuers()
        {
            float now = Time.time;
            foreach (Resident resident in host.Society.Residents)
            {
                if (resident == null) continue;
                if (!IsPursuing(resident)) { following.Remove(resident); continue; }

                if (!following.TryGetValue(resident, out float arrivesAt))
                {
                    float metres = Vector3.ProjectOnPlane(resident.transform.position - ExitPoint(out _), Vector3.up).magnitude;
                    if (metres > pursuitRange) continue;
                    following[resident] = arrivesAt = now + metres / pursuitSpeed;
                }
                if (now < arrivesAt) continue;

                following.Remove(resident);
                PutAt(resident, room.DoorPosition, room.DoorFacing);
                // On its way out already: it fights where it stands, and leaves by the door only once it is calm again.
                Visitors[resident] = new Visit
                {
                    resident = resident,
                    routine = resident.GetComponent<ResidentRoutine>(),
                    seatPlace = -1,
                    leaving = true,
                    leavingSince = now,
                };
            }
        }

        private bool IsPursuing(Resident resident)
        {
            if (!resident.isActiveAndEnabled || resident.IsDead || resident.IsOffstage || Visitors.ContainsKey(resident) || IsCalm(resident))
                return false;

            // The grudge, not the live target: a chaser loses its target the moment the player leaves the world it can path in.
            Transform enemy = resident.Provocation.Aggressor != null ? resident.Provocation.Aggressor : resident.Provocation.Provoker;
            return enemy != null && enemy.root == insidePlayer.root;
        }

        // Up off whatever it sits on first: a sitter moved while seated is put back at its old stand point when it gets up.
        private static void PutAt(Resident resident, Vector3 position, Quaternion facing)
        {
            resident.GetComponent<ResidentRoutine>()?.StandUp();
            NetworkedTeleport.Move(resident.gameObject, position, facing);
        }

        private int FreeSeatPlace()
        {
            var free = new List<int>();
            for (int seat = 0; seat < room.SeatCount; seat++)
            {
                int place = HouseRoom.SeatPlace(seat);
                if (HouseRoom.PlaceAt(place).Usable && !IsTaken(place)) free.Add(place);
            }
            return free.Count == 0 ? -1 : free[Random.Range(0, free.Count)];
        }

        private static bool IsTaken(int place)
        {
            foreach (Visit visit in Visitors.Values)
                if (visit.seatPlace == place && !visit.leaving) return true;
            return false;
        }

        // Of the residents free to idle, unseen where they stand, the one the house would most like to see.
        private Resident ChooseGuest()
        {
            candidates.Clear();
            candidateResidents.Clear();
            foreach (Resident resident in host.Society.Residents)
            {
                if (!CanCallIn(resident)) continue;

                bool kin = false;
                foreach (int close in resident.CloseTo()) kin |= household.Contains(close);
                candidates.Add(new GuestCandidate(household.Contains(resident.index), kin));
                candidateResidents.Add(resident);
            }
            int chosen = GuestPick.Choose(candidates, draw);
            return chosen < 0 ? null : candidateResidents[chosen];
        }

        private bool CanCallIn(Resident resident)
        {
            if (resident == null || !resident.isActiveAndEnabled || resident.IsDead || resident.IsOffstage) return false;
            if (resident.Override != OverrideKind.None || Visitors.ContainsKey(resident) || !IsCalm(resident)) return false;
            if (resident.Focus != null && resident.Focus.IsFocused) return false;
            if (resident.Presence != null && resident.Presence.Prop != 0) return false;

            ResidentRoutine routine = resident.GetComponent<ResidentRoutine>();
            if (routine == null || !routine.Current.HasValue || !IsIdling(routine.Current.Value.activity)) return false;

            return !Watched(resident.transform.position);
        }

        // Free time, whichever shape it takes; never a job, a patrol, a chore, a trip or bed.
        private static bool IsIdling(Activity planned) => planned is Activity.Break or Activity.Hearth or Activity.Stroll or Activity.Amble;

        private static bool IsCalm(Resident resident) => resident.Provocation == null || resident.Provocation.Band == AggressionBand.Calm;

        private bool PlayerNear(Vector3 point)
        {
            SessionPlayers.Collect(Players);
            foreach (Transform player in Players)
                if (Vector3.ProjectOnPlane(player.position - point, Vector3.up).magnitude < arrivalClearance) return true;
            return false;
        }

        private bool Watched(Vector3 point) => ObserverCheck.AnyPlayerSees(point + Vector3.up * ObserverCheck.ChestHeight, observedWithin);

        // Back out of the dwelling's door, facing away from it; the day plan walks the resident on from there.
        private void SendHome(Resident guest)
        {
            Visitors.Remove(guest);
            if (guest == null) return;

            Vector3 stand = ExitPoint(out Quaternion facing);
            PutAt(guest, stand, facing);
        }

        private void SendAllHome()
        {
            if (Visitors.Count == 0) return;

            gone.Clear();
            gone.AddRange(Visitors.Keys);
            foreach (Resident guest in gone) SendHome(guest);
        }

        private Vector3 ExitPoint(out Quaternion facing)
        {
            Transform doorway = SettlementPlaces.DoorwayOf(dwelling);
            facing = Quaternion.LookRotation(SettlementPlaces.OutwardOf(doorway));
            SettlementPlaces.TryDoorStand(dwelling, host.WalkableHeart, ResidentTuning.Instance.doorStandDistances, out Vector3 stand);
            return stand;
        }

        private static float RandomBetween(Vector2 range) => Random.Range(range.x, range.y);
    }
}
