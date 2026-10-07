// Where a settlement's band musters when no prefab brought a muster spot along. Settlements are generated and no
// two look alike, so the spot is placed by one rule. The road out is the street whose trail reaches farthest from
// the centre; the band forms up at the edge of the built settlement on it -- a few metres back inside the end of
// that street's PAVING (road, slabs, stairs), not out where its stepping-stone path fades into the desert -- in view
// of home, facing out along the paving. The departure and hand-off distances are measured from here.
//
// Generate applies the rule to the lanes it has just grown (SettlementLayoutResult.lanes, paved where the street's
// surface is not a stone path). A settlement generated earlier keeps no street data, so Tools/SpaceGame/Expeditions/
// Place Muster Spots traces its streets from the pieces laid along them (TraceStreets) and applies the same rule.
// Both the rule and the trace are pure -- points in, a pose out -- so they are tested without a scene; only TryPlace
// touches one.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementMuster
    {
        /// <summary>The name of a muster spot placed by rule, directly under the settlement's Generated root.</summary>
        public const string SpotName = "MusterSpot";

        // Two street ends this close in their distance from the centre are a tie, broken by bearing.
        private const float TieMetres = 0.001f;

        /// <summary>A point along a street, and whether the street is paved there (road, slabs, stairs) or a stone path.</summary>
        public readonly struct StreetPoint
        {
            public readonly Vector3 position;
            public readonly bool paved;

            public StreetPoint(Vector3 position, bool paved) => (this.position, this.paved) = (position, paved);
        }

        /// <summary>
        /// The muster pose for <paramref name="streets"/> (each a polyline from the centre outward). The street whose last point
        /// lies farthest from <paramref name="centre"/> across the ground is the road out -- on a tie, the one whose end has the
        /// smaller bearing clockwise from +Z, so the answer never depends on the streets' order. Its paved end is its last paved
        /// point (its end when nothing past its first point is paved). The pose stands <paramref name="inset"/> metres back from the paved end,
        /// measured along the street (its start, on a shorter one), level and facing from there to the paved end: the last
        /// metres of paving, which run the way the street does where the band stands. Its height follows the street; callers
        /// snap it to the ground. False when no street has two points.
        /// </summary>
        public static bool TryChoose(Vector3 centre, IReadOnlyList<IReadOnlyList<StreetPoint>> streets, float inset, out Pose pose)
        {
            pose = default;
            IReadOnlyList<StreetPoint> chosen = null;
            float chosenReach = 0f, chosenBearing = 0f;
            foreach (IReadOnlyList<StreetPoint> street in streets)
            {
                if (street == null || street.Count < 2) continue;

                Vector3 end = street[street.Count - 1].position;
                float reach = Flat(end - centre).magnitude, bearing = Bearing(end - centre);
                bool farther = reach > chosenReach + TieMetres;
                bool tiedFirstClockwise = Mathf.Abs(reach - chosenReach) <= TieMetres && bearing < chosenBearing;
                if (chosen != null && !farther && !tiedFirstClockwise) continue;
                (chosen, chosenReach, chosenBearing) = (street, reach, bearing);
            }
            if (chosen == null) return false;

            int pavedEnd = chosen.Count - 1;
            while (pavedEnd > 0 && !chosen[pavedEnd].paved) pavedEnd--;
            if (pavedEnd == 0) pavedEnd = chosen.Count - 1;   // paved at most at its start: the trail is the whole street

            Vector3 edge = chosen[pavedEnd].position;
            Vector3 at = InsetBefore(chosen, pavedEnd, inset);
            Vector3 outward = Flat(edge - at);
            if (outward.sqrMagnitude < TieMetres * TieMetres) outward = Flat(edge - centre);
            if (outward.sqrMagnitude < TieMetres * TieMetres) return false;

            pose = new Pose(at, Yaw(outward));
            return true;
        }

        /// <summary>
        /// The streets of a settlement generated earlier, rebuilt from the pieces laid along them. Pieces within
        /// <paramref name="link"/> metres of each other (across the ground) are joined; every street runs from the piece nearest
        /// <paramref name="centre"/> out to a piece nothing lies beyond, along the chain that minimises the sum of SQUARED
        /// steps, so it visits every piece of a row instead of leaping over them (the last slab is where the paving ends). A
        /// piece no chain reaches belongs to no street. Deterministic for one input order.
        /// </summary>
        public static List<StreetPoint[]> TraceStreets(Vector3 centre, IReadOnlyList<StreetPoint> pieces, float link)
        {
            var streets = new List<StreetPoint[]>();
            int count = pieces.Count;
            if (count == 0) return streets;

            int root = 0;
            for (int i = 1; i < count; i++)
                if (Flat(pieces[i].position - centre).sqrMagnitude < Flat(pieces[root].position - centre).sqrMagnitude) root = i;

            // Dijkstra over the pieces, dense: a settlement lays a few hundred.
            var cost = new float[count];
            var parent = new int[count];
            var settled = new bool[count];
            for (int i = 0; i < count; i++) (cost[i], parent[i]) = (float.PositiveInfinity, -1);
            cost[root] = 0f;
            for (int step = 0; step < count; step++)
            {
                int next = -1;
                for (int i = 0; i < count; i++)
                    if (!settled[i] && !float.IsPositiveInfinity(cost[i]) && (next < 0 || cost[i] < cost[next])) next = i;
                if (next < 0) break;

                settled[next] = true;
                for (int i = 0; i < count; i++)
                {
                    if (settled[i]) continue;
                    float gap = Flat(pieces[i].position - pieces[next].position).magnitude;
                    if (gap > link || cost[next] + gap * gap >= cost[i]) continue;
                    (cost[i], parent[i]) = (cost[next] + gap * gap, next);
                }
            }

            var hasChild = new bool[count];
            for (int i = 0; i < count; i++)
                if (parent[i] >= 0) hasChild[parent[i]] = true;
            for (int i = 0; i < count; i++)
            {
                if (hasChild[i] || float.IsPositiveInfinity(cost[i])) continue;

                var chain = new List<StreetPoint>();
                for (int k = i; k >= 0; k = parent[k]) chain.Add(pieces[k]);
                chain.Reverse();
                streets.Add(chain.ToArray());
            }
            return streets;
        }

        /// <summary>The first active spot of <paramref name="use"/> under <paramref name="generated"/>, by rule or on a prefab; null when there is none.</summary>
        public static SettlementSpot Find(Transform generated, SpotUse use)
        {
            if (generated == null || use == null) return null;
            foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>())
                if (spot.Use == use) return spot;
            return null;
        }

        /// <summary>
        /// Puts a <see cref="SpotName"/> spot of <paramref name="use"/> under <paramref name="generated"/> at <paramref name="pose"/>,
        /// on the highest NavMesh surface there that is walkable from <paramref name="heart"/>. Needs a NavMesh over the settlement.
        /// False, placing nothing, with <paramref name="why"/>, when nothing there is walkable from the heart.
        /// </summary>
        public static bool TryPlace(Transform generated, SpotUse use, Pose pose, Vector3 heart, out SettlementSpot spot, out string why)
        {
            spot = null;
            why = null;
            if (!WalkableSnap.TryReachable(pose.position, heart, out Vector3 ground))
            {
                why = $"nothing at {pose.position} is walkable from the settlement's heart {heart}";
                return false;
            }

            var host = new GameObject(SpotName);
            host.transform.SetParent(generated, worldPositionStays: false);
            host.transform.SetPositionAndRotation(ground, pose.rotation);
            spot = host.AddComponent<SettlementSpot>();
            spot.SetUse(use);
            return true;
        }

        // Walks back from point `from` of the street toward its start, across the ground, until inset metres are behind.
        private static Vector3 InsetBefore(IReadOnlyList<StreetPoint> street, int from, float inset)
        {
            float left = inset;
            for (int i = from; i > 0; i--)
            {
                Vector3 here = street[i].position, before = street[i - 1].position;
                float leg = Flat(here - before).magnitude;
                if (leg >= left) return Vector3.Lerp(here, before, leg > 0f ? left / leg : 0f);
                left -= leg;
            }
            return street[0].position;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Degrees clockwise from +Z, in [0, 360).
        private static float Bearing(Vector3 v) => Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);

        // A turn about +Y that faces +Z along the level direction, built by hand (Quaternion.LookRotation is a native
        // call, and the rule stays pure).
        private static Quaternion Yaw(Vector3 level)
        {
            float half = Mathf.Atan2(level.x, level.z) * 0.5f;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }
    }
}
