// The geometry of a herd drive, with no GameObject anywhere near it.
//
// A drive is a point rider out in front, whom the herd follows, and herders behind and beside it:
// the drag directly behind, the flank riders swung round on either side. HerdingModule asks this
// class where its herder should be and which herder goes after a stray; keeping the answers pure
// is what lets them be asserted (HerdingMathTests) instead of judged by watching Appas walk.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class HerdingMath
    {
        /// <summary>
        /// Where herder <paramref name="index"/> of <paramref name="count"/> rides, in degrees measured
        /// from directly behind the herd (positive swings round to the herd's right). One herder rides
        /// the drag (0°); more spread evenly from one end of <paramref name="arcDegrees"/> to the other,
        /// so two take opposite flanks and a third rides the drag between them.
        /// </summary>
        public static float StationAngle(int index, int count, float arcDegrees)
        {
            if (count <= 1) return 0f;
            return -arcDegrees * 0.5f + arcDegrees * index / (count - 1);
        }

        /// <summary>
        /// A herder's station: out from <paramref name="centre"/> by the herd's radius plus
        /// <paramref name="standOff"/>, behind <paramref name="heading"/> and swung round by
        /// <paramref name="angleDegrees"/>. Flat: the height is the centre's, for the caller to put on
        /// the ground. A herd with no heading yet is treated as facing +Z.
        /// </summary>
        public static Vector3 StationPoint(Vector3 centre, Vector3 heading, float herdRadius, float standOff,
                                           float angleDegrees)
        {
            Vector3 forward = Flat(heading);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            Vector3 behind = Quaternion.AngleAxis(-angleDegrees, Vector3.up) * -forward;
            return centre + behind * (herdRadius + standOff);
        }

        /// <summary>The mean position of the herd.</summary>
        public static Vector3 Centroid(IReadOnlyList<Vector3> heads)
        {
            if (heads.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < heads.Count; i++) sum += heads[i];
            return sum / heads.Count;
        }

        /// <summary>How far the herd spreads from its centre, measured flat, and never under <paramref name="minimum"/>.</summary>
        public static float HerdRadius(IReadOnlyList<Vector3> heads, Vector3 centre, float minimum)
        {
            float radius = minimum;
            for (int i = 0; i < heads.Count; i++)
                radius = Mathf.Max(radius, Flat(heads[i] - centre).magnitude);
            return radius;
        }

        /// <summary>The head farthest from the centre beyond <paramref name="strayRadius"/>, or -1 when the herd is together.</summary>
        public static int StrayIndex(IReadOnlyList<Vector3> heads, Vector3 centre, float strayRadius)
        {
            int stray = -1;
            float farthest = strayRadius;

            for (int i = 0; i < heads.Count; i++)
            {
                float distance = Flat(heads[i] - centre).magnitude;
                if (distance <= farthest) continue;

                farthest = distance;
                stray = i;
            }

            return stray;
        }

        /// <summary>
        /// Where to ride to turn a stray: <paramref name="standOff"/> beyond it on the side away from the
        /// herd, so the herder comes at it from outside.
        /// </summary>
        public static Vector3 FetchPoint(Vector3 stray, Vector3 centre, float standOff)
        {
            Vector3 outward = Flat(stray - centre);
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            return stray + outward * standOff;
        }

        /// <summary>The index of the point nearest <paramref name="target"/>, measured flat, or -1 for none.</summary>
        public static int NearestIndex(IReadOnlyList<Vector3> points, Vector3 target)
        {
            int nearest = -1;
            float best = float.PositiveInfinity;

            for (int i = 0; i < points.Count; i++)
            {
                float distance = Flat(points[i] - target).sqrMagnitude;
                if (distance >= best) continue;

                best = distance;
                nearest = i;
            }

            return nearest;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
