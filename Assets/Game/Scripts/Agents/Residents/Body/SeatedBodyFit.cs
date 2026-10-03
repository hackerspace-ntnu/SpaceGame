// Settles a sitting body onto its seat. A sit loop is authored with the hips a little above the floor, and a Seat's sit
// point is higher, so the model is moved up until the hips rest on the sit point: measured after the animator has posed
// the body, not authored per clip. Purely visual and local to each machine: the body root, its collider and the NavMesh
// agent stay where the seat put them, nothing is sent, nothing is saved.
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
        /// The lift that puts a body's hips on a sit point. <paramref name="hipsY"/> is the hips as posed WITHOUT any lift;
        /// never negative, because the pose a body is blending out of (standing) has its hips higher than any seat.
        /// </summary>
        public static float TargetLift(float sitY, float hipsY, float scale, ResidentTuning tuning) =>
            Mathf.Max(0f, sitY + tuning.seatHipsAboveSurface * scale - hipsY);

        /// <summary>Call after the animator has posed the body (LateUpdate). A null <paramref name="sitY"/> eases the body back down.</summary>
        public void Update(float? sitY, float deltaTime)
        {
            if (!Fits) return;

            ResidentTuning tuning = ResidentTuning.Instance;
            float target = sitY.HasValue ? TargetLift(sitY.Value, hips.position.y - lift, model.lossyScale.y, tuning) : 0f;

            lift = Mathf.SmoothDamp(lift, target, ref liftVelocity, tuning.seatBlendSeconds, float.PositiveInfinity, deltaTime);
            Transform parent = model.parent;
            Vector3 up = parent != null ? parent.InverseTransformVector(Vector3.up * lift) : Vector3.up * lift;
            model.localPosition = restLocalPosition + up;
        }
    }
}
