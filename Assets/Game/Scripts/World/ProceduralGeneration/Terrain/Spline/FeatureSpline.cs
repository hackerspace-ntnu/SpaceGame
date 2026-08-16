using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Smooth Catmull-Rom evaluation of a <see cref="FeaturePath"/>. Linear terrain features sweep
    /// their cross-section along the curve this produces. All sampling is deterministic and allocation
    /// free — same path always gives the same curve.
    ///
    /// Usage by a linear feature:
    ///   var spline = new FeatureSpline(context.Path);
    ///   for t in 0..1:  Vector3 p = spline.Evaluate(t);  Vector3 fwd = spline.Tangent(t);
    ///   // build the canyon / ridge / bridge cross-section at p, oriented by fwd.
    ///
    /// <see cref="ClosestParam"/> is the workhorse for HEIGHTFIELD linear features: given an (x, z)
    /// sample point, it finds the nearest point on the spline so the feature can compute "distance to
    /// the path centre-line" and apply its cross-section profile + overlap falloff.
    /// </summary>
    public sealed class FeatureSpline
    {
        readonly Vector3[] _points;

        /// <summary>Half-width carried over from the source <see cref="FeaturePath"/>.</summary>
        public float HalfWidth { get; }

        /// <summary>True if the spline has enough points to evaluate.</summary>
        public bool IsValid => _points != null && _points.Length >= 2;

        /// <summary>Number of cached control points.</summary>
        public int PointCount => _points != null ? _points.Length : 0;

        public FeatureSpline(FeaturePath path)
        {
            if (path == null || !path.IsValid)
            {
                _points = System.Array.Empty<Vector3>();
                HalfWidth = 0f;
                return;
            }
            _points = path.controlPoints.ToArray();
            HalfWidth = path.halfWidth;
        }

        /// <summary>
        /// Position on the curve at normalised parameter t in [0, 1]. Uses Catmull-Rom through the
        /// control points with duplicated endpoints, so the curve actually reaches its first/last point.
        /// </summary>
        public Vector3 Evaluate(float t)
        {
            if (!IsValid) return Vector3.zero;
            if (_points.Length == 2) return Vector3.Lerp(_points[0], _points[1], Mathf.Clamp01(t));

            int segments = _points.Length - 1;
            float scaled = Mathf.Clamp01(t) * segments;
            int seg = Mathf.Min((int)scaled, segments - 1);
            float local = scaled - seg;

            Vector3 p0 = _points[Mathf.Max(seg - 1, 0)];
            Vector3 p1 = _points[seg];
            Vector3 p2 = _points[seg + 1];
            Vector3 p3 = _points[Mathf.Min(seg + 2, _points.Length - 1)];
            return CatmullRom(p0, p1, p2, p3, local);
        }

        /// <summary>Normalised tangent (travel direction) at parameter t. Falls back to +X if degenerate.</summary>
        public Vector3 Tangent(float t)
        {
            if (!IsValid) return Vector3.right;
            const float eps = 1e-3f;
            Vector3 a = Evaluate(Mathf.Clamp01(t - eps));
            Vector3 b = Evaluate(Mathf.Clamp01(t + eps));
            Vector3 d = b - a;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.right;
        }

        /// <summary>
        /// Finds the parameter t whose curve point is nearest to <paramref name="worldOrLocalPoint"/>,
        /// comparing in the XZ plane only (heightfield features sweep along ground distance). Also
        /// outputs the signed lateral distance from the centre-line — negative on the left of the
        /// travel direction, positive on the right — and the closest curve point itself.
        /// </summary>
        public float ClosestParam(Vector3 point, out float lateralDistance, out Vector3 closestPoint, int resolution = 64)
        {
            lateralDistance = 0f;
            closestPoint = Vector3.zero;
            if (!IsValid) return 0f;

            float bestT = 0f;
            float bestSqr = float.MaxValue;
            Vector2 p2 = new Vector2(point.x, point.z);

            // Coarse scan, then nothing fancier — features call this per voxel-column, so keep it cheap.
            for (int i = 0; i <= resolution; i++)
            {
                float t = i / (float)resolution;
                Vector3 c = Evaluate(t);
                float sqr = (new Vector2(c.x, c.z) - p2).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; bestT = t; closestPoint = c; }
            }

            Vector3 fwd = Tangent(bestT);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x).normalized;
            Vector3 offset = point - closestPoint;
            lateralDistance = offset.x * right.x + offset.z * right.z;
            return bestT;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
