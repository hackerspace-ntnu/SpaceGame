// The plane the machine's feet are actually standing on.
//
// Not the same question as WalkerSurface, which fits the patch under ONE sole. This is the whole
// machine's support: given where every grounded foot is, what is the ground doing UNDER THE BODY --
// how high, and which way is it tilted.
//
// A body that ignores the answer and holds itself dead level cannot walk across a slope. Its hips
// all sit at one height while its feet do not, so the downhill legs have to span the ride height
// PLUS the whole drop of the slope. Past their reach they simply stop arriving, and the machine
// walks along on its uphill legs with the downhill ones hanging in the air. Tilting the body toward
// this plane moves each hip toward its own foot, which is what puts the reach back.
//
// The fit is LEAST SQUARES, not the supporting-plane fit WalkerSurface uses, and the difference
// matters: a sole must rest on top of everything under it or it sinks a corner, but a body wants
// the average of its feet -- pushing it up to clear the highest one is exactly the behaviour that
// strands the lowest.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public struct WalkerSupportPlane
    {
        /// Unit normal, pointing up, in whatever frame the points were given in.
        public Vector3 Normal;

        /// Height of the plane at the origin of that frame -- which, when the points are given
        /// relative to the body, is the height of the ground under the body itself.
        public float Height;

        /// False when the feet do not span a plane: fewer than three of them, or all in a line. A
        /// machine standing on two feet has no roll to measure, and inventing one would be noise.
        public bool Valid;

        public static WalkerSupportPlane Level => new WalkerSupportPlane
        {
            Normal = Vector3.up, Height = 0f, Valid = false,
        };

        /// Least-squares fit of y = a·x + b·z + c through `count` points.
        ///
        /// The points are centred first, which makes c fall out and leaves a symmetric 2x2 --
        /// cheaper, and far better conditioned than solving the 3x3 on raw coordinates that may sit
        /// hundreds of units from the origin.
        public static bool TryFit(Vector3[] points, int count, out WalkerSupportPlane plane)
        {
            plane = Level;
            if (points == null || count < 3 || count > points.Length) return false;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < count; i++) mean += points[i];
            mean /= count;

            float xx = 0f, xz = 0f, zz = 0f, xy = 0f, zy = 0f;
            for (int i = 0; i < count; i++)
            {
                float x = points[i].x - mean.x;
                float y = points[i].y - mean.y;
                float z = points[i].z - mean.z;
                xx += x * x;
                xz += x * z;
                zz += z * z;
                xy += x * y;
                zy += z * y;
            }

            float determinant = xx * zz - xz * xz;

            // Feet in a line span no plane: every tilt about that line fits them equally well, so
            // there is no answer to give. The scale-relative guard is what stops a nearly-collinear
            // stance -- four legs almost in a row -- turning a millimetre of measurement noise into
            // an enormous slope.
            if (Mathf.Abs(determinant) < 1e-6f * Mathf.Max(xx * zz, 1e-6f)) return false;

            float a = (xy * zz - zy * xz) / determinant;
            float b = (zy * xx - xy * xz) / determinant;

            plane.Normal = new Vector3(-a, 1f, -b).normalized;
            plane.Height = mean.y - a * mean.x - b * mean.z;
            plane.Valid = true;
            return true;
        }

        /// Rotation that takes level onto this plane, scaled by `follow` and capped at `maxTilt`
        /// degrees.
        ///
        /// `follow` is the whole tunable: 0 holds a deck rigidly level and will strand legs on any
        /// slope worth the name, 1 lies the body flat on the hillside. Neither extreme suits a
        /// machine that has to cross a hillside AND carry people, which is why this is a fraction
        /// rather than a flag.
        public Quaternion Tilt(float follow, float maxTilt)
        {
            if (!Valid || follow <= 0f || maxTilt <= 0f) return Quaternion.identity;

            Quaternion full = Quaternion.FromToRotation(Vector3.up, Normal);
            Quaternion wanted = Quaternion.Slerp(Quaternion.identity, full, Mathf.Clamp01(follow));

            float angle = Quaternion.Angle(Quaternion.identity, wanted);
            if (angle <= maxTilt || angle < 1e-4f) return wanted;

            return Quaternion.Slerp(Quaternion.identity, wanted, maxTilt / angle);
        }
    }
}
