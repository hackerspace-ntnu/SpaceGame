// One mirror of the sun shrine: a disc on a pedestal that turns about the vertical and, on the
// mirror that catches the sun, tilts its face up. Presentation and geometry only — the shrine
// decides where each mirror stands and tells every machine; this turns that into motion and says
// which way the face points while it moves.
using System.Collections;
using SpaceGame.Audio;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class SunMirror : MonoBehaviour
    {
        [Tooltip("Turns about the world vertical. Its authored pose is turn step 0.")]
        [SerializeField] private Transform yawPivot;

        [Tooltip("Child of the yaw pivot; lifts the face. Its authored pose is tilt step 0 (upright).")]
        [SerializeField] private Transform tiltPivot;

        [Tooltip("The reflecting disc. Its position is the disc's centre and its forward is the " +
                 "reflecting face in the authored pose. Both pivots must pass through the centre.")]
        [SerializeField] private Transform pane;

        [Tooltip("Spun a full turn each time the turn crank is worked.")]
        [SerializeField] private Transform yawCrank;

        [Tooltip("Spun a full turn each time the tilt crank is worked. Empty on a mirror that does not tilt.")]
        [SerializeField] private Transform tiltCrank;

        [Tooltip("Stops in a full turn about the vertical.")]
        [SerializeField, Min(1)] private int yawSteps = 12;

        [Tooltip("Tilt positions, from upright (0) upward. 1 for a mirror that does not tilt.")]
        [SerializeField, Min(1)] private int tiltSteps = 1;

        [Tooltip("Degrees the face lifts per tilt step. The reflected beam moves twice this.")]
        [SerializeField] private float tiltStepDegrees = 5f;

        [Tooltip("Radius of the reflecting disc, metres. The beam is caught anywhere on it.")]
        [SerializeField, Min(0.05f)] private float radius = 0.7f;

        [Tooltip("Seconds the mirror takes to move one step.")]
        [SerializeField, Min(0.05f)] private float stepSeconds = 0.5f;

        [SerializeField] private SfxId crankSound = SfxId.InteractLever;

        private Quaternion yawRest;
        private Quaternion tiltRest;
        private Quaternion yawCrankRest;
        private Quaternion tiltCrankRest;
        private Vector3 restCentre;
        private Vector3 restFace;
        private Vector3 restTiltAxis;
        private bool rested;

        private float shownYaw;
        private float shownTilt;
        private Coroutine move;

        public int YawSteps => yawSteps;
        public int TiltSteps => tiltSteps;
        public float Radius => radius;

        public Vector3 Centre
        {
            get { CaptureRest(); return restCentre; }
        }

        /// <summary>Which way the face points right now, mid-swing included, so the beam sweeps with it.</summary>
        public Vector3 Normal => NormalAt(shownYaw, shownTilt);

        /// <summary>Which way the face points at a given pose, in steps. Pure: tests ask it of a prefab.</summary>
        public Vector3 NormalAt(float yawStep, float tiltStep)
        {
            CaptureRest();
            return Quaternion.AngleAxis(YawDegrees(yawStep), Vector3.up) *
                   Quaternion.AngleAxis(-TiltDegrees(tiltStep), restTiltAxis) * restFace;
        }

        private void Awake() => CaptureRest();

        // Lazy as well as in Awake: an EditMode test reads a prefab that never wakes.
        private void CaptureRest()
        {
            if (rested) return;
            rested = true;
            yawRest = yawPivot.rotation;
            tiltRest = tiltPivot.rotation;
            restCentre = pane.position;
            restFace = pane.forward;
            restTiltAxis = pane.right;
            if (yawCrank != null) yawCrankRest = yawCrank.localRotation;
            if (tiltCrank != null) tiltCrankRest = tiltCrank.localRotation;
        }

        /// <summary>
        /// Show the mirror at (<paramref name="yaw"/>, <paramref name="tilt"/>). Moves there the
        /// short way round, or lands there silently when <paramref name="instant"/>.
        /// <paramref name="crankedAxis"/> is 0 for the turn crank, 1 for the tilt crank, or −1
        /// when no crank of this mirror was worked.
        /// </summary>
        public void Show(int yaw, int tilt, bool instant, int crankedAxis)
        {
            CaptureRest();
            if (move != null) StopCoroutine(move);
            move = null;

            float yawFrom = Mathf.Repeat(shownYaw, yawSteps);
            float yawTo = yawFrom + Mathf.DeltaAngle(yawFrom * 360f / yawSteps, yaw * 360f / yawSteps) * yawSteps / 360f;

            if (instant || !isActiveAndEnabled)
            {
                shownYaw = yaw;
                shownTilt = tilt;
                Pose(0f, crankedAxis);
                return;
            }

            Transform crank = crankedAxis == 0 ? yawCrank : crankedAxis == 1 ? tiltCrank : null;
            if (crank != null) Sfx.Play(crankSound, crank.position, GetInstanceID());
            move = StartCoroutine(Move(yawFrom, yawTo, shownTilt, tilt, crankedAxis));
        }

        private IEnumerator Move(float yawFrom, float yawTo, float tiltFrom, float tiltTo, int crankedAxis)
        {
            float steps = Mathf.Max(1f, Mathf.Max(Mathf.Abs(yawTo - yawFrom), Mathf.Abs(tiltTo - tiltFrom)));
            float duration = stepSeconds * steps;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                shownYaw = Mathf.Lerp(yawFrom, yawTo, k);
                shownTilt = Mathf.Lerp(tiltFrom, tiltTo, k);
                Pose(360f * k, crankedAxis);
                yield return null;
            }

            shownYaw = Mathf.Repeat(yawTo, yawSteps);
            shownTilt = tiltTo;
            Pose(0f, crankedAxis);
            move = null;
        }

        private void Pose(float crankDegrees, int crankedAxis)
        {
            // World rotations, parent first, so the pose is the same formula NormalAt uses however
            // the FBX nodes under the pivots carry their axis conversion.
            Quaternion yawTurn = Quaternion.AngleAxis(YawDegrees(shownYaw), Vector3.up);
            yawPivot.rotation = yawTurn * yawRest;
            tiltPivot.rotation = yawTurn * Quaternion.AngleAxis(-TiltDegrees(shownTilt), restTiltAxis) * tiltRest;

            Spin(yawCrank, yawCrankRest, crankedAxis == 0 ? crankDegrees : 0f);
            Spin(tiltCrank, tiltCrankRest, crankedAxis == 1 ? crankDegrees : 0f);
        }

        private void Spin(Transform crank, Quaternion rest, float degrees)
        {
            if (crank == null) return;
            // The axle runs out from the pedestal's vertical axis to the handle, resolved in the
            // crank's parent frame for the same FBX-axis reason as the pivots above.
            Vector3 outward = Vector3.ProjectOnPlane(crank.position - yawPivot.position, Vector3.up);
            if (outward.sqrMagnitude < 1e-6f) outward = crank.forward;
            Vector3 axle = crank.parent.InverseTransformDirection(outward.normalized);
            crank.localRotation = Quaternion.AngleAxis(degrees, axle) * rest;
        }

        private float YawDegrees(float step) => step * 360f / yawSteps;

        private float TiltDegrees(float step) => step * tiltStepDegrees;
    }
}
