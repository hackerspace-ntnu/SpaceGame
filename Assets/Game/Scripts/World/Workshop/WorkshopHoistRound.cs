using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// The round the main room's hoist makes by itself: out along its runways, across its bridge, hook down, a
    /// rest, hook up, and on to the next call, for ever. Nobody works it. It is the workshop's only crane.
    ///
    /// <para>
    /// <b>A place for every second.</b> The whole round is planned once from the stops and the calls, and
    /// <see cref="At"/> answers where the hoist is at any time on a clock. Every machine asks with the same clock,
    /// so every machine sees the hoist in the same place without a word on the wire, and a joiner or a loaded
    /// save is right at once.
    /// </para>
    /// <para>
    /// It keeps the two manners it had when it was worked by hand: bridge and trolley only travel with the hook
    /// right up, and the hook stops on whatever stands under it (<c>hookLimits</c>, measured by the export).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WorkshopHoistRound
    {
        private const int Bridge = 0;
        private const int Trolley = 1;
        private const int Hook = 2;
        private const int Rest = -1;

        [Tooltip("Stops along each axis, counting the modelled one: x the bridge, y the trolley, z the hook.")]
        [SerializeField] private Vector3Int stops = new(8, 8, 5);
        [Tooltip("How many stops down the hook may go at each place, a row per bridge stop. Written by the prefab builder.")]
        [SerializeField] private int[] hookLimits = Array.Empty<int>();
        [Tooltip("Metres between stops: x the bridge, y the trolley, z the hook.")]
        [SerializeField] private Vector3 metresPerStop = new(2f, 2f, 3f);
        [SerializeField, Min(0.05f)] private float metresPerSecond = 1.2f;
        [Tooltip("Seconds the hook hangs at the bottom of each call.")]
        [SerializeField, Min(0f)] private float restSeconds = 5f;
        [Tooltip("The places it calls at, in order: x the bridge stop, y the trolley stop. It goes home after the last.")]
        [SerializeField] private Vector2Int[] calls =
        {
            new(1, 2), new(6, 1), new(4, 6), new(7, 4), new(2, 7), new(5, 3), new(0, 5), new(3, 0),
        };

        private struct Leg
        {
            public double Start;
            public float Seconds;
            public int Axis;
            public float From;
            public float To;
        }

        private List<Leg> legs;
        private double total;

        public WorkshopHoistRound() { }

        public WorkshopHoistRound(Vector3Int stops, int[] hookLimits, Vector3 metresPerStop, float metresPerSecond,
                                  float restSeconds, Vector2Int[] calls)
        {
            this.stops = stops;
            this.hookLimits = hookLimits;
            this.metresPerStop = metresPerStop;
            this.metresPerSecond = metresPerSecond;
            this.restSeconds = restSeconds;
            this.calls = calls;
        }

        /// <summary>Seconds one whole round takes; zero for a round that goes nowhere.</summary>
        public double Seconds
        {
            get
            {
                Plan();
                return total;
            }
        }

        /// <summary>
        /// Metres the bridge (x), the trolley (y) and the hook (z) stand from where they are modelled,
        /// <paramref name="seconds"/> into a clock that never stops.
        /// </summary>
        public Vector3 At(double seconds)
        {
            Plan();
            if (total <= 0d) return Vector3.zero;

            double into = seconds % total;
            if (into < 0d) into += total;

            Vector3 at = Vector3.zero;
            foreach (Leg leg in legs)
            {
                if (into < leg.Start) break;

                bool done = into >= leg.Start + leg.Seconds;
                if (leg.Axis != Rest)
                    at[leg.Axis] = done ? leg.To : Mathf.Lerp(leg.From, leg.To, (float)((into - leg.Start) / leg.Seconds));
                if (!done) break;
            }
            return at;
        }

        private void Plan()
        {
            if (legs != null) return;

            legs = new List<Leg>();
            total = 0d;
            int bridge = 0, trolley = 0;

            foreach (Vector2Int call in calls)
            {
                TravelTo(call, ref bridge, ref trolley);

                int depth = Mathf.Min(LimitAt(bridge, trolley), stops.z - 1);
                if (depth <= 0) continue;

                Add(Hook, 0f, depth * metresPerStop.z);
                Add(Rest, 0f, 0f);
                Add(Hook, depth * metresPerStop.z, 0f);
            }

            TravelTo(Vector2Int.zero, ref bridge, ref trolley);
        }

        private void TravelTo(Vector2Int call, ref int bridge, ref int trolley)
        {
            int nextBridge = Mathf.Clamp(call.x, 0, Mathf.Max(stops.x - 1, 0));
            int nextTrolley = Mathf.Clamp(call.y, 0, Mathf.Max(stops.y - 1, 0));

            Add(Bridge, bridge * metresPerStop.x, nextBridge * metresPerStop.x);
            Add(Trolley, trolley * metresPerStop.y, nextTrolley * metresPerStop.y);
            bridge = nextBridge;
            trolley = nextTrolley;
        }

        private void Add(int axis, float from, float to)
        {
            float seconds = axis == Rest ? restSeconds : Mathf.Abs(to - from) / Mathf.Max(metresPerSecond, 0.05f);
            if (seconds <= 0f) return;

            legs.Add(new Leg { Start = total, Seconds = seconds, Axis = axis, From = from, To = to });
            total += seconds;
        }

        /// <summary>A place the table does not cover is a place nothing was measured at: the hook stays up there.</summary>
        private int LimitAt(int bridge, int trolley)
        {
            int at = (bridge * stops.y) + trolley;
            return hookLimits != null && at >= 0 && at < hookLimits.Length ? hookLimits[at] : 0;
        }
    }
}
