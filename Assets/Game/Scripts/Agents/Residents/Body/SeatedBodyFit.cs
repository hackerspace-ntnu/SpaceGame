// Settles a sitting body onto its seat. A sit loop is authored with the hips a little above the floor, and a Seat's sit
// point is higher, so the model is moved up until the hips rest on the sit point: measured after the animator has posed
// the body, not authored per clip. The chair sit of a Stool seat is the other way round: its hips are higher than a pot
// or a stool, so the model is let a little down until the sitter rests on the seat with its feet on the floor.
// Purely visual and local to each machine: the body root, its collider and the NavMesh agent stay where the seat put
// them, nothing is sent, nothing is saved.
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class SeatedBodyFit
    {
        private readonly Transform model;
        private readonly Transform hips;
        private readonly Vector3 restLocalPosition;
        private float lift, liftVelocity;

        /// <summary>The model is the animator's own object; a body whose animator IS the root cannot be lifted without lifting its collider, so it is not fitted.</summary>
        public SeatedBodyFit(Animator animator, Transform root)
        {
            if (animator == null || !animator.isHuman || animator.transform == root) return;

            model = animator.transform;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            restLocalPosition = model.localPosition;
        }

        public bool Fits => model != null && hips != null;

        /// <summary>
        /// The lift that puts a body's hips <paramref name="hipsAboveSurface"/> (at scale 1) above a sit point. <paramref name="hipsY"/>
        /// is the hips as posed WITHOUT any lift. The body is let down by at most <paramref name="maxDrop"/> (at scale 1): the pose
        /// a body is blending out of (standing) has its hips higher than any seat, and an unlimited drop would sink it through the
        /// floor for the length of the blend. 0 never lowers it.
        /// </summary>
        public static float TargetLift(float sitY, float hipsY, float scale, float hipsAboveSurface, float maxDrop) =>
            Mathf.Max(-maxDrop * scale, sitY + hipsAboveSurface * scale - hipsY);

        /// <summary>Call after the animator has posed the body (LateUpdate). A null <paramref name="seat"/> eases the body back to rest.</summary>
        public void Update(Seat seat, float deltaTime)
        {
            if (!Fits) return;

            ResidentTuning tuning = ResidentTuning.Instance;
            float target = 0f;
            // A sleeper lies with its root on the mattress: nothing to lift or lower.
            if (seat != null && seat.Pose != SeatPose.Lie)
            {
                bool stool = seat.Pose == SeatPose.Stool;
                target = TargetLift(seat.SitPosition.y, hips.position.y - lift, model.lossyScale.y,
                                    stool ? tuning.stoolHipsAboveSurface : tuning.seatHipsAboveSurface, stool ? tuning.stoolMaxDrop : 0f);
            }

            lift = Mathf.SmoothDamp(lift, target, ref liftVelocity, tuning.seatBlendSeconds, float.PositiveInfinity, deltaTime);
            Transform parent = model.parent;
            Vector3 up = parent != null ? parent.InverseTransformVector(Vector3.up * lift) : Vector3.up * lift;
            model.localPosition = restLocalPosition + up;
        }
    }
}
