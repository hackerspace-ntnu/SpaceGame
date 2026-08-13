// Recovering a joint's hinge axis from the model.
//
// The artist modelled a real pin at every joint (LEG3_HipPin, LEG3_KneePin, LEG3_AnklePin) plus
// a gear disc at the hip and knee. A pin is a cylinder, so its longest local axis IS the hinge
// axis — measuring the mesh is more trustworthy than guessing from the bone hierarchy, and it
// means re-exporting the model with the legs splayed differently keeps working with no code
// change.
//
// A hinge is a LINE, not a direction: the axle and its negation describe the same joint. Sign is
// therefore normalised once per leg so the whole linkage shares one convention, which is what
// lets the plane basis and the joint angles agree on which way is positive.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerAxle
    {
        /// Direction of a box's longest axis, expressed in the frame `meshToTarget` maps into.
        /// The pure core of the measurement — the mesh's own scale and orientation are carried by
        /// the matrix, so a pin authored along any local axis resolves correctly.
        public static Vector3 LongestExtent(Matrix4x4 meshToTarget, Bounds meshBounds)
        {
            Vector3 ex = meshToTarget.MultiplyVector(new Vector3(meshBounds.extents.x, 0f, 0f));
            Vector3 ey = meshToTarget.MultiplyVector(new Vector3(0f, meshBounds.extents.y, 0f));
            Vector3 ez = meshToTarget.MultiplyVector(new Vector3(0f, 0f, meshBounds.extents.z));

            Vector3 longest = ex;
            if (ey.sqrMagnitude > longest.sqrMagnitude) longest = ey;
            if (ez.sqrMagnitude > longest.sqrMagnitude) longest = ez;
            return longest.sqrMagnitude > 1e-12f ? longest.normalized : Vector3.right;
        }

        /// Fallback when no pin mesh is present: for a planar linkage the rest pose itself gives
        /// the axle away, as the normal of the plane the two segments span.
        public static Vector3 FromRestPose(Vector3 upperSegment, Vector3 lowerSegment, Vector3 fallback)
        {
            Vector3 normal = Vector3.Cross(upperSegment, lowerSegment);
            if (normal.sqrMagnitude > 1e-8f) return normal.normalized;

            // Segments colinear — a straight leg spans no plane. Any axis perpendicular to it is
            // a legal hinge, so take the one perpendicular to the fallback hint.
            normal = Vector3.Cross(upperSegment, fallback);
            return normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.right;
        }

        /// Both senses of a hinge line count as parallel.
        public static bool AreParallel(Vector3 a, Vector3 b, float tolerance = 0.99f)
            => Mathf.Abs(Vector3.Dot(a.normalized, b.normalized)) >= tolerance;

        /// Force `axle` into the same sense as `reference`, so a leg's three joints agree.
        public static Vector3 MatchSense(Vector3 axle, Vector3 reference)
            => Vector3.Dot(axle, reference) < 0f ? -axle : axle;

        /// The leg's in-plane horizontal axis. Built as cross(up, axle) so that
        /// (fwd, up, axle) is right-handed — which makes a positive rotation about the axle
        /// increase the plane angle measured by atan2(up, fwd). The solver depends on that.
        public static Vector3 PlaneForward(Vector3 axle, Vector3 up)
        {
            Vector3 fwd = Vector3.Cross(up, axle);
            return fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
        }
    }
}
