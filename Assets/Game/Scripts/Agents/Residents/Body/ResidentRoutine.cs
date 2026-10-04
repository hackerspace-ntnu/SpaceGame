// A resident's day, turned into where it should be standing right now — and the only writer of its goal.
//
// It never moves the body. It reads the plan (or the one live override), writes AgentGoal with a hold,
// and GoalTravelModule does the walking and the standing, so a fight or a flee preempts the day by
// the ordinary priority ladder with neither side knowing about the other. The things a player must
// never watch — stepping indoors, skipping across the settlement after a load — happen only unseen.
//
// A segment that is an errand (a chore, an amble, a patrol) is still ONE plan segment: once its walk to the
// anchor is over, ErrandRunner names the stop to be at, and that stop is written as the same holding goal.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [RequireComponent(typeof(Resident))]
    public sealed class ResidentRoutine : BehaviourModuleBase
    {
        private const int TraceLength = 10;
        private const float MinutesPerDay = 1440f;
        private const float MinutesPerHour = 60f;

        [Header("Routine")]
        [SerializeField, Min(0.05f)] private float evaluateEvery = 0.5f;
        [Tooltip("How close counts as there at a spot — a seat, a counter, an anvil — where the body holds a pose.")]
        [SerializeField, Min(0.3f)] private float spotArriveRadius = 0.6f;
        [Tooltip("How close counts as there at a door or a camp.")]
        [SerializeField, Min(0.5f)] private float placeArriveRadius = 1.2f;
        [Tooltip("How close counts as there at a trip point, where the exact spot does not matter.")]
        [SerializeField, Min(0.5f)] private float looseArriveRadius = 3f;
        [Tooltip("A player this close with line of sight counts as watching.")]
        [SerializeField, Min(1f)] private float observedWithin = 60f;
        [Tooltip("Seconds a sleeper waits at its door for watchers to look away before going in anyway.")]
        [SerializeField, Min(0f)] private float offstageAfterSeconds = 10f;
        [Tooltip("On enable or load, a resident farther than this from its plan's place is moved there while unwatched.")]
        [SerializeField, Min(1f)] private float settleDistance = 30f;
        [Tooltip("On enable or load, a sleeper this close to its door goes straight indoors.")]
        [SerializeField, Min(0f)] private float doorstepDistance = 2f;
        [Tooltip("A resident farther than this from an elevated post it should be at is moved there while unwatched.")]
        [SerializeField, Min(1f)] private float deckReach = 3f;
        [Tooltip("How far short of a noise an approaching resident stops.")]
        [SerializeField, Min(0f)] private float approachStandOff = 8f;
        [Tooltip("How close counts as in the doorway: the point a resident steps through to go indoors.")]
        [SerializeField, Min(0.3f)] private float thresholdArriveRadius = 0.5f;
        [Tooltip("A resident already this close to its doorway goes in without the extra step.")]
        [SerializeField, Min(0f)] private float thresholdStepMin = 0.7f;

        private readonly List<string> trace = new();
        private Resident resident;
        private float sinceEvaluation;
        private bool needsSettle = true;
        private bool hasTarget;
        private bool targetIsPlan;
        private Vector3 target;
        private bool targetHolds = true;
        private float targetSpeed = 1f;
        private float arrivedAtDoorAt = -1f;
        private bool steppingIn;
        private NavMeshAgentMotor motor;
        private ResidentAwareness awareness;
        private float attendingUntil = float.NegativeInfinity;
        private readonly ErrandRunner errands = new();
        private ResidentSeating seating;
        private ResidentPushing pushing;

        public PlanSegment? Current { get; private set; }
        public string LastReason { get; private set; } = string.Empty;
        /// <summary>The last ten decisions, oldest first, each stamped with the game clock.</summary>
        public IReadOnlyList<string> Trace => trace;
        /// <summary>Holding at the current segment's place rather than walking or overridden.</summary>
        /// <remarks>A sitter counts as there wherever its seat put its body: the seat is not on the spot's stand point.</remarks>
        public bool AtPlace => Current.HasValue && targetIsPlan && resident != null && resident.Goal != null &&
                               (seating.IsSeated || resident.Goal.HasArrived);

        /// <summary>The place index being held right now (an errand's stop, else the plan's place); <see cref="ResidentPresence.NoPlace"/> when walking.</summary>
        public int HeldPlace => !AtPlace ? ResidentPresence.NoPlace : PlannedPlace;

        // Where the plan has this resident, errand stops included. Only meaningful while a plan segment is current.
        private int PlannedPlace => errands.Active ? errands.Stop.place : Current.Value.place;

        /// <summary>On an amble or a patrol: walking is the activity, so a conversation need not stop it.</summary>
        public bool OnTheMove => errands.Active && errands.Stop.shown is Activity.Patrol or Activity.Amble;

        public override bool ClaimsMovement => false;

        public override string ModuleDescription =>
            "Server only. Every half second turns the resident's day plan (or its one live override) into an " +
            "AgentGoal with a hold; GoalTravelModule walks there and stands facing the work.\n\n" +
            "• Sleep: goes offstage at its door once unwatched (or after a wait); lies down at a camp or in its bed\n" +
            "• Enable/load: an unwatched resident far from its plan is moved there\n" +
            "• Publishes ResidentPresence and keeps a ten-line decision trace";

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

        private void Awake()
        {
            resident = GetComponent<Resident>();
            motor = GetComponent<NavMeshAgentMotor>();
            awareness = GetComponentInChildren<ResidentAwareness>(true);
            seating = new ResidentSeating(gameObject);
            pushing = new ResidentPushing(gameObject);
        }

        private void OnEnable()
        {
            needsSettle = true;
            SettlementSociety.PlansRebuilt += OnPlansRebuilt;
        }

        // A prop in this resident's hands lands where it was going rather than vanishing with the body.
        private void OnDisable()
        {
            SettlementSociety.PlansRebuilt -= OnPlansRebuilt;
            errands.Reset();
            seating.Abandon();
            pushing.Release();
        }

        /// <summary>Gets the resident up off its seat where it is, ahead of a move that must not leave it sitting.</summary>
        public void StandUp() => seating.Release();

        /// <summary>
        /// Puts down everything the body holds in the settlement — a carried prop lands where it was going, the seat and
        /// the cart are freed — ahead of an absence the routine will not tick through (<see cref="Resident.GoAway"/>).
        /// </summary>
        public void LetGo()
        {
            errands.Reset();
            seating.Release();
            pushing.Release();
        }

        // A load or a time jump rebuilds the plans, and that is when a body can be far from where its day says.
        private void OnPlansRebuilt(SettlementSociety rebuilt)
        {
            if (resident != null && rebuilt == resident.Society) needsSettle = true;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            sinceEvaluation += deltaTime;
            if (sinceEvaluation < evaluateEvery || !Network.Decides) return null;
            sinceEvaluation = 0f;

            SettlementSociety society = resident != null ? resident.Society : null;
            if (resident != null && resident.IsDead)
            {
                errands.Reset();
                seating.Abandon();
                pushing.Release();
            }
            // Offstage covers away: a resident out with a band is offstage until it comes home.
            if (society == null || context.Goal == null || resident.IsDead || resident.IsOffstage)
            {
                seating.Release();
                pushing.Release();
                return null;
            }

            Evaluate(society, context.Goal);
            return null;
        }

        private void Evaluate(SettlementSociety society, AgentGoal goal)
        {
            double now = society.NowMinutes;
            DayPlan plan = society.PlanFor(resident);
            // A resident calling at a house is on the visit's segment (a seat, then the doorway) whatever its day says.
            Current = HouseVisits.TryGetSegment(resident, out PlanSegment visiting) ? visiting
                : plan != null && plan.At(now, out PlanSegment segment) ? segment : (PlanSegment?)null;

            bool overridden = TryOverride(society, out Vector3 wanted, out string reason);
            float radius = placeArriveRadius, speed = 1f;
            bool hold = true;
            Vector3? face = null;
            SettlementPlace place = null;
            errands.Reset(overridden || !Current.HasValue || !ErrandRunner.IsErrand(Current.Value.activity) || now < Current.Value.arrive);

            if (!overridden && Current.HasValue)
            {
                place = society.Place(Current.Value.place);
                if (place == null) { Note(now, $"plan names missing place {Current.Value.place}"); return; }

                wanted = place.Position;
                face = place.FacePoint;
                radius = place.Kind switch
                {
                    PlaceKind.Trip => looseArriveRadius,
                    PlaceKind.Door or PlaceKind.Camp => placeArriveRadius,
                    _ => spotArriveRadius,
                };
                reason = $"{Current.Value.activity} at {place.Kind} {Current.Value.place}";

                if (ErrandRunner.IsErrand(Current.Value.activity) && now >= Current.Value.arrive)
                {
                    bool arrived = hasTarget && targetIsPlan && goal.HasGoal && goal.HasArrived;
                    ErrandStop stop = errands.Step(resident, society, Current.Value, arrived);
                    if (errands.HasStop)
                    {
                        (wanted, face, radius, hold, speed) = (stop.position, stop.face, stop.radius, stop.hold, stop.speed);
                        reason = $"{Current.Value.activity}: {(stop.place != ResidentPresence.NoPlace ? "spot " + stop.place : "on the move")}";
                    }
                }
            }
            else if (!overridden)
            {
                Note(now, "no plan segment now");
                Publish(false);
                return;
            }

            // A resident going to bed walks the last metres into the doorway, where it goes indoors, instead of vanishing on the step.
            if (steppingIn && (overridden || place == null || place.Threshold == null || Current.Value.activity != Activity.Sleep)) steppingIn = false;
            if (steppingIn)
            {
                (wanted, radius, hold, face) = (place.Threshold.Value, thresholdArriveRadius, false, null);
                reason = $"stepping in at the door of {Current.Value.place}";
            }

            // A spot is stood at where it was MEASURED on the NavMesh: one not measured yet waits for the world to load, and an
            // unusable one (the plan was built before the measurement said so) is not walked to.
            if (!overridden && place.Use != null && (!place.Resolved || !place.Usable))
            {
                Note(now, $"no stand point for {reason}");
                return;
            }

            // Step 3: one write per change of mind; a goal restored by the save is overwritten here too.
            bool switchedBetweenPlanAndOverride = targetIsPlan == overridden;
            if (!hasTarget || !goal.HasGoal || switchedBetweenPlanAndOverride || FlatDistance(wanted, target) > radius ||
                hold != targetHolds || !Mathf.Approximately(speed, targetSpeed))
            {
                bool exact = !overridden && (errands.Active ? errands.Stop.place != ResidentPresence.NoPlace : place.Use != null);
                if (!WriteGoal(goal, overridden, exact, wanted, radius, reason, hold, face, speed)) { Note(now, $"no NavMesh for {reason}"); return; }

                hasTarget = true;
                targetIsPlan = !overridden;
                target = wanted;
                targetHolds = hold;
                targetSpeed = speed;
                arrivedAtDoorAt = -1f;
                Note(now, reason);
            }

            if (needsSettle) { needsSettle = false; if (Settle(now, goal)) return; }
            if (!overridden && place != null && Hop(society, place, goal, now)) return;

            // A camp has no door to go in by and a bed is slept in where it stands: their sleepers lie down in sight.
            bool sleepingIndoors = !overridden && Current.Value.activity == Activity.Sleep && place.Kind == PlaceKind.Door;
            if (sleepingIndoors && (TryGoIndoors(now, goal) || Backstop(now, goal))) return;

            Publish(overridden && resident.Override == OverrideKind.Shelter);
        }

        // A place's point is already a measured stand point: it is used as it is, and a spot's must be ENDED on. Only a point
        // nobody measured (a noise to approach) is snapped to the nearest NavMesh.
        private static bool WriteGoal(AgentGoal goal, bool overridden, bool exact, Vector3 wanted, float radius, string reason,
                                      bool hold, Vector3? face, float speed) =>
            overridden ? goal.TrySetSampled(wanted, radius, reason, hold, face, speed)
                       : goal.Set(wanted, radius, reason, hold, face, speed, exact);

        private bool TryOverride(SettlementSociety society, out Vector3 point, out string reason)
        {
            point = transform.position;
            reason = null;
            // A guest in a house has no door of its own to shelter at, nor a street to approach a noise in.
            if (resident.Override == OverrideKind.None || HouseVisits.IsVisiting(resident)) return false;

            switch (resident.Override)
            {
                case OverrideKind.Shelter:
                    SettlementPlace door = society.Place(society.HomeOf(resident));
                    if (door == null) return false;
                    point = door.Position;
                    reason = "sheltering at home";
                    return true;
                case OverrideKind.Approach:
                    Vector3 away = Vector3.ProjectOnPlane(transform.position - resident.OverridePoint, Vector3.up);
                    if (away.magnitude > approachStandOff) point = resident.OverridePoint + away.normalized * approachStandOff;
                    reason = "approaching a noise";
                    return true;
                default:
                    point = resident.OverridePoint;
                    reason = "scripted";
                    return true;
            }
        }

        // Step 5. True when the resident went indoors, which ends this evaluation. Never for one away with a band: a
        // teleport home would put a body back that its band still has on the road.
        private bool Settle(double now, AgentGoal goal)
        {
            if (resident.IsAway) return false;
            if (FlatDistance(transform.position, goal.Position) > settleDistance && Unwatched(goal.Position))
                Teleport(now, goal, "moved to its plan unseen");

            bool asleepAtDoor = targetIsPlan && Current.Value.activity == Activity.Sleep &&
                                resident.Society.Place(Current.Value.place)?.Kind == PlaceKind.Door &&
                                FlatDistance(transform.position, goal.Position) <= doorstepDistance;
            if (asleepAtDoor) GoIndoors(now, "indoors at once on load");
            return asleepAtDoor;
        }

        // A post up a ladder is walked to over its link. Only when no path leads there (a deck nothing climbs to) is its
        // worker put there, and taken down, while nobody sees it — never mid-climb.
        private bool Hop(SettlementSociety society, SettlementPlace place, AgentGoal goal, double now)
        {
            bool up = place.Elevated && Vector3.Distance(transform.position, place.Position) > deckReach;
            bool down = !place.Elevated && society.OnDeck(transform.position);
            if (!(up || down) || (motor != null && motor.IsRidingLink)) return false;
            if (NavMeshReach.CanWalk(transform.position, goal.Position) || !Unwatched(goal.Position)) return false;

            Teleport(now, goal, up ? "up to its post unseen" : "down from its post unseen");
            return true;
        }

        // Step 4: in at the door when nobody is looking, or after a wait when somebody keeps looking.
        private bool TryGoIndoors(double now, AgentGoal goal)
        {
            if (!goal.HasArrived) { arrivedAtDoorAt = -1f; return false; }
            if (steppingIn)
            {
                GoIndoors(now, "stepped in at the door");
                return true;
            }
            if (arrivedAtDoorAt < 0f) arrivedAtDoorAt = Time.time;

            bool watched = ObserverCheck.AnyPlayerSees(transform.position + Vector3.up * ObserverCheck.ChestHeight, observedWithin);
            if (watched && Time.time - arrivedAtDoorAt < offstageAfterSeconds) return false;

            SettlementPlace door = resident.Society.Place(Current.Value.place);
            bool mustStepIn = door.Threshold.HasValue && FlatDistance(transform.position, door.Threshold.Value) > thresholdStepMin;
            if (mustStepIn && !steppingIn)
            {
                steppingIn = true;
                return true;
            }

            GoIndoors(now, watched ? "went indoors while watched" : "went indoors unseen");
            return true;
        }

        // Step 6: a sleeper still out long past bedtime is put on its doorstep, unseen; the next pass takes it in. Never one
        // away with a band (as Settle).
        private bool Backstop(double now, AgentGoal goal)
        {
            if (resident.IsAway || now < Current.Value.arrive + ResidentTuning.Instance.backstopDelay || !Unwatched(goal.Position)) return false;

            Teleport(now, goal, "backstop: put on its doorstep");
            return true;
        }

        private void GoIndoors(double now, string why)
        {
            arrivedAtDoorAt = -1f;
            steppingIn = false;
            resident.GoOffstage();
            Note(now, why);
        }

        private void Teleport(double now, AgentGoal goal, string why)
        {
            Vector3 heading = goal.FacePoint.HasValue ? Vector3.ProjectOnPlane(goal.FacePoint.Value - goal.Position, Vector3.up) : Vector3.zero;
            Quaternion facing = heading.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(heading) : transform.rotation;
            NetworkedTeleport.Move(gameObject, goal.Position, facing);
            Note(now, why);
        }

        // Step 7: what every machine shows, derived from where the body is in its day. The place travels with
        // it, so every machine holds the loop that place's spot asks for.
        private void Publish(bool sheltering)
        {
            if (resident.Presence == null) return;

            bool talking = resident.Focus != null && resident.Focus.IsFocused;
            bool climbing = motor != null && motor.IsClimbingLadder;

            // Before anything reads AtPlace: the body is put on, or taken off, the seat the plan's place calls for.
            seating.Sync(resident.Society, Current.HasValue && targetIsPlan ? PlannedPlace : ResidentPresence.NoPlace,
                         resident.Goal != null && resident.Goal.HasArrived);
            Activity shown = talking ? Activity.Talking
                : climbing ? Activity.Climbing
                : sheltering ? Activity.Sheltering
                : resident.IsWithBand ? Activity.Expedition
                : errands.Active ? errands.Stop.shown
                : AtPlace ? Holding(Current.Value.activity)
                : Activity.Walking;
            byte carried = errands.Active ? errands.Stop.prop
                : targetIsPlan && Current.HasValue && Current.Value.activity == Activity.Trip ? TripProp() : (byte)0;
            // A sitter keeps its seat and its loop through a conversation; anyone else puts the pose down to talk.
            int heldPlace = !climbing && (!talking || seating.IsSeated) ? HeldPlace : ResidentPresence.NoPlace;

            // A worker that turns to look at a player puts its work down (see ResidentAttention).
            bool attending = ResidentAttention.HandsOff(shown, awareness != null && awareness.Attending, Time.time,
                                                        ResidentTuning.Instance.attendResumeSeconds, ref attendingUntil);
            // Hands on a cart while working at a post that has one by it; anything else lets go and leaves the cart standing.
            pushing.Sync(resident.archetype != null && resident.archetype.pushesCart && shown == Activity.Work && heldPlace != ResidentPresence.NoPlace);
            resident.Presence.Publish(shown, carried, false, heldPlace, attending, seating.SeatId, pushing.CartId);
        }

        private static Activity Holding(Activity planned) => planned switch
        {
            Activity.Work => Activity.Work,
            Activity.Break or Activity.Hearth => Activity.Sitting,
            Activity.Stroll => Activity.Stroll,
            Activity.Trip => Activity.Stalking,
            Activity.Sleep => Activity.Sleep,
            _ => Activity.Walking,
        };

        // The first trip row this archetype goes on, 1-based as ResidentPresence reads it; 0 carries nothing.
        private byte TripProp()
        {
            var rows = ResidentTuning.Instance.tripKinds;
            TripKind trips = resident.archetype != null ? resident.archetype.trips : TripKind.None;
            for (int i = 0; rows != null && i < rows.Length; i++)
                if ((rows[i].kind & trips) != TripKind.None) return (byte)(i + 1);
            return 0;
        }

        private bool Unwatched(Vector3 destination) =>
            !ObserverCheck.AnyPlayerSees(transform.position + Vector3.up * ObserverCheck.ChestHeight, observedWithin) &&
            !ObserverCheck.AnyPlayerSees(destination + Vector3.up * ObserverCheck.ChestHeight, observedWithin);

        private void Note(double now, string why)
        {
            if (why == LastReason) return;
            LastReason = why;

            double minuteOfDay = now % MinutesPerDay;
            trace.Add($"{(int)(minuteOfDay / MinutesPerHour):00}:{(int)(minuteOfDay % MinutesPerHour):00} {why}");
            if (trace.Count > TraceLength) trace.RemoveAt(0);
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    }
}
