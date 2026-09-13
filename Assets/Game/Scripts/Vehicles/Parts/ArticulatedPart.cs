// One moving piece of a vehicle: a door that swings, a hatch that lowers, a wing that tilts.
// Animates its own transform between the pose it was authored at ("closed") and an open pose
// expressed as a rotation about — or a slide along — a local axis.
//
// The component rotates around its OWN origin, so put it on a pivot GameObject placed at the
// hinge line with the mesh parented underneath. PlayerShip is built that way (see
// Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs).
//
// Deliberately knows nothing about who moves it. Player interaction lives in
// ArticulatedPartInteraction; mount-driven deployment lives in VehicleDeploymentController.
using System;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public class ArticulatedPart : MonoBehaviour
    {
        public enum MotionType
        {
            Rotate,
            Slide
        }

        [Header("Motion")]
        [SerializeField] private MotionType motion = MotionType.Rotate;

        [Tooltip("Axis in this transform's own local space. Rotate: the hinge axis. Slide: the travel direction.")]
        [SerializeField] private Vector3 axis = Vector3.right;

        [Tooltip("Degrees travelled when fully open. Sign picks which way it swings.")]
        [SerializeField] private float openAngle = 90f;

        [Tooltip("Metres travelled when fully open. Sign picks which way it slides.")]
        [SerializeField] private float openDistance = 1f;

        [Header("Timing")]
        [SerializeField] private float openDuration = 1.5f;
        [SerializeField] private float closeDuration = 1.5f;
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Initial State")]
        [Tooltip("Start fully open. The authored transform is still treated as the closed pose.")]
        [SerializeField] private bool startOpen;

        // The authored pose, captured once. Everything is driven relative to it so the part can
        // never accumulate drift from repeated open/close cycles.
        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private bool baselineCaptured;

        private float openness;     // 0 = closed, 1 = open
        private float target;       // where openness is heading

        /// <summary>Fires whenever the part finishes moving. True when it settled open.</summary>
        public event Action<bool> Settled;

        public bool IsOpen => target > 0.5f;
        public bool IsMoving => !Mathf.Approximately(openness, target);

        /// <summary>0 = fully closed, 1 = fully open. Useful for driving audio/VFX.</summary>
        public float Openness => openness;

        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);
        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open, bool instant = false)
        {
            EnsureBaseline();
            float wanted = open ? 1f : 0f;

            // Already on its way there: leave the swing alone.
            //
            // This matters because the networked state answer is a BROADCAST, not a reply to the
            // one client that asked — the messaging layer has no unicast. So every machine hears
            // "this door is open", including the machines already watching it open. Without this
            // guard that restarts the animation from wherever it had got to, and a door somebody
            // is walking through visibly stutters every time anyone joins.
            //
            // An instant request is still honoured: skipping the animation is the whole point of
            // it, and a part mid-swing has to be able to be snapped to its target.
            if (!instant && Mathf.Approximately(target, wanted)) return;

            target = wanted;

            if (instant)
            {
                openness = target;
                ApplyPose();
                Settled?.Invoke(IsOpen);
            }
        }

        private void Awake()
        {
            EnsureBaseline();
            if (startOpen)
                SetOpen(true, instant: true);
            else
                ApplyPose();
        }

        private void Update()
        {
            if (!IsMoving)
                return;

            float duration = Mathf.Max(0.01f, target > openness ? openDuration : closeDuration);
            openness = Mathf.MoveTowards(openness, target, Time.deltaTime / duration);
            ApplyPose();

            if (!IsMoving)
                Settled?.Invoke(IsOpen);
        }

        // Callers can drive the part from their own Awake, which may run before ours.
        private void EnsureBaseline()
        {
            if (baselineCaptured)
                return;
            closedLocalPosition = transform.localPosition;
            closedLocalRotation = transform.localRotation;
            baselineCaptured = true;
        }

        private void ApplyPose()
        {
            Vector3 dir = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            float t = ease != null && ease.length > 0 ? ease.Evaluate(openness) : openness;

            if (motion == MotionType.Rotate)
            {
                transform.localRotation = closedLocalRotation * Quaternion.AngleAxis(openAngle * t, dir);
                transform.localPosition = closedLocalPosition;
            }
            else
            {
                transform.localRotation = closedLocalRotation;
                transform.localPosition = closedLocalPosition + closedLocalRotation * (dir * (openDistance * t));
            }
        }

        private void OnValidate()
        {
            openDuration = Mathf.Max(0.01f, openDuration);
            closeDuration = Mathf.Max(0.01f, closeDuration);
            if (axis.sqrMagnitude < 1e-6f)
                axis = Vector3.right;
        }

        // ─────────── Editor helpers ───────────
        // Let whoever tunes the hinge see the result without entering play mode. Both preview
        // actions write the transform directly; the authored pose is whatever "Preview Closed"
        // leaves behind, so always finish on Closed before saving the prefab.
        [ContextMenu("Preview Open")]
        private void PreviewOpen()
        {
            EnsureBaseline();
            openness = target = 1f;
            ApplyPose();
        }

        [ContextMenu("Preview Closed")]
        private void PreviewClosed()
        {
            EnsureBaseline();
            openness = target = 0f;
            ApplyPose();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 dir = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            Vector3 world = transform.TransformDirection(dir);
            Gizmos.color = motion == MotionType.Rotate ? Color.cyan : Color.yellow;
            Gizmos.DrawLine(transform.position - world, transform.position + world);
            Gizmos.DrawSphere(transform.position, 0.06f);
        }
    }
}
