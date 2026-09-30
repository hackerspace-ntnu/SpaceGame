// One walking-city house's crew: seated on its VesselSeats posts while the column marches, put
// ashore at its gangway when the column stops, called back and seated again before it moves on.
// Server-side only (Network.Simulates); seating replicates through NpcSeating's parenting, so a
// client sees the result with no message of its own. The decisions are CrewShiftLogic's; this
// component counts, moves people and holds the leader.
//
// The leader waits for EVERY house: whichever house FormationModule.LeaderOf names (checked every
// tick, since a dead flagged leader hands the column to the first live member) sets its
// NpcTaskModule's departure gate to AllAboard(formationId), so a stay can end but the column does
// not leave until the last crew member of the last house is seated (or seated by the recall
// timeout). A house that stops leading takes its gate down.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    [RequireComponent(typeof(VesselSeats))]
    [DisallowMultipleComponent]
    public class CrewShift : MonoBehaviour
    {
        [Tooltip("Ground-level marker beside the hull where crew get off and on. Must be within the " +
                 "VesselSeats navMeshReach of walkable ground.")]
        [SerializeField] private Transform gangway;

        [Tooltip("Seconds between one crew member stepping off and the next.")]
        [SerializeField] private float disembarkInterval = 0.5f;

        [Tooltip("How far from the gangway crew may wander while ashore before they are walked back.")]
        [SerializeField] private float ashoreRadius = 40f;

        [Tooltip("How close to the gangway a recalled crew member must get to be seated.")]
        [SerializeField] private float boardRadius = 4f;

        [Tooltip("Seconds of recall (not counting time spent fighting) before anyone still ashore is seated directly.")]
        [SerializeField] private float recallTimeout = 60f;

        [Tooltip("How far around the gangway marker to look for NavMesh.")]
        [SerializeField] private float gangwaySampleDistance = 8f;

        // Ashore crew are walked back to somewhere inside half the tether, not to its very edge, so
        // one step outward does not send them straight back again.
        private const float TetherReturnFraction = 0.5f;

        // A boarding crew member counts as arrived well inside the board radius, so it is seated
        // on the next recall tick rather than stopping just outside it.
        private const float BoardApproachFraction = 0.5f;

        // Below this, every crew member steps off in the same frame and the gangway reads as a burst.
        private const float MinDisembarkInterval = 0.05f;

        // Less than an agent's own stopping distance, and nobody could ever get close enough to board.
        private const float MinBoardRadius = 0.5f;

        // The tether must be wider than the board radius, or crew ashore are pulled onto the gangway.
        private const float MinTetherMargin = 1f;

        // A shorter recall is no recall at all: crew would be seated before they could start walking.
        private const float MinRecallTimeout = 1f;

        private static readonly Dictionary<string, List<CrewShift>> Houses = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Houses.Clear();

        private readonly List<GameObject> crew = new();
        private VesselSeats seats;
        private FormationModule formation;
        private string registeredId;
        private NpcTaskModule gatedTasks;
        private float disembarkTimer;
        private float recallElapsed;

        public CrewState State { get; private set; } = CrewState.Aboard;
        public bool HasRoom => crew.Count < Seats.Capacity;
        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();

        public Vector3 GangwayPoint
        {
            get
            {
                Vector3 point = gangway != null ? gangway.position : transform.position;
                return NavMesh.SamplePosition(point, out NavMeshHit hit, gangwaySampleDistance, NavMesh.AllAreas)
                    ? hit.position : point;
            }
        }

        /// <summary>Add a crew member: seated on a free post, or left ashore by the gangway.</summary>
        public void Take(GameObject member, bool aboard)
        {
            if (member == null || !HasRoom) return;

            // The spawner configures this house's formation before any crew are taken, so the gate
            // can go up now. Waiting for Update would let a leader spawned mid-stop pick its next
            // stop on its first tick, before the gate exists, and walk off from crew still ashore.
            FollowFormation();

            if (aboard)
            {
                if (Seats.Seat(member) < 0)
                {
                    Debug.LogError($"[CrewShift] Could not seat '{member.name}' aboard '{name}': no free post, " +
                                   "or this machine is not the authority.", this);
                    return;
                }
            }
            else if (State == CrewState.Aboard)
            {
                // Mid-shift the house is already ashore or recalling; restarting that would reset its clock.
                State = CrewState.Ashore;
            }

            crew.Add(member);
        }

        public static CrewShift FirstWithRoom(IEnumerable<GameObject> members)
        {
            foreach (GameObject member in members)
                if (member != null && member.TryGetComponent(out CrewShift house) && house.HasRoom)
                    return house;
            return null;
        }

        public static bool AnyAshore(IEnumerable<GameObject> members)
        {
            foreach (GameObject member in members)
                if (member != null && member.TryGetComponent(out CrewShift house) && house.State != CrewState.Aboard)
                    return true;
            return false;
        }

        public static bool AllAboard(string formationId)
        {
            if (string.IsNullOrEmpty(formationId) || !Houses.TryGetValue(formationId, out List<CrewShift> houses))
                return true;
            foreach (CrewShift house in houses)
                if (house != null && house.State != CrewState.Aboard) return false;
            return true;
        }

        private void OnDisable()
        {
            RemoveGate();
            Unregister();
        }

        private void Update()
        {
            if (!Network.Simulates(this)) return;

            FollowFormation();

            crew.RemoveAll(member => member == null || !IsAlive(member));
            CrewCensus census = Count();
            NpcTaskModule leaderTasks = LeaderTasks();
            bool atStop = leaderTasks != null && leaderTasks.AtStop;

            CrewState next = CrewShiftLogic.Next(State, atStop, census);
            if (next != State) Enter(next);

            switch (State)
            {
                case CrewState.Disembarking: TickDisembark(); break;
                case CrewState.Ashore:       TetherAshore(); break;
                case CrewState.Recalling:    TickRecall(census); break;
            }
        }

        private void Enter(CrewState next)
        {
            State = next;
            disembarkTimer = 0f;
            recallElapsed = 0f;
            if (next == CrewState.Aboard) ClearGoals();
        }

        private void TickDisembark()
        {
            disembarkTimer -= Time.deltaTime;
            if (disembarkTimer > 0f) return;
            disembarkTimer = disembarkInterval;

            int seat = Seats.FirstOccupiedSeat();
            if (seat >= 0) Seats.Unseat(seat, GangwayPoint);
        }

        private void TetherAshore()
        {
            Vector3 home = GangwayPoint;
            foreach (GameObject member in crew)
            {
                if (IsSeated(member)) continue;
                AgentGoal goal = AgentGoal.GetOrAdd(member);
                float distance = Flat(member.transform.position - home);
                if (distance > ashoreRadius) goal.Set(home, ashoreRadius * TetherReturnFraction, "back to the walker");
                else if (goal.HasGoal && goal.HasArrived) goal.Clear();
            }
        }

        private void TickRecall(CrewCensus census)
        {
            recallElapsed = CrewShiftLogic.AdvanceRecallClock(recallElapsed, Time.deltaTime, census);
            bool force = CrewShiftLogic.ShouldForceBoard(recallElapsed, recallTimeout, census);
            Vector3 gangwayPoint = GangwayPoint;

            foreach (GameObject member in crew)
            {
                if (IsSeated(member) || IsFighting(member)) continue;

                if (force || Flat(member.transform.position - gangwayPoint) <= boardRadius)
                {
                    AgentGoal.GetOrAdd(member).Clear();
                    Seats.Seat(member);
                }
                else
                {
                    AgentGoal.GetOrAdd(member).Set(gangwayPoint, boardRadius * BoardApproachFraction, "boarding the walker");
                }
            }
        }

        private CrewCensus Count()
        {
            int aboard = 0, fighting = 0;
            foreach (GameObject member in crew)
            {
                if (IsSeated(member)) aboard++;
                if (IsFighting(member)) fighting++;
            }
            return new CrewCensus(crew.Count, aboard, fighting);
        }

        private bool IsSeated(GameObject member)
        {
            for (int i = 0; i < Seats.Capacity; i++)
                if (Seats.OccupantAt(i) == member) return true;
            return false;
        }

        private static bool IsFighting(GameObject member) =>
            member.TryGetComponent(out AgentTargeting targeting) && targeting.HasTarget;

        private static bool IsAlive(GameObject member) =>
            !member.TryGetComponent(out HealthComponent health) || health.Alive;

        private void ClearGoals()
        {
            foreach (GameObject member in crew)
                if (member != null && member.TryGetComponent(out AgentGoal goal)) goal.Clear();
        }

        private void FollowFormation()
        {
            if (formation == null) TryGetComponent(out formation);
            KeepRegistered();
            KeepGate();
        }

        private NpcTaskModule LeaderTasks()
        {
            if (formation == null || string.IsNullOrEmpty(formation.FormationId)) return null;
            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);
            return leader != null && leader.TryGetComponent(out NpcTaskModule tasks) ? tasks : null;
        }

        private void KeepRegistered()
        {
            string id = formation != null ? formation.FormationId : null;
            if (id == registeredId) return;

            RemoveGate();
            Unregister();
            if (string.IsNullOrEmpty(id)) return;

            if (!Houses.TryGetValue(id, out List<CrewShift> list)) Houses[id] = list = new List<CrewShift>();
            list.Add(this);
            registeredId = id;
        }

        /// <summary>
        /// Hold the column's departure gate while this house is the one the column follows, and only
        /// then. The gate is keyed to the registered formation id, which KeepRegistered already
        /// re-keys (taking the gate down first) whenever that id changes.
        /// </summary>
        private void KeepGate()
        {
            // KeepRegistered has just set registeredId to the formation's id (or null for none).
            if (registeredId == null || !formation.LeadsFormation)
            {
                RemoveGate();
                return;
            }

            if (gatedTasks != null || !TryGetComponent(out NpcTaskModule tasks)) return;
            string gateId = registeredId;
            tasks.SetDepartureGate(() => AllAboard(gateId));
            gatedTasks = tasks;
        }

        private void RemoveGate()
        {
            if (gatedTasks != null) gatedTasks.SetDepartureGate(null);
            gatedTasks = null;
        }

        private void Unregister()
        {
            if (registeredId != null && Houses.TryGetValue(registeredId, out List<CrewShift> list))
            {
                list.Remove(this);
                if (list.Count == 0) Houses.Remove(registeredId);
            }
            registeredId = null;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private void OnValidate()
        {
            disembarkInterval = Mathf.Max(MinDisembarkInterval, disembarkInterval);
            boardRadius = Mathf.Max(MinBoardRadius, boardRadius);
            ashoreRadius = Mathf.Max(boardRadius + MinTetherMargin, ashoreRadius);
            recallTimeout = Mathf.Max(MinRecallTimeout, recallTimeout);
        }
    }
}
