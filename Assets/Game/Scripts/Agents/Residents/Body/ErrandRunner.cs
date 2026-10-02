// What a resident does INSIDE one plan segment that is more than "stand at a place": the rounds of a chore,
// an amble from stop to stop, a patrol along the perimeter ring, or walking beside a companion. The plan
// says when and roughly where; this says which stop is next, how long to stay there, what is carried and how
// fast to walk — one stop at a time, so the routine can write it as an ordinary holding goal and everything
// else (walking, standing, fights preempting) stays exactly as it was.
//
// A chore that carries props moves real things: the round picks up a prop resting at a source spot's rest and
// sets it down at a free rest of a target spot (SettlementProps), and the hand shows the prop's item meanwhile.
// With no prop to fetch or nowhere to put one, the round is the ordinary one with the chore's own item.
//
// Server only and deliberately stateless across loads: a round restarts from its first stop when the world
// does, which no player can tell from the end of a round. A prop in hand when the round is cut short lands at
// the rest it was promised to. Nothing here is replicated — presence publishes the stop's held place and the
// prop byte, the props' sync publishes where each prop stands, and every machine derives the picture.
using System.Collections.Generic;
using SpaceGame.World;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents.Residents
{
    /// <summary>One thing to do at one point: where, how to stand, how long, what is carried, how fast to get there.</summary>
    public struct ErrandStop
    {
        public Vector3 position;
        public Vector3? face;
        /// <summary>The spot's place index when this is a spot (every machine holds its cue), else <see cref="ResidentPresence.NoPlace"/>.</summary>
        public int place;
        public Activity shown;
        public float radius, speed, dwellSeconds;
        public bool hold;
        /// <summary>Advance on coming within this distance instead of waiting to arrive (a patrol keeps moving); 0 = wait.</summary>
        public float advanceWithin;
        public byte prop;
        /// <summary>Picked up when the stop's dwell ends.</summary>
        public SettlementProp pick;
        /// <summary>Set down at <see cref="dropRest"/> when the stop's dwell ends.</summary>
        public SettlementProp drop;
        public int dropRest;
    }

    public sealed class ErrandRunner
    {
        private const float SpotRadius = 0.8f, LooseRadius = 2f, FollowRadius = 0.8f, FollowCatchUp = 3f, FollowCatchUpSpeed = 1.5f;
        private const float OccupiedWithin = 1.2f, ForgetFollowerAfter = 20f, ForgetFollowerFor = 40f;
        private const float PointDwellMax = 2f, SpotAmbleShare = 0.55f, BehindLeader = 0.6f, AheadOfLeader = 3f;
        private const int PointAttempts = 6;
        private const float PointSnap = 6f, MinPointShare = 0.15f;
        private const float WaitResume = 0.6f;

        private readonly Queue<ErrandStop> queue = new();
        private readonly List<int> strolls = new();
        private readonly List<int> targets = new();
        private System.Random rng;
        private ErrandStop stop;
        private Activity mode;
        private float segmentArrive = float.NaN;
        private float arrivedAt = -1f;
        private int[] chain;
        private int cursor, lastSpot = -1, ringAt, direction = 1;
        private bool waiting;
        private float waitingSince, ignoreFollowerUntil;

        private bool begun;
        private SettlementProps props;
        private SettlementProp promised, carrying;
        private int promisedRest = -1;

        /// <summary>This pass's segment is an errand and a stop is chosen.</summary>
        public bool Active => begun && HasStop;
        public ErrandStop Stop => stop;

        public static bool IsErrand(Activity activity) => activity is Activity.Chore or Activity.Patrol or Activity.Amble;

        /// <summary>Forgets the round when the segment is not an errand (or the resident is overridden); <paramref name="when"/> false keeps it.</summary>
        public void Reset(bool when = true)
        {
            if (!when) return;

            begun = false;
            LandCarried();
            queue.Clear();
            segmentArrive = float.NaN;
            arrivedAt = -1f;
            waiting = false;
        }

        /// <summary>
        /// The stop to be at now. <paramref name="arrived"/>: the body is standing at the stop returned last time.
        /// Call once per routine pass while the plan's segment is an errand and its walk to the anchor is over.
        /// </summary>
        public ErrandStop Step(Resident resident, SettlementSociety society, in PlanSegment segment, bool arrived)
        {
            if (!begun || mode != segment.activity || segmentArrive != segment.arrive) Begin(resident, society, segment);
            begun = true;

            Resident leader = society.LeaderOf(resident);
            if (leader != null && (mode == Activity.Patrol || mode == Activity.Amble) && Walks(leader, mode))
                return stop = Follow(resident, leader);

            if (HoldsForFollower(resident, society)) return stop = Wait(resident);

            if (!HasStop) Advance(resident, society);
            if (!HasStop) return stop;

            bool near = stop.advanceWithin > 0f && Flat(resident.transform.position, stop.position) <= stop.advanceWithin;
            if (arrived || near)
            {
                if (arrivedAt < 0f) arrivedAt = Time.time;
                if (Time.time - arrivedAt >= stop.dwellSeconds)
                {
                    FinishProps();
                    Advance(resident, society);
                }
            }
            else arrivedAt = -1f;
            return stop;
        }

        /// <summary>False when there is nothing to do (no places for the chore, no ring): the routine falls back to the plan's anchor.</summary>
        public bool HasStop => stop.radius > 0f;

        private void Begin(Resident resident, SettlementSociety society, in PlanSegment segment)
        {
            mode = segment.activity;
            segmentArrive = segment.arrive;
            LandCarried();
            props = society.Settlement != null ? society.Settlement.Props : null;
            queue.Clear();
            stop = default;
            arrivedAt = -1f;
            waiting = false;
            rng = new System.Random(unchecked(resident.seed * 31 + (int)segment.arrive));

            switch (mode)
            {
                case Activity.Chore: BeginChore(resident, society); break;
                case Activity.Patrol: BeginPatrol(resident, society, segment); break;
                case Activity.Amble: BeginAmble(society, segment); break;
            }
        }

        private void Advance(Resident resident, SettlementSociety society)
        {
            arrivedAt = -1f;
            if (queue.Count == 0) Refill(resident, society);
            stop = queue.Count > 0 ? queue.Dequeue() : default;
        }

        private void Refill(Resident resident, SettlementSociety society)
        {
            switch (mode)
            {
                case Activity.Chore: PlanRound(resident, society); break;
                case Activity.Patrol: queue.Enqueue(NextRingStop(society)); break;
                case Activity.Amble: queue.Enqueue(NextAmble(resident, society)); break;
            }
        }

        // ── chore ────────────────────────────────────────────────────────────────────────────────

        private void BeginChore(Resident resident, SettlementSociety society)
        {
            ChoreDefinition chore = resident.archetype != null ? resident.archetype.chore : null;
            targets.Clear();
            chain = System.Array.Empty<int>();
            cursor = 0;
            if (chore == null || !chore.IsComplete) return;

            targets.AddRange(society.ErrandPlaces(chore.target));
            List<int> sources = society.ErrandPlaces(chore.source);
            if (sources.Count == 0 || targets.Count == 0) return;

            Vector3 from = society.Place(Nearest(society, sources, resident.transform.position)).Position;
            var positions = new List<Vector3>(targets.Count);
            foreach (int index in targets) positions.Add(society.Place(index).Position);
            chain = ChoreRounds.Chain(from, positions);
        }

        // A round: the nearest source, then a few targets in walking order. With one use at both ends a round
        // never delivers to the stop it just left.
        private void PlanRound(Resident resident, SettlementSociety society)
        {
            ChoreDefinition chore = resident.archetype != null ? resident.archetype.chore : null;
            if (chore == null || chain.Length == 0) return;
            if (chore.carriesProps && PlanPropRound(resident, society, chore)) return;

            List<int> sources = society.ErrandPlaces(chore.source);
            if (sources.Count == 0) return;

            byte carried = ResidentTuning.Instance.PropIndexOf(chore.carried);
            int source = Nearest(society, sources, resident.transform.position);
            queue.Enqueue(Spot(society, source, Activity.Chore, Range(chore.sourceSeconds), chore.carryThroughout ? carried : (byte)0));

            int count = Mathf.Clamp(rng.Next(chore.targetsPerRound.x, chore.targetsPerRound.y + 1), 1, chain.Length);
            int taken = 0;
            for (int guard = 0; guard < chain.Length && taken < count; guard++)
            {
                int target = targets[chain[cursor % chain.Length]];
                cursor = (cursor + 1) % chain.Length;
                if (target == source) continue;

                queue.Enqueue(Spot(society, target, Activity.Chore, Range(chore.targetSeconds), carried));
                taken++;
            }
        }

        // A round that moves one real prop: the nearest source with a prop resting at one of its rests, then the
        // nearest other target with a free rest. Both are promised so no second resident plans the same ones.
        private bool PlanPropRound(Resident resident, SettlementSociety society, ChoreDefinition chore)
        {
            if (props == null || props.RestCount == 0) return false;

            ResidentTuning tuning = ResidentTuning.Instance;
            int source = -1;
            SettlementProp fetched = null;
            foreach (int place in ByDistance(society, society.ErrandPlaces(chore.source), resident.transform.position))
            {
                foreach (int rest in props.RestsOf(society.SpotAt(place)))
                {
                    SettlementProp candidate = props.FreePropAt(rest);
                    if (candidate == null || tuning.PropIndexOf(candidate.Item) == 0) continue;
                    (source, fetched) = (place, candidate);
                    break;
                }
                if (fetched != null) break;
            }
            if (fetched == null) return false;

            int target = -1, restTo = -1;
            foreach (int place in ByDistance(society, society.ErrandPlaces(chore.target), society.Place(source).Position))
            {
                if (place == source) continue;
                foreach (int rest in props.RestsOf(society.SpotAt(place)))
                {
                    if (!props.IsFree(rest)) continue;
                    (target, restTo) = (place, rest);
                    break;
                }
                if (target >= 0) break;
            }
            if (target < 0) return false;

            props.Reserve(fetched, restTo);
            (promised, promisedRest) = (fetched, restTo);

            ErrandStop pickUp = Spot(society, source, Activity.Chore, Range(chore.sourceSeconds), 0);
            pickUp.pick = fetched;
            ErrandStop setDown = Spot(society, target, Activity.Chore, Range(chore.targetSeconds), tuning.PropIndexOf(fetched.Item));
            setDown.drop = fetched;
            setDown.dropRest = restTo;
            queue.Enqueue(pickUp);
            queue.Enqueue(setDown);
            return true;
        }

        // The dwell at a stop is the reach: the prop changes hands as the resident turns to go.
        private void FinishProps()
        {
            if (props == null) return;

            if (stop.pick != null)
            {
                if (props.Pick(stop.pick))
                {
                    carrying = stop.pick;
                    props.Release(stop.pick, -1);
                }
                else
                {
                    // Somebody took it first: the round is over, nothing to set down.
                    queue.Clear();
                    props.Release(promised, promisedRest);
                    (promised, promisedRest) = (null, -1);
                }
            }

            if (stop.drop != null && stop.drop == carrying)
            {
                props.Put(carrying, stop.dropRest);
                props.Release(null, stop.dropRest);
                (carrying, promised, promisedRest) = (null, null, -1);
            }
        }

        // A round cut short (a fight, the end of the segment, a load) keeps nothing in the air.
        private void LandCarried()
        {
            if (props == null) return;
            if (carrying != null && promisedRest >= 0) props.Put(carrying, promisedRest);
            props.Release(promised, promisedRest);
            (carrying, promised, promisedRest) = (null, null, -1);
        }

        private static List<int> ByDistance(SettlementSociety society, List<int> places, Vector3 from)
        {
            places.Sort((a, b) => Flat(society.Place(a).Position, from).CompareTo(Flat(society.Place(b).Position, from)));
            return places;
        }

        // ── patrol ───────────────────────────────────────────────────────────────────────────────

        private void BeginPatrol(Resident resident, SettlementSociety society, in PlanSegment segment)
        {
            IReadOnlyList<int> ring = society.PatrolPoints;
            ringAt = Mathf.Max(0, IndexOf(ring, segment.place));
            direction = society.PatrolSlotOf(resident) % 2 == 0 ? 1 : -1;
        }

        private ErrandStop NextRingStop(SettlementSociety society)
        {
            IReadOnlyList<int> ring = society.PatrolPoints;
            if (ring.Count == 0) return default;

            SettlementPlace point = society.Place(ring[ringAt % ring.Count]);
            ringAt = (ringAt + direction + ring.Count) % ring.Count;
            return new ErrandStop
            {
                position = point.Position, place = ResidentPresence.NoPlace, shown = Activity.Patrol,
                radius = LooseRadius, speed = ResidentTuning.Instance.patrolSpeed, hold = false,
                advanceWithin = ResidentTuning.Instance.perimeterOffset * 0.7f,
            };
        }

        // ── amble ────────────────────────────────────────────────────────────────────────────────

        private void BeginAmble(SettlementSociety society, in PlanSegment segment)
        {
            strolls.Clear();
            for (int i = 0; i < society.PlaceCount; i++)
            {
                PlaceKind kind = society.Place(i).Kind;
                if (kind is PlaceKind.Stroll or PlaceKind.Hearth) strolls.Add(i);
            }
            lastSpot = segment.place;
            queue.Enqueue(Spot(society, segment.place, Activity.Amble, AmbleStay(), 0));
        }

        // A spot (a seat, a shop counter) or, between those, a point out in the street; never a spot someone stands at.
        private ErrandStop NextAmble(Resident resident, SettlementSociety society)
        {
            if (rng.NextDouble() < SpotAmbleShare || society.AmbleRadius <= 0f)
            {
                int spot = FreeSpot(resident, society);
                if (spot >= 0) return Spot(society, lastSpot = spot, Activity.Amble, AmbleStay(), 0);
            }

            for (int attempt = 0; attempt < PointAttempts; attempt++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2d);
                float distance = society.AmbleRadius * Mathf.Lerp(MinPointShare, 1f, (float)rng.NextDouble());
                Vector3 heart = society.Settlement.WalkableHeart;
                Vector3 candidate = heart + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, PointSnap, NavMesh.AllAreas) || !NavMeshReach.CanWalk(heart, hit.position)) continue;

                return new ErrandStop
                {
                    position = hit.position, place = ResidentPresence.NoPlace, shown = Activity.Amble, radius = LooseRadius, speed = 1f,
                    dwellSeconds = (float)rng.NextDouble() * PointDwellMax, hold = false,
                };
            }

            int fallback = FreeSpot(resident, society);
            return fallback >= 0 ? Spot(society, lastSpot = fallback, Activity.Amble, AmbleStay(), 0) : default;
        }

        private int FreeSpot(Resident resident, SettlementSociety society)
        {
            if (strolls.Count == 0) return -1;

            int offset = rng.Next(strolls.Count);
            for (int k = 0; k < strolls.Count; k++)
            {
                int spot = strolls[(offset + k) % strolls.Count];
                if (spot != lastSpot && !Occupied(resident, society, society.Place(spot).Position)) return spot;
            }
            return -1;
        }

        private static bool Occupied(Resident self, SettlementSociety society, Vector3 point)
        {
            foreach (Resident other in society.Residents)
                if (other && other != self && !other.IsOffstage && Flat(other.transform.position, point) < OccupiedWithin) return true;
            return false;
        }

        private float AmbleStay() => Range(ResidentTuning.Instance.ambleStopSeconds);

        // ── pairs ────────────────────────────────────────────────────────────────────────────────

        // Beside the leader and a little behind, facing where it is heading; hurries to catch up, never overtakes.
        private ErrandStop Follow(Resident resident, Resident leader)
        {
            Transform lead = leader.transform;
            float gap = ResidentTuning.Instance.pairGap;
            Vector3 side = lead.right * (resident.index % 2 == 0 ? gap : -gap);
            float distance = Flat(resident.transform.position, lead.position);
            float baseSpeed = mode == Activity.Patrol ? ResidentTuning.Instance.patrolSpeed : 1f;
            return new ErrandStop
            {
                position = lead.position - lead.forward * BehindLeader + side, face = lead.position + lead.forward * AheadOfLeader,
                place = ResidentPresence.NoPlace, shown = mode, radius = FollowRadius, hold = true,
                speed = distance > FollowCatchUp ? baseSpeed * FollowCatchUpSpeed : baseSpeed,
            };
        }

        // A leader stops for a follower that has fallen behind, and stops waiting for one that never comes.
        private bool HoldsForFollower(Resident resident, SettlementSociety society)
        {
            Resident follower = society.FollowerOf(resident);
            if (follower == null || Time.time < ignoreFollowerUntil || !Walks(follower, mode)) { waiting = false; return false; }

            float apart = Flat(resident.transform.position, follower.transform.position);
            float limit = ResidentTuning.Instance.pairWaitDistance;
            bool wasWaiting = waiting;
            waiting = apart > limit || (wasWaiting && apart > limit * WaitResume);
            if (waiting && !wasWaiting) waitingSince = Time.time;
            if (waiting && Time.time - waitingSince > ForgetFollowerAfter)
            {
                waiting = false;
                ignoreFollowerUntil = Time.time + ForgetFollowerFor;
            }
            return waiting;
        }

        private ErrandStop Wait(Resident resident) => new ErrandStop
        {
            position = resident.transform.position, place = ResidentPresence.NoPlace, shown = mode, radius = SpotRadius, speed = 1f, hold = true,
        };

        private static bool Walks(Resident other, Activity mode) =>
            other && !other.IsOffstage && !other.IsDead &&
            (other.Provocation == null || other.Provocation.Band == AggressionBand.Calm) &&
            other.TryGetComponent(out ResidentRoutine routine) && routine.Current.HasValue && routine.Current.Value.activity == mode;

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        // A spot's own pose is held there on every machine, so the place index travels with the stop.
        private static ErrandStop Spot(SettlementSociety society, int index, Activity shown, float dwellSeconds, byte prop)
        {
            SettlementPlace place = society.Place(index);
            bool isSpot = place.Kind is PlaceKind.Errand or PlaceKind.Stroll or PlaceKind.Hearth or PlaceKind.Post;
            return new ErrandStop
            {
                position = place.Position, face = place.FacePoint, place = isSpot ? index : ResidentPresence.NoPlace, shown = shown,
                radius = SpotRadius, speed = 1f, hold = true, dwellSeconds = dwellSeconds, prop = prop,
            };
        }

        private static int Nearest(SettlementSociety society, List<int> indices, Vector3 from)
        {
            int best = indices[0];
            foreach (int index in indices)
                if (Flat(society.Place(index).Position, from) < Flat(society.Place(best).Position, from)) best = index;
            return best;
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return i;
            return -1;
        }

        private float Range(Vector2 range) => Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());

        private static float Flat(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    }
}
