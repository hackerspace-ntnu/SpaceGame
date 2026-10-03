// What a resident notices about the players around it, twice a second, on the machine that simulates it.
//
// One observation wins across every player in notice range (by priority, then distance) and is raised as
// Observed with the resident's stance toward that player; ResidentVoice decides what, if anything, to say.
// The event fires when the noticed player or observation changes, and again every restateSeconds so the
// voice's own throttles — not this module — set how often a resident remarks.
//
// Three side effects, none of them movement:
//   glance      — IFacingModule at Ambient: a short look at whoever was just noticed, held while they are a
//                 threat. Never while walking, and never over a conversation (InteractionFocusModule wins).
//   irritation  — a player standing (or sprinting) in the resident's work place while it works, and not
//                 talking to it, escalates ProvocationModule one band per irritationStepSeconds. A guard
//                 (archetype.challengesArmed) takes an armed or sprinting player in notice range the same way:
//                 warned at Wary, told to stand down at Drawn, and only a fight once the meter is full.
//   avoidance   — a player this resident holds kin harm against, inside avoidRadius, sends it home to
//                 shelter. Shelter, never flight: FleeModule reads the shared target, which would chase you.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [RequireComponent(typeof(Resident))]
    public class ResidentAwareness : BehaviourModuleBase, IFacingModule
    {
        [Tooltip("Seconds between sweeps of the players in notice range.")]
        [SerializeField] private float sweepInterval = 0.5f;

        [Tooltip("Seconds before the same observation of the same player is raised again.")]
        [SerializeField] private float restateSeconds = 10f;

        [Tooltip("Seconds the head turns to a player after something new is noticed about them.")]
        [SerializeField] private float glanceSeconds = 3f;

        [Tooltip("Metres around the work place a player has to stand within to be in the way.")]
        [SerializeField] private float workAreaRadius = 2.5f;

        [Tooltip("How much faster a sprinting player's racket irritates than standing in the way.")]
        [SerializeField] private float sprintIrritationScale = 2f;

        [Tooltip("Seconds a resident stays sheltering after it last saw the player it avoids.")]
        [SerializeField] private float shelterSeconds = 60f;

        public event Action<Transform, Observation, Stance> Observed;

        public Transform Noticed { get; private set; }
        public Observation LastObservation { get; private set; }

        /// <summary>
        /// Looking at a player it just noticed (or at a threat for as long as it is one): the window
        /// <see cref="TryGetFacing"/> turns the body for. A worker in it has stopped what it was doing.
        /// </summary>
        public bool Attending => Noticed != null && (Time.time - raisedAt < glanceSeconds || IsThreat(LastObservation));

        public int FacingPriority => Priority;
        public override bool ClaimsMovement => false;

        public override string ModuleDescription =>
            "Notices players in ResidentTuning.noticeRadius and raises Observed for the voice.\n\n" +
            "• Glances at the noticed player (facing only)\n" +
            "• Lingering in the work place → ProvocationModule.Escalate(Trespass)\n" +
            "• Kin grudge within avoidRadius → Shelter override\n" +
            "• Side-effect module: never claims the frame.";

        private Resident resident;
        private readonly List<Transform> players = new List<Transform>();
        private float sinceSweep;
        private float raisedAt = float.NegativeInfinity;
        private Transform irritant;
        private float irritatedFor;
        private int expiredOnDay = int.MinValue;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

        private void Awake() => resident = GetComponent<Resident>();

        private void OnEnable()
        {
            sinceSweep = 0f;
            Noticed = null;
            LastObservation = Observation.None;
            irritant = null;
            irritatedFor = 0f;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (resident == null || resident.IsDead || resident.IsOffstage ||
                resident.Society == null || resident.Memory == null)
            {
                Noticed = null;
                LastObservation = Observation.None;
                return null;
            }

            sinceSweep += deltaTime;
            if (sinceSweep < sweepInterval) return null;
            Sweep(sinceSweep);
            sinceSweep = 0f;
            return null;
        }

        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            facePosition = default;
            if (context.IsMoving || !Attending) return false;

            facePosition = Noticed.position;
            return true;
        }

        /// <summary>This resident's stance toward a player right now (recomputed, never stored).</summary>
        public Stance StanceToward(Transform player)
        {
            if (resident == null || resident.Society == null || resident.Memory == null || player == null)
                return Stance.Unsure;
            PlayerRead read = PlayerRead.Of(player, transform);
            return StanceFor(in read, ResidentMemory.ProfileOf(player), resident.Society.Day);
        }

        private void Sweep(float elapsed)
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            SettlementSociety society = resident.Society;
            int day = society.Day;
            if (day != expiredOnDay)
            {
                resident.Memory.Expire(day);
                expiredOnDay = day;
            }

            Vector3? workPlace = WorkPlace(society);
            float workAreaSqr = workAreaRadius * workAreaRadius;

            Transform best = null, inWorkArea = null;
            Observation bestObservation = Observation.None;
            float bestDistance = float.MaxValue;
            PlayerRead bestRead = default;
            string bestProfile = null;
            bool racket = false, avoid = false;

            SessionPlayers.Collect(players);
            foreach (Transform player in players)
            {
                float distance = Vector3.Distance(player.position, transform.position);
                if (distance > tuning.noticeRadius) continue;

                PlayerRead read = PlayerRead.Of(player, transform);
                string profile = ResidentMemory.ProfileOf(player);
                bool kinHarmed = profile != null && resident.Memory.KinHarmedBy(profile, day);
                Observation observation = read.Observe(kinHarmed);

                if (best == null || Rank(observation) < Rank(bestObservation) ||
                    (observation == bestObservation && distance < bestDistance))
                {
                    best = player;
                    bestObservation = observation;
                    bestDistance = distance;
                    bestRead = read;
                    bestProfile = profile;
                }

                avoid |= kinHarmed && distance <= tuning.avoidRadius;
                if (inWorkArea == null && workPlace.HasValue && (player.position - workPlace.Value).sqrMagnitude <= workAreaSqr)
                {
                    inWorkArea = player;
                    racket = read.sprinting;
                }
            }

            Notice(best, bestObservation, in bestRead, bestProfile, day);
            if (inWorkArea == null && resident.archetype != null && resident.archetype.challengesArmed &&
                bestObservation is Observation.ArmedHeld or Observation.Sprinting)
            {
                inWorkArea = best;
                racket = bestObservation == Observation.Sprinting;
            }
            Irritate(inWorkArea, racket, elapsed, tuning.irritationStepSeconds);
            if (avoid) resident.Shelter(shelterSeconds);
        }

        private void Notice(Transform player, Observation observation, in PlayerRead read, string profile, int day)
        {
            bool fresh = player != Noticed || observation != LastObservation || Time.time - raisedAt >= restateSeconds;
            Noticed = player;
            LastObservation = player != null ? observation : Observation.None;
            if (player == null || !fresh) return;

            raisedAt = Time.time;
            Observed?.Invoke(player, observation, StanceFor(in read, profile, day));
        }

        private void Irritate(Transform player, bool sprinting, float elapsed, float stepSeconds)
        {
            bool talking = resident.Focus != null && resident.Focus.IsFocused;
            if (player != irritant || talking)
            {
                irritant = player;
                irritatedFor = 0f;
            }
            if (player == null || talking || resident.Provocation == null) return;

            irritatedFor += elapsed * (sprinting ? sprintIrritationScale : 1f);
            if (irritatedFor < stepSeconds) return;
            irritatedFor = 0f;
            resident.Provocation.Escalate(player, AggressionInput.Trespass);
        }

        // Where the resident is working right now, or null when the plan has it doing anything else.
        private Vector3? WorkPlace(SettlementSociety society)
        {
            DayPlan plan = society.PlanFor(resident);
            if (plan == null || !plan.At(society.NowMinutes, out PlanSegment segment) || segment.activity != Activity.Work)
                return null;
            SettlementPlace place = society.Place(segment.place);
            return place != null ? place.Position : (Vector3?)null;
        }

        private Stance StanceFor(in PlayerRead read, string profile, int day)
        {
            ProvocationModule provocation = resident.Provocation;
            bool causedByThisPlayer = provocation != null && read.player != null &&
                                      (provocation.LastCauseFrom == read.player || provocation.Aggressor == read.player);
            AggressionBand band = causedByThisPlayer ? provocation.Band : AggressionBand.Calm;
            bool grudge = profile != null && resident.Memory.HoldsPersonalGrudge(profile, day);
            ResidentTuning tuning = ResidentTuning.Instance;
            return Attitude.StanceFor(resident.Nerve, resident.Temper, resident.Memory.Regard(profile), grudge,
                                      in read, band, tuning.warmAt, tuning.coldAt);
        }

        // Lower is more urgent; None never wins.
        private static int Rank(Observation o) => o == Observation.None ? int.MaxValue : (int)o;

        private static bool IsThreat(Observation o) =>
            o == Observation.KinHarmed || o == Observation.Hitting || o == Observation.Menacing || o == Observation.ArmedHeld;

        protected override void OnValidate()
        {
            sweepInterval = Mathf.Max(0.05f, sweepInterval);
            restateSeconds = Mathf.Max(sweepInterval, restateSeconds);
            glanceSeconds = Mathf.Max(0f, glanceSeconds);
            workAreaRadius = Mathf.Max(0f, workAreaRadius);
            sprintIrritationScale = Mathf.Max(1f, sprintIrritationScale);
            shelterSeconds = Mathf.Max(0f, shelterSeconds);
        }
    }
}
