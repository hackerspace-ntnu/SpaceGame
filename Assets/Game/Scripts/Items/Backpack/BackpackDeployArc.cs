using UnityEngine;

namespace SpaceGame.Items
{
    // The path the pack travels between the player's back socket and the ground.
    //
    // A pure function of (start, end, t) rather than a coroutine, so the same curve plays
    // forwards on deploy and backwards on re-shoulder, can be sampled by the EditMode tests,
    // and could later be driven from a replicated t without holding any state of its own.
    public static class BackpackDeployArc
    {
        /// Pose along the swing-down path. Uses UnityEngine.Pose (position + rotation).
        /// t is clamped to [0,1].
        ///   arcHeight — metres the pack rises above the straight line at midpoint,
        ///               so it swings over the shoulder rather than through the chest.
        ///   outward   — metres the pack bows away from the straight line horizontally,
        ///               along the horizontal component of (end - start) rotated 90 deg,
        ///               so it clears the player's own body.
        public static Pose Evaluate(Pose start, Pose end, float t, float arcHeight, float outward)
        {
            t = Mathf.Clamp01(t);

            // The endpoints are returned untouched rather than evaluated. The pack is parented to
            // the back socket at t == 0 and to the world at t == 1, and a Bezier's floating point
            // residue there would show as a visible jump on the frame the parenting hands over.
            if (t <= 0f) return start;
            if (t >= 1f) return end;

            // Smoothstep the rotation only — the position already gets its ease from the arc.
            float eased = t * t * (3f - 2f * t);
            Quaternion rotation = Quaternion.Slerp(start.rotation, end.rotation, eased);

            Vector3 travel = end.position - start.position;

            // Nowhere to go: bowing an arc over a zero-length path would shove the pack out
            // sideways and back again for no reason.
            if (travel.sqrMagnitude <= 1e-10f) return new Pose(start.position, rotation);

            var run = new Vector3(travel.x, 0f, travel.z);

            // The horizontal run turned 90 degrees about world up: (x,z) -> (z,-x). A straight
            // drop has no run and therefore no defined "away from the body"; world right is an
            // arbitrary but stable stand-in, and the controller always deploys forwards anyway.
            Vector3 side = run.sqrMagnitude > 1e-8f
                ? new Vector3(run.z, 0f, -run.x).normalized
                : Vector3.right;

            Vector3 control = (start.position + end.position) * 0.5f
                              + Vector3.up * arcHeight
                              + side * outward;

            float u = 1f - t;
            Vector3 position = u * u * start.position + 2f * u * t * control + t * t * end.position;

            // A quadratic Bezier stays inside the convex hull of its three points, so a control
            // point on or above the lower end already keeps the curve out of the ground. The floor
            // is here for the case that does not cover — a negative arcHeight, which would sag the
            // pack through a downhill slope on its way to the drop point.
            position.y = Mathf.Max(position.y, Mathf.Min(start.position.y, end.position.y));

            return new Pose(position, rotation);
        }
    }
}
