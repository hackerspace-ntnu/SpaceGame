// An arm that reaches for a point: the shoulder, the elbow and the hand, solved as two bones of fixed length.
//
// A hold pose is baked data, so it can only put the hands where one rig's proportions put them: the old Push pose raised a
// Raxy's hands above its head, and a cart held there floats. Reaching for the real handle puts the hand on it whatever the
// rig's size, which is why a pusher reaches instead of posing.
using UnityEngine;

namespace SpaceGame.Presentation
{
    public static class ArmReach
    {
        // A fully straight or fully folded arm has no elbow to place; keep a hair inside both limits.
        private const float LimitMargin = 1e-3f;

        /// <summary>
        /// Where the elbow and the hand go. The hand lands on <paramref name="target"/> when the arm is long enough; otherwise it
        /// stops short on the line to it, the arm straight. The elbow bends toward <paramref name="pole"/>.
        /// </summary>
        public static void Solve(Vector3 shoulder, float upper, float lower, Vector3 target, Vector3 pole, out Vector3 elbow, out Vector3 hand)
        {
            Vector3 toTarget = target - shoulder;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 1e-5f ? toTarget / distance : Vector3.forward;

            float reach = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + LimitMargin, upper + lower - LimitMargin);
            hand = shoulder + direction * reach;

            float along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));

            Vector3 bend = Vector3.ProjectOnPlane(pole - shoulder, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);

            elbow = shoulder + direction * along + bend.normalized * height;
        }
    }

    /// <summary>One arm's three bones, turned each frame so the hand reaches a point. Plain class: the owner calls it from its LateUpdate.</summary>
    public sealed class ReachingArm
    {
        // The hand bone is at the wrist; a fist closes about a hand's length further on. Turning the arm turns the hand, so the
        // wrist goal is taken from where the palm will be, and the second pass corrects for the first one's turn.
        private const int Passes = 2;

        private readonly Transform upper;
        private readonly Transform lower;
        private readonly Transform hand;
        private readonly Vector3 palm;

        /// <param name="palm">Where the fist closes, in the hand bone's own space (the grip frame's origin); zero reaches with the wrist.</param>
        public ReachingArm(Transform upper, Transform lower, Transform hand, Vector3 palm = default)
        {
            this.upper = upper;
            this.lower = lower;
            this.hand = hand;
            this.palm = palm;
        }

        public Vector3 Shoulder => upper.position;

        /// <summary>Straight-line length of the arm as the rig is posed now: shoulder to elbow plus elbow to hand.</summary>
        public float Length => Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);

        /// <summary>
        /// Brings the palm toward <paramref name="target"/>, <paramref name="weight"/> of the way from where the animation has it
        /// (0 leaves the animation alone, 1 puts the palm on the point). Call after the animator has posed the body.
        /// </summary>
        public void Reach(Vector3 target, Vector3 pole, float weight)
        {
            if (weight <= 0f) return;

            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            Vector3 from = hand.position;

            for (int pass = 0; pass < Passes; pass++)
            {
                Vector3 goal = Vector3.Lerp(from, target - hand.TransformVector(palm), Mathf.Clamp01(weight));
                ArmReach.Solve(shoulder, upperLength, lowerLength, goal, pole, out Vector3 elbow, out Vector3 reached);

                upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, reached - lower.position) * lower.rotation;
            }
        }
    }
}
