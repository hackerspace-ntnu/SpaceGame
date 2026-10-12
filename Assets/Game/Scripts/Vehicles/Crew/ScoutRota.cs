// The walking city's scouts: of the monowheels riding in its column, keep scoutsOut of them away on
// a sweep -- a closed loop of waypoints on a ring round the city -- and when one is back, send the
// one that has been home longest. Server-side only (Network.Simulates); a scout's movement
// replicates like any other agent's, so there is nothing to send. The choices are ScoutRotaLogic's.
//
// Every house carries one (they are one prefab variant), and only the house the column follows
// acts: FormationModule.LeadsFormation is asked every tick, as CrewShift does for its gate, because
// the lead moves on an unfold or a reload. A house that stops leading, or is switched off, calls its
// scouts home, so the house that leads next starts from a column with nobody out.
//
// How a scout leaves the column without fighting anyone for it. The wheel's FormationModule and
// GoalTravelModule have two owners of their `enabled` already: MonowheelDriverGate parks them while
// nobody is at the tiller, and MountModule while a player drives. So the rota never writes
// `enabled`. It turns the FormationModule's runtime `active` switch off (SetRuntimeActive), which
// nothing else writes, and sets the wheel's AgentGoal to the next waypoint; GoalTravelModule drives
// there, at the cruise speed its own speedMultiplier gives every Strider wheel (the builder's). A
// scout stays registered in the formation while it is out, so the rest of the column keeps its
// slots and nobody reshuffles. Coming home is switching formation back on and clearing the goal:
// past regroupDistance the formation rides it straight back to the leader.
// It still counts as away while it rides back, and frees its place in the rota only once it is
// within its regroupDistance of the leader -- so exactly scoutsOut are away from the column at a
// time. A scout whose rider is lost (dead, knocked off, or a player took the wheel) is dropped at
// once, until a rider sits at its tiller again; the gate then parks the riderless wheel wherever it
// is, exactly as it would in the column.
//
// A sweep stays on streamed-in ground. The NavMesh is global but the terrain streams in 500 m
// chunks round the players, and the ring reaches past them, so a waypoint whose chunk is not loaded
// is pulled in toward the city (pullInStep at a time) until it is, or skipped; with nothing left the
// sweep ends. Chunks are never pinned for a scout.
//
// Nothing is saved. The city's members are never saved on their own (the group record decides), so
// after a reload -- or a fold and unfold -- every scout spawns from its prefab in formation, the
// sweep in progress is forgotten, and the rota sends a fresh pair on its first tick.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.Monowheel;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    [RequireComponent(typeof(FormationModule))]
    [DisallowMultipleComponent]
    public class ScoutRota : MonoBehaviour
    {
        [Tooltip("How many scouts are away from the column at once, riding a sweep or riding back.")]
        [SerializeField] private int scoutsOut = 2;

        [Tooltip("Radius of the ring the sweep rides round the city, in metres.")]
        [SerializeField] private float sweepRadius = 600f;

        [Tooltip("Waypoints on that ring, ridden in turn and back to the first.")]
        [SerializeField] private int sweepPoints = 6;

        [Tooltip("How close to a waypoint counts as reaching it.")]
        [SerializeField] private float sweepArriveRadius = 25f;

        [Tooltip("How far from a waypoint to look for NavMesh to put it on. Off the mesh (or on ground " +
                 "not loaded yet) the waypoint is used as it is and the wheel steers straight at it.")]
        [SerializeField] private float sweepSampleDistance = 60f;

        [Tooltip("A waypoint whose ground has not streamed in is pulled in toward the city this many " +
                 "metres at a time until its chunk is loaded -- the NavMesh is global, the terrain is " +
                 "not, so a wheel sent onto an unloaded chunk falls through the world. With nothing " +
                 "loaded along the way the waypoint is skipped; with no waypoint left the sweep ends.")]
        [SerializeField] private float pullInStep = 100f;

        [Tooltip("Seconds before a sweep is abandoned and the scout sent home, whether or not it rode " +
                 "the whole loop -- a waypoint behind a cliff must not keep a scout out for ever. Must " +
                 "outlast the loop at the wheels' cruise speed (the habitat builder sets it so).")]
        [SerializeField] private float sweepTimeout = 450f;

        [Tooltip("Seconds between the rota's looks at who is home.")]
        [SerializeField] private float rotaInterval = 1f;

        /// <summary>Written on a scout's goal, so the rota only ever clears a goal it set.</summary>
        public const string SweepReason = "scouting round the city";

        private const int MinSweepPoints = 3;
        private const float MinArriveRadius = 1f;
        private const float MinRotaInterval = 0.1f;

        private sealed class Outing
        {
            public FormationModule Scout;
            public AgentGoal Goal;
            public Vector3[] Offsets;   // round the leader, re-centred as each waypoint is issued
            public int Next;            // waypoints reached so far; the loop closes back on Offsets[0]
            public float StartedAt;
            public bool Returning;      // swept; formation back on, riding home, still away
        }

        private readonly List<Outing> outings = new();
        private readonly Dictionary<FormationModule, float> cameHomeAt = new();
        private readonly List<FormationModule> members = new();
        private readonly List<FormationModule> scouts = new();
        private readonly List<ScoutRecord> records = new();
        private readonly List<FormationModule> stale = new();
        private FormationModule formation;
        private float nextLook;
        private Func<Vector3, bool> groundLoaded;

        /// <summary>How many scouts this house has away from the column, sweeping or riding back.</summary>
        public int OutCount => outings.Count;

        /// <summary>Away from the column: on a sweep or riding back from one.</summary>
        public bool IsOut(FormationModule scout) => OutingOf(scout) != null;

        /// <summary>On a sweep right now (not yet riding back).</summary>
        public bool IsSweeping(FormationModule scout) => OutingOf(scout) is { Returning: false };

        public float SweepTimeout => sweepTimeout;

        private FormationModule Formation => formation != null ? formation : formation = GetComponent<FormationModule>();

        /// <summary>Whether the ground at a point has streamed in: the world streamer's chunk state (no streamer, no streaming -- all of it).</summary>
        private Func<Vector3, bool> GroundLoaded => groundLoaded ??= StreamedGroundCheck();

        /// <summary>Replace the streamed-ground check. For tests, which have no world streamer.</summary>
        public void SetGroundCheck(Func<Vector3, bool> check) => groundLoaded = check;

        private static Func<Vector3, bool> StreamedGroundCheck()
        {
            // Chunk state is tracked on the machine that issues the loads -- the server, where the rota runs.
            WorldStreamer streamer = FindFirstObjectByType<WorldStreamer>();
            return streamer != null ? streamer.IsChunkLoadedAt : _ => true;
        }

        private void Update()
        {
            if (!Network.Simulates(this)) return;
            Tick(Time.time);
        }

        private void OnDisable() => CallEveryoneHome(Time.time);

        /// <summary>One rota step at <paramref name="now"/>. Public so it can be tested without a play session.</summary>
        public void Tick(float now)
        {
            if (!Formation.LeadsFormation)
            {
                CallEveryoneHome(now);
                return;
            }

            RideOutings(now);

            if (now < nextLook) return;
            nextLook = now + rotaInterval;
            SendNext(now);
        }

        private void RideOutings(float now)
        {
            for (int i = outings.Count - 1; i >= 0; i--)
            {
                Outing outing = outings[i];
                if (outing.Scout == null) { outings.RemoveAt(i); cameHomeAt.Remove(outing.Scout); continue; }

                // Lost its rider (or parked by the gate or a player's mount): nothing left to scout with.
                if (!CanRide(outing.Scout)) { Release(outing, now); continue; }

                if (outing.Returning)
                {
                    if (ScoutRotaLogic.BackWithTheColumn(FlatDistance(outing.Scout.transform.position),
                                                         outing.Scout.RegroupDistance))
                    {
                        outings.RemoveAt(i);
                        cameHomeAt[outing.Scout] = now;
                    }
                    continue;
                }

                // Somebody else took its goal.
                if (!OwnsGoal(outing.Goal)) { Release(outing, now); continue; }

                int stops = ScoutRotaLogic.LoopStops(outing.Offsets.Length);
                // Head skips what it cannot reach; with nothing left, Next runs out and the loop is over.
                if (outing.Goal.HasArrived && ++outing.Next < stops)
                    Head(outing);

                bool rodeTheLoop = outing.Next >= stops;
                if (ScoutRotaLogic.SweepOver(now - outing.StartedAt, sweepTimeout, rodeTheLoop))
                    TurnForHome(outing);
            }
        }

        private void SendNext(float now)
        {
            if (outings.Count >= scoutsOut) return;

            FormationModule.CollectMembers(Formation.FormationId, members);
            scouts.Clear();
            records.Clear();

            foreach (FormationModule member in members)
            {
                if (member == Formation || !member.TryGetComponent(out MonowheelMotor _)) continue;

                bool isOut = IsOut(member);
                // Switched off by somebody else: not the rota's to send.
                if (!isOut && !member.IsActive) continue;

                if (!cameHomeAt.TryGetValue(member, out float since)) cameHomeAt[member] = since = now;
                scouts.Add(member);
                records.Add(new ScoutRecord
                {
                    Alive = CanRide(member),
                    Out = isOut,
                    Fighting = IsFighting(member),
                    HomeFor = now - since,
                });
            }

            ForgetTheGone();

            List<int> picked = ScoutRotaLogic.PickNext(records, scoutsOut);
            if (picked.Count == 0) return;

            // A pair sent together starts on opposite sides of the ring, so they cover it between them.
            float start = UnityEngine.Random.Range(0f, 360f);
            for (int k = 0; k < picked.Count; k++)
                Send(scouts[picked[k]], start + k * 360f / picked.Count, now);
        }

        private void Send(FormationModule scout, float startBearing, float now)
        {
            var outing = new Outing
            {
                Scout = scout,
                Goal = AgentGoal.GetOrAdd(scout.gameObject),
                Offsets = ScoutRotaLogic.SweepPoints(Vector3.zero, sweepRadius, sweepPoints, startBearing),
                StartedAt = now,
            };

            // Nowhere on the ring has ground streamed in: stay with the column.
            if (!Head(outing)) return;

            scout.SetRuntimeActive(false);
            outings.Add(outing);
        }

        /// <summary>
        /// Point the scout at its next waypoint, placed round where the city is now -- pulled in toward
        /// the city until its ground is streamed in, or skipped for the one after when none is. False
        /// (with <see cref="Outing.Next"/> run out) when no waypoint left in the loop can be reached.
        /// </summary>
        private bool Head(Outing outing)
        {
            int stops = ScoutRotaLogic.LoopStops(outing.Offsets.Length);
            for (; outing.Next < stops; outing.Next++)
            {
                Vector3 offset = outing.Offsets[outing.Next % outing.Offsets.Length];
                if (!ScoutRotaLogic.TryPullIn(transform.position, offset, pullInStep, sweepArriveRadius,
                                              GroundLoaded, out Vector3 point))
                    continue;

                if (!outing.Goal.TrySetSampled(point, sweepArriveRadius, sweepSampleDistance, SweepReason))
                    outing.Goal.Set(point, sweepArriveRadius, SweepReason);
                return true;
            }

            return false;
        }

        /// <summary>The sweep is over: formation back on, and the scout rides home, still counted away.</summary>
        private void TurnForHome(Outing outing)
        {
            outing.Returning = true;
            outing.Scout.SetRuntimeActive(true);
            if (OwnsGoal(outing.Goal)) outing.Goal.Clear();
        }

        /// <summary>Give the scout back to the column and forget the outing.</summary>
        private void Release(Outing outing, float now)
        {
            outings.Remove(outing);
            if (outing.Scout == null) return;

            if (!outing.Returning) TurnForHome(outing);
            cameHomeAt[outing.Scout] = now;
        }

        private void CallEveryoneHome(float now)
        {
            for (int i = outings.Count - 1; i >= 0; i--)
                Release(outings[i], now);
        }

        private void ForgetTheGone()
        {
            stale.Clear();
            foreach (FormationModule known in cameHomeAt.Keys)
                if (known == null || !scouts.Contains(known)) stale.Add(known);
            foreach (FormationModule gone in stale)
                if (!IsOut(gone)) cameHomeAt.Remove(gone);
        }

        private Outing OutingOf(FormationModule scout)
        {
            foreach (Outing outing in outings)
                if (outing.Scout == scout) return outing;
            return null;
        }

        private float FlatDistance(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private static bool OwnsGoal(AgentGoal goal) =>
            goal != null && goal.HasGoal && goal.Reason == SweepReason;

        /// <summary>A living wheel with an NPC at its tiller and its column brain not parked by anyone.</summary>
        private static bool CanRide(FormationModule scout) =>
            scout.isActiveAndEnabled
            && (!scout.TryGetComponent(out HealthComponent health) || health.Alive)
            && scout.TryGetComponent(out NpcPassenger passenger) && passenger.HasRider;

        private static bool IsFighting(FormationModule scout) =>
            scout.TryGetComponent(out NpcPassenger passenger) && passenger.HasRider
            && passenger.Rider.TryGetComponent(out AgentTargeting targeting) && targeting.HasTarget;

        private void OnValidate()
        {
            // The fields others are clamped against come first.
            sweepArriveRadius = Mathf.Max(MinArriveRadius, sweepArriveRadius);
            rotaInterval = Mathf.Max(MinRotaInterval, rotaInterval);
            scoutsOut = Mathf.Max(0, scoutsOut);
            sweepPoints = Mathf.Max(MinSweepPoints, sweepPoints);
            sweepRadius = Mathf.Max(sweepArriveRadius, sweepRadius);
            sweepSampleDistance = Mathf.Max(0f, sweepSampleDistance);
            pullInStep = Mathf.Max(sweepArriveRadius, pullInStep);
            sweepTimeout = Mathf.Max(rotaInterval, sweepTimeout);
        }
    }
}
