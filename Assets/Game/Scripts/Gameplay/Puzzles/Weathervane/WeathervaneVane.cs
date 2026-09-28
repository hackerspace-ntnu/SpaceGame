// One vane of a weathervane ring: the head that swings, the crank that spins, the lamp that says
// "this one is right". Presentation only — the ring decides, and tells every machine where each
// vane stands; this turns that into motion.
using System.Collections;
using SpaceGame.Audio;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class WeathervaneVane : MonoBehaviour
    {
        [Tooltip("Swings about the world vertical. Its authored pose is position 0 — pointing up the plateau.")]
        [SerializeField] private Transform head;

        [Tooltip("Spins a full turn about the vane's forward axis each time its own crank is worked.")]
        [SerializeField] private Transform crank;

        [Tooltip("Lit while this vane points the right way.")]
        [SerializeField] private GameObject alignedLamp;

        [Tooltip("Seconds a head takes to swing one quarter.")]
        [SerializeField] private float quarterTurnSeconds = 0.7f;

        [SerializeField] private SfxId crankSound = SfxId.InteractLever;

        private Quaternion headRest;
        private Quaternion crankRest;
        private float shownQuarters;    // may run past 4: a swing always goes clockwise
        private float crankDegrees;
        private Coroutine swing;
        private bool rested;

        private void Awake() => CaptureRest();

        private void CaptureRest()
        {
            if (rested) return;
            rested = true;
            headRest = head.localRotation;
            crankRest = crank.localRotation;
        }

        /// <summary>
        /// Show the vane at <paramref name="quarter"/>. Swings clockwise the short way round, or
        /// lands there silently when <paramref name="instant"/> — the state was true before this
        /// machine was looking. <paramref name="cranked"/> spins this vane's own crank and plays its
        /// clunk; a vane that was only dragged round by its neighbour's crank does neither.
        /// </summary>
        public void Show(int quarter, bool instant, bool cranked)
        {
            CaptureRest();
            if (swing != null) StopCoroutine(swing);
            swing = null;

            float current = Mathf.Repeat(shownQuarters, WeathervanePositions.Quarters);
            float delta = Mathf.Repeat(quarter - current, WeathervanePositions.Quarters);

            if (instant || !isActiveAndEnabled)
            {
                shownQuarters = quarter;
                crankDegrees = 0f;
                Pose();
                alignedLamp.SetActive(quarter == 0);
                return;
            }

            if (cranked) Sfx.Play(crankSound, crank.position, GetInstanceID());
            swing = StartCoroutine(Swing(current, current + delta, cranked, quarter == 0));
        }

        private IEnumerator Swing(float from, float to, bool cranked, bool aligned)
        {
            // Off for the swing whichever way it is heading, so the lamp only ever comes on once the
            // arrow has actually arrived.
            alignedLamp.SetActive(false);

            float duration = Mathf.Max(0.05f, quarterTurnSeconds * Mathf.Max(1f, to - from));
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                shownQuarters = Mathf.Lerp(from, to, k);
                if (cranked) crankDegrees = 360f * k;
                Pose();
                yield return null;
            }

            shownQuarters = to;
            crankDegrees = 0f;
            Pose();
            alignedLamp.SetActive(aligned);
            swing = null;
        }

        private void Pose()
        {
            // Axes resolved in each part's parent frame, not assumed: the model arrives through an
            // FBX whose nodes carry the axis conversion, so "local up" on the node is not world up.
            Vector3 up = head.parent.InverseTransformDirection(Vector3.up).normalized;
            head.localRotation = Quaternion.AngleAxis(90f * shownQuarters, up) * headRest;

            Vector3 axle = crank.parent.InverseTransformDirection(transform.forward).normalized;
            crank.localRotation = Quaternion.AngleAxis(crankDegrees, axle) * crankRest;
        }
    }
}
