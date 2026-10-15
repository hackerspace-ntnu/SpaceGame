// A body with a heavy load in its hands: tells the load where the hands are each frame and reaches the arms for its grips.
//
// On every machine, for every carrier, from replicated state only — the body is already everywhere (a player's transform is
// owner-authoritative and interpolated for everyone else) and WHICH load it carries is the load's own replicated state, so
// nothing is sent while it moves. On the carrier's own machine that makes the load exactly as smooth and as immediate as the
// body: it follows in LateUpdate from the body that frame's input moved, after the animator, with nothing between the two to
// lag or rubber-band. Who may take hold and where the load was let go are the load's (Liftable, CaptiveHaul); this is only
// the presentation.
//
// Any ILiftLoad. A prop lifted by one end (Liftable) is posed rigidly from the hands, carried out in front; a tied-up person
// (CaptiveHaul) is laid over the shoulder (Shoulder), the hands round its legs. The phases are the same for both.
//
// Three phases. Lifting: the arms reach for the grips while the take-hold clip squats the body, then the near end follows the
// animated hands up and settles at the carry point. Carrying: the near end is at a point fixed to the body (in front, at the
// height a fraction of the arm's length below the shoulders gives: about the waist). Lowering: the reverse, ending exactly on
// the rest pose the load was given.
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    // After the animator and the other late writers of the arms, so the hands end the frame on the grips. Before BoundBody's
    // 1000, which poses a shouldered captive on the root this places.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class LiftCarrier : MonoBehaviour
    {
        private enum Phase { Free, Lifting, Carrying, Lowering }

        [Tooltip("How far from the shoulders the hands carry the grips, as a fraction of the arm's length: 1 is a straight arm.")]
        [SerializeField, Range(0.5f, 1f)] private float armReach = 0.92f;

        [Tooltip("How far below the shoulders the hands carry the grips, as a fraction of the arm's length. Near 1 is the waist " +
                 "of a body whose arms hang almost straight, which is how a heavy end is carried.")]
        [SerializeField, Range(0f, 1f)] private float handsBelowShoulder = 0.8f;

        [Tooltip("How far the elbows bow outward, as a fraction of the arm's length.")]
        [SerializeField, Range(0f, 1f)] private float elbowsOut = 0.35f;

        private ILiftLoad load;
        private Phase phase;
        private float elapsed, duration;
        private Pose from, rest;
        private Vector3 heading;
        private Animator animator;
        private ReachingArm leftArm, rightArm;
        private BodyLanguage language;

        public ILiftLoad Load => load;

        /// <summary>The near end is up and the body may walk with it.</summary>
        public bool IsCarrying => Alive(load) && phase == Phase.Carrying;

        /// <summary>The carrier on <paramref name="body"/>, added if it has none.</summary>
        public static LiftCarrier On(GameObject body) =>
            body.TryGetComponent(out LiftCarrier carrier) ? carrier : body.AddComponent<LiftCarrier>();

        /// <summary>Whether this body has the two humanoid arms a lift needs.</summary>
        public bool CanReach => ResolveArms();

        /// <summary>
        /// Takes hold of <paramref name="target"/> over <paramref name="seconds"/> (the take-hold clip's length), starting
        /// <paramref name="alreadyDone"/> of the way in — 1 for a late joiner who finds it already carried.
        /// Idempotent for the load already held.
        /// </summary>
        public bool Lift(ILiftLoad target, float seconds, float alreadyDone = 0f)
        {
            if (!Alive(target) || !target.CanBeHeld || !ResolveArms()) return false;
            if (ReferenceEquals(load, target) && phase != Phase.Lowering) return true;

            Detach();
            load = target;
            load.SetHeldBy(this);

            from = new Pose(load.transform.position, load.transform.rotation);
            heading = LiftPoseSolver.Flat(load.Axis);
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
            if (!Alive(load)) return;

            rest = restPose;
            if (phase == Phase.Lowering) return;

            from = new Pose(load.transform.position, load.transform.rotation);
            duration = Mathf.Max(0.05f, seconds);
            elapsed = 0f;
            phase = Phase.Lowering;
        }

        /// <summary>The rest pose the load would take if it were put down this instant.</summary>
        public Pose RestFromHere() => Alive(load) ? load.RestFromHere() : default;

        /// <summary>Lets go at once, wherever the load is: it was taken away (snapped into its mount, despawned, untied).</summary>
        public void Drop() => Detach();

        private void OnDisable() => Drop();

        private void Forget()
        {
            load = null;
            phase = Phase.Free;
            if (language != null) language.OccupyArms(BodyArms.None);
        }

        private void Detach()
        {
            ILiftLoad let = load;
            Forget();
            if (Alive(let)) let.SetHeldBy(null);
        }

        /// <summary>A load is a component, and a destroyed one compares equal to null only through UnityEngine.Object.</summary>
        private static bool Alive(ILiftLoad load) => load is Object unity ? unity != null : load != null;

        private void LateUpdate() => Follow(Time.deltaTime);

        /// <summary>Hands the load this frame's grip and reaches the arms for it. Every frame from LateUpdate; public so a test can step it.</summary>
        public void Follow(float deltaTime)
        {
            if (!Alive(load))
            {
                // Destroyed under the hands (despawned into its mount mid-lowering): nothing left to hand back but the arms.
                if (!ReferenceEquals(load, null)) Forget();
                return;
            }

            elapsed += deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Read before the arms are turned: where the clip has put the hands this frame.
            Vector3 animatedHands = (leftArm.Palm + rightArm.Palm) * 0.5f;
            Vector3 carryPoint = CarryPoint();
            Vector3 facing = transform.forward;

            // Not before the hands close on it: the load lies still while the body bends down to it.
            if (phase != Phase.Lifting || t >= load.GripFraction)
                heading = LiftPoseSolver.Swing(heading, facing, load.SwingRate, deltaTime, load.MaxSwingLag);

            Vector3 restingGrip = load.GripOf(from);
            Vector3 grip;
            float reach;
            float toRest = 0f;
            float fromStart = 1f;
            float raised = 1f;

            switch (phase)
            {
                case Phase.Lifting:
                {
                    float grab = load.GripFraction;
                    reach = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(1e-3f, grab));
                    raised = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(grab, 1f, t));
                    Vector3 hands = Vector3.Lerp(animatedHands, carryPoint, raised);
                    grip = Vector3.Lerp(restingGrip, hands, raised);
                    fromStart = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(1e-3f, grab));
                    if (t >= 1f) phase = Phase.Carrying;
                    break;
                }
                case Phase.Lowering:
                {
                    Vector3 hands = Vector3.Lerp(carryPoint, animatedHands, Mathf.SmoothStep(0f, 1f, t / 0.3f));
                    raised = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.85f, t));
                    grip = Vector3.Lerp(hands, load.GripOf(rest), 1f - raised);
                    reach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t));
                    toRest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t));
                    break;
                }
                default:
                    grip = carryPoint;
                    reach = 1f;
                    break;
            }

            load.Hold(new LiftFrame(grip, heading, carryPoint, from, rest, fromStart, toRest, raised));

            load.Grips(transform, grip, out Vector3 gripLeft, out Vector3 gripRight);
            if (Vector3.Dot(gripRight - gripLeft, transform.right) < 0f) (gripLeft, gripRight) = (gripRight, gripLeft);
            Reach(leftArm, gripLeft, -transform.right, reach);
            Reach(rightArm, gripRight, transform.right, reach);

            if (phase == Phase.Lowering && t >= 1f) Detach();
        }

        private void Reach(ReachingArm arm, Vector3 target, Vector3 outward, float weight) =>
            arm.Reach(target, arm.Shoulder + outward * (arm.Length * elbowsOut) + Vector3.down * arm.Length, weight);

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

        /// <summary>Where the carrier's shoulder joint is this frame — the right one, or the left. Needs <see cref="CanReach"/>.</summary>
        public Vector3 Shoulder(bool right) => ResolveArms() ? (right ? rightArm : leftArm).Shoulder : transform.position;

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
