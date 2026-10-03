// Lifts a sitting body onto what it sits on. A sit loop is authored with the hips a little above the floor, so a
// resident sitting on a bench or a stool would sit in it; the fix is measured, not authored per clip: after the
// animator has posed the body, the hips' height is read and the model is moved up until the hips rest on the
// seat surface. Purely visual and local to each machine: the body, its collider and the NavMesh agent stay on the
// floor, nothing is sent, nothing is saved. A floor-level surface (a ground sit) leaves the loop's own pose alone.
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
        /// The lift that puts a body's hips on a seat. <paramref name="floorY"/> is where the feet are, <paramref name="hipsY"/>
        /// the hips as posed WITHOUT any lift; zero when the surface is within <paramref name="minLift"/> of the floor.
        /// </summary>
        public static float TargetLift(float surfaceY, float floorY, float hipsY, float scale, ResidentTuning tuning)
        {
            if (surfaceY - floorY < tuning.seatMinLift * scale) return 0f;

            float wanted = surfaceY + tuning.seatHipsAboveSurface * scale - hipsY;
            return Mathf.Clamp(wanted, 0f, tuning.seatMaxLift * scale);
        }

        /// <summary>Call after the animator has posed the body (LateUpdate). A null <paramref name="surfaceY"/> eases the body back down.</summary>
        public void Update(float? surfaceY, float deltaTime)
        {
            if (!Fits) return;

            ResidentTuning tuning = ResidentTuning.Instance;
            float target = 0f;
            if (surfaceY.HasValue)
            {
                float scale = model.lossyScale.y;
                target = TargetLift(surfaceY.Value, model.position.y - lift, hips.position.y - lift, scale, tuning);
            }

            lift = Mathf.SmoothDamp(lift, target, ref liftVelocity, tuning.seatBlendSeconds, float.PositiveInfinity, deltaTime);
            Transform parent = model.parent;
            Vector3 up = parent != null ? parent.InverseTransformVector(Vector3.up * lift) : Vector3.up * lift;
            model.localPosition = restLocalPosition + up;
        }
    }
}
