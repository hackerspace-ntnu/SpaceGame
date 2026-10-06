// A body with one end of a heavy load in its hands: poses the load from the body each frame and reaches the arms for its grips.
//
// On every machine, for every carrier, from replicated state only — the body is already everywhere (a player's transform is
// owner-authoritative and interpolated for everyone else) and WHICH load it carries is the load's own replicated state, so the
// load's pose is derived and never sent while it moves. On the carrier's own machine that makes the load exactly as smooth and
// as immediate as the body: it is posed in LateUpdate from the body that frame's input moved, after the animator, with nothing
// between the two to lag or rubber-band. Who may lift and where the load was put down are the load's (Liftable); this is only
// the presentation.
//
// Three phases. Lifting: the arms reach for the grips while the lift clip squats the body, then the near end follows the
// animated hands up and settles at the carry point. Carrying: the near end is at a point fixed to the body (in front, at the
// height a fraction of the arm's length below the shoulders gives: about the waist), the far end on the ground. Lowering: the
// reverse, ending exactly on the rest pose the load was given.
using System;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    // After the animator and the other late writers of the arms, so the hands end the frame on the grips.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class LiftCarrier : MonoBehaviour
    {
        private enum Phase { Free, Lifting, Carrying, Lowering }

        [Tooltip("How far from the shoulders the hands carry the grips, as a fraction of the arm's length: 1 is a straight arm.")]
        [SerializeField, Range(0.5f, 1f)] private float armReach = 0.98f;

        [Tooltip("How far below the shoulders the hands carry the grips, as a fraction of the arm's length. Near 1 is the waist " +
                 "of a body whose arms hang almost straight, which is how a heavy end is carried.")]
        [SerializeField, Range(0f, 1f)] private float handsBelowShoulder = 0.8f;

        [Tooltip("How far the elbows bow outward, as a fraction of the arm's length.")]
        [SerializeField, Range(0f, 1f)] private float elbowsOut = 0.35f;

        // Where above the hands the ground probes start: under a cabin roof, over a ramp lip.
        private const float ProbeAboveHands = 1.5f;

        private Liftable load;
        private LiftShape shape;
        private Phase phase;
        private float elapsed, duration;
        private Pose from, rest;
        private Vector3 heading;
        private Animator animator;
        private ReachingArm leftArm, rightArm;
        private BodyLanguage language;
        private Collider[] bodyColliders = Array.Empty<Collider>();
        private float probeFrom, lastGround;
        private Func<float, float, float> groundAt;

        public Liftable Load => load;

        /// <summary>The near end is up and the body may walk with it.</summary>
        public bool IsCarrying => load != null && phase == Phase.Carrying;

        /// <summary>The carrier on <paramref name="body"/>, added if it has none.</summary>
        public static LiftCarrier On(GameObject body) =>
            body.TryGetComponent(out LiftCarrier carrier) ? carrier : body.AddComponent<LiftCarrier>();

        /// <summary>Whether this body has the two humanoid arms a lift needs.</summary>
        public bool CanReach => ResolveArms();

        /// <summary>
        /// Lifts <paramref name="target"/> over <paramref name="seconds"/> (the lift clip's length), starting
        /// <paramref name="alreadyDone"/> of the way in — 1 for a late joiner who finds it already carried.
        /// Idempotent for the load already held.
        /// </summary>
        public bool Lift(Liftable target, float seconds, float alreadyDone = 0f)
        {
            if (target == null || !target.Shape.IsValid || !ResolveArms()) return false;
            if (load == target && phase != Phase.Lowering) return true;

            Detach();
            load = target;
            shape = target.Shape;
            bodyColliders = GetComponentsInChildren<Collider>(false);
            load.SetSeenByGround(bodyColliders, false);
            load.SetCarried(true);

            from = new Pose(load.transform.position, load.transform.rotation);
            heading = LiftPoseSolver.Flat(load.transform.rotation * shape.Axis);
            lastGround = load.transform.position.y;
            groundAt ??= GroundAt;
            if (language == null) language = GetComponentInChildren<BodyLanguage>(true);
            if (language != null) language.OccupyArms(BodyArms.Both);

            duration = Mathf.Max(0.05f, seconds);
            elapsed = Mathf.Clamp01(alreadyDone) * duration;
            phase = elapsed >= duration ? Phase.Carrying : Phase.Lifting;
            return true;
        }

        /// <summary>Lowers the near end to <paramref name="restPose"/> over <paramref name="seconds"/> and lets go there.</summary>
        public void Lower(Pose restPose, float seconds)
        {
            if (load == null) return;

            rest = restPose;
            if (phase == Phase.Lowering) return;

            from = new Pose(load.transform.position, load.transform.rotation);
            duration = Mathf.Max(0.05f, seconds);
            elapsed = 0f;
            phase = Phase.Lowering;
        }

        /// <summary>The rest pose the load would take if it were put down this instant.</summary>
        public Pose RestFromHere() => load != null
            ? LiftPoseSolver.Rest(shape, new Pose(load.transform.position, load.transform.rotation), groundAt)
            : default;

        /// <summary>Lets go at once, wherever the load is: it was taken away (snapped into its mount, despawned).</summary>
        public void Drop()
        {
            Liftable let = Detach();
            if (let != null) let.Place(new Pose(let.transform.position, let.transform.rotation));
        }

        private void OnDisable() => Drop();

        private Liftable Detach()
        {
            Liftable let = load;
            if (let == null) return null;

            load = null;
            phase = Phase.Free;
            let.SetSeenByGround(bodyColliders, true);
            let.SetCarried(false);
            bodyColliders = Array.Empty<Collider>();
            if (language != null) language.OccupyArms(BodyArms.None);
            return let;
        }

        private void LateUpdate() => Follow(Time.deltaTime);

        /// <summary>Poses the load on the body and reaches the arms for its grips. Every frame from LateUpdate; public so a test can step it.</summary>
        public void Follow(float deltaTime)
        {
            if (load == null) return;

            elapsed += deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Read before the arms are turned: where the clip has put the hands this frame.
            Vector3 animatedHands = (leftArm.Palm + rightArm.Palm) * 0.5f;
            Vector3 carryPoint = CarryPoint();
            probeFrom = Mathf.Max(carryPoint.y, load.transform.position.y) + ProbeAboveHands;
            // Not before the hands close on it: the load lies still while the body bends down to it.
            if (phase != Phase.Lifting || t >= load.GripFraction)
                heading = LiftPoseSolver.Swing(heading, transform.forward, load.SwingRate, deltaTime);

            Pose restingGrip = new Pose(from.position + from.rotation * shape.Grip, from.rotation);
            Vector3 grip;
            float reach;
            float toRest = 0f;
            float fromStart = 1f;

            switch (phase)
            {
                case Phase.Lifting:
                {
                    float grab = load.GripFraction;
                    reach = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(1e-3f, grab));
                    Vector3 hands = Vector3.Lerp(animatedHands, carryPoint, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(grab, 1f, t)));
                    grip = Vector3.Lerp(restingGrip.position, hands, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(grab, 1f, t)));
                    fromStart = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(1e-3f, grab));
                    if (t >= 1f) phase = Phase.Carrying;
                    break;
                }
                case Phase.Lowering:
                {
                    Vector3 hands = Vector3.Lerp(carryPoint, animatedHands, Mathf.SmoothStep(0f, 1f, t / 0.3f));
                    Vector3 restGrip = rest.position + rest.rotation * shape.Grip;
                    grip = Vector3.Lerp(hands, restGrip, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.85f, t)));
                    reach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t));
                    toRest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t));
                    break;
                }
                default:
                    grip = carryPoint;
                    reach = 1f;
                    break;
            }

            Pose pose = LiftPoseSolver.Solve(shape, grip, heading, groundAt);
            if (fromStart < 1f) pose = new Pose(Vector3.Lerp(from.position, pose.position, fromStart), Quaternion.Slerp(from.rotation, pose.rotation, fromStart));
            if (toRest > 0f) pose = new Pose(Vector3.Lerp(pose.position, rest.position, toRest), Quaternion.Slerp(pose.rotation, rest.rotation, toRest));
            load.Place(pose);
            lastGround = pose.position.y;

            Transform gripLeft = load.GripLeft, gripRight = load.GripRight;
            if (Vector3.Dot(gripRight.position - gripLeft.position, transform.right) < 0f) (gripLeft, gripRight) = (gripRight, gripLeft);
            Reach(leftArm, gripLeft.position, -transform.right, reach);
            Reach(rightArm, gripRight.position, transform.right, reach);

            if (phase == Phase.Lowering && t >= 1f)
            {
                Liftable let = Detach();
                let.Place(rest);
            }
        }

        private void Reach(ReachingArm arm, Vector3 target, Vector3 outward, float weight) =>
            arm.Reach(target, arm.Shoulder + outward * (arm.Length * elbowsOut) + Vector3.down * arm.Length, weight);

        private float GroundAt(float x, float z) => load.GroundY(x, z, probeFrom, lastGround);

        /// <summary>
        /// Where the grips are carried: <see cref="handsBelowShoulder"/> of the arm's length below the shoulders' middle, and as
        /// far ahead of it as puts the hands at <see cref="armReach"/>. From the body, never from the animated hands, so the
        /// load stays put while the walk swings the arms.
        /// </summary>
        public Vector3 CarryPoint()
        {
            Vector3 shoulders = (leftArm.Shoulder + rightArm.Shoulder) * 0.5f;
            float arm = (leftArm.Length + rightArm.Length) * 0.5f;
            float below = handsBelowShoulder * arm;
            float ahead = Mathf.Sqrt(Mathf.Max(0f, armReach * armReach * arm * arm - below * below));

            Vector3 flatForward = LiftPoseSolver.Flat(transform.forward);
            Vector3 ownShoulders = new Vector3(transform.position.x, shoulders.y, transform.position.z);
            float shoulderAhead = Vector3.Dot(shoulders - ownShoulders, flatForward);
            return ownShoulders + flatForward * (shoulderAhead + ahead) + Vector3.down * below;
        }

        private bool ResolveArms()
        {
            if (leftArm != null) return true;

            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            leftArm = ReachingArm.Of(animator, false);
            rightArm = ReachingArm.Of(animator, true);
            if (leftArm != null && rightArm != null) return true;

            leftArm = rightArm = null;
            return false;
        }
    }
}
