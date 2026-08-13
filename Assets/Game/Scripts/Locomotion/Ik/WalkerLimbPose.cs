// Where a solved limb's joints actually are, in world space.
//
// WalkerLimbSolver answers "what angles reach this foothold". Until this existed nothing could ask
// the complementary question -- "and where does that put the knee" -- which is precisely why a shin
// could be driven through a hillside with nothing in the machine noticing. Terrain clearance needs
// the segments, not the angles.
//
// The construction is the same one WalkerLimbSolver.SoleFromResult uses, extended to report every
// joint rather than only the last. That the two agree is a test.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerLimbPose
    {
        /// One limb's joint centres, root-most first, ending at the contact point the IK was
        /// aiming at. On the classic leg that is hip, knee, ankle, sole.
        public struct Joints
        {
            /// Joint centres. `Points[0]` is the first pitch joint and `Points[^1]` the contact,
            /// so there is one more point than there are segments.
            public Vector3[] Points;

            /// How many of `Points` are this limb's. A caller-supplied buffer may be longer.
            public int Count;

            public Vector3 Hip => At(0);
            public Vector3 Knee => At(1);
            public Vector3 Ankle => At(Count - 2);
            public Vector3 Sole => At(Count - 1);

            private Vector3 At(int i)
                => Points != null && i >= 0 && i < Count ? Points[i] : Vector3.zero;
        }

        /// Forward kinematics of a solved pose. The limb is a plane hanging off the yaw axle, so
        /// this is the 2D chain walked out and then mapped into that plane's world basis.
        ///
        /// Allocates its point array. This is a query, not part of the frame path -- the gait never
        /// asks where a knee is, only gizmos and tests do -- so `buffer` is offered for callers who
        /// want to ask it every frame without garbage.
        public static Joints Resolve(in WalkerLimbSolver.Frame f, in WalkerLimbGeometry g,
                                     in WalkerLimbSolver.Result r, Vector3[] buffer = null)
        {
            int count = g.PitchCount;
            Vector3[] points = buffer != null && buffer.Length >= count + 1
                ? buffer
                : new Vector3[count + 1];

            Vector3 fwd = Quaternion.AngleAxis(r.Yaw, f.YawAxis) * f.RestFwd;
            Vector3 up = f.YawAxis;

            Vector2 walked = Vector2.zero;
            points[0] = f.Hip;
            for (int i = 0; i < count; i++)
            {
                walked += Segment(r.Pitch[i], g.Pitch[i].Length);
                points[i + 1] = InPlane(f.Hip, fwd, up, walked);
            }

            return new Joints { Points = points, Count = count + 1 };
        }

        private static Vector2 Segment(float angle, float length)
            => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;

        private static Vector3 InPlane(Vector3 hip, Vector3 fwd, Vector3 up, Vector2 p)
            => hip + fwd * p.x + up * p.y;
    }
}
