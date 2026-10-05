// Draws a hull's interior only for the machine whose camera is in it — or at an opening, looking in.
//
// A walkable vehicle ships as one prefab with both halves: the exterior shell everybody sees and the
// fit-out inside it. From outside the fit-out is invisible anyway, so this switches its renderers off
// and the hull costs what an exterior-only model would. Colliders are never touched: collision is the
// same on every machine, only what this machine DRAWS changes.
//
// Presentation only, like SandstormShelter's point query it is modelled on: each machine asks where
// its own camera is, polls a handful of local-space boxes on a timer, and nothing goes on the wire.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public class InteriorReveal : MonoBehaviour
    {
        [Tooltip("Renderers of the fit-out: drawn only while this machine's camera is in or near it.")]
        [SerializeField] private Renderer[] interiorRenderers;

        [Tooltip("The walkable interior, as boxes in this transform's local space.")]
        [SerializeField] private Bounds[] localVolumes;

        [Tooltip("Hatches, doors and ports: standing within Reveal Radius of one shows the interior, " +
                 "so a player looking in through it sees what is there.")]
        [SerializeField] private Transform[] openings;

        [SerializeField, Min(0f)] private float revealRadius = 4f;

        [Tooltip("Seconds between checks. The camera crosses a threshold at walking pace.")]
        [SerializeField, Min(0.02f)] private float pollInterval = 0.2f;

        private readonly List<Vector3> openingsLocal = new List<Vector3>();
        private bool shown = true;
        private float nextPoll;

        public bool Shown => shown;

        private void OnEnable()
        {
            nextPoll = 0f;
            Apply(false);
        }

        private void Update()
        {
            if (Time.time < nextPoll) return;
            nextPoll = Time.time + pollInterval;

            Camera cam = Camera.main;
            if (cam == null) return;
            openingsLocal.Clear();
            foreach (Transform t in openings)
            {
                if (t != null) openingsLocal.Add(transform.InverseTransformPoint(t.position));
            }
            float localRadius = revealRadius / Mathf.Max(transform.lossyScale.x, 1e-4f);
            Apply(ShouldReveal(transform.InverseTransformPoint(cam.transform.position), localVolumes, openingsLocal, localRadius));
        }

        /// <summary>
        /// True when `point` is inside any of `volumes`, or strictly closer than `radius` to any of
        /// `openings`. Everything in one space.
        /// </summary>
        public static bool ShouldReveal(Vector3 point, IReadOnlyList<Bounds> volumes, IReadOnlyList<Vector3> openings, float radius)
        {
            if (volumes != null)
            {
                for (int i = 0; i < volumes.Count; i++)
                {
                    if (volumes[i].Contains(point)) return true;
                }
            }
            if (openings != null)
            {
                float r2 = radius * radius;
                for (int i = 0; i < openings.Count; i++)
                {
                    if ((openings[i] - point).sqrMagnitude < r2) return true;
                }
            }
            return false;
        }

        private void Apply(bool show)
        {
            if (show == shown) return;
            shown = show;
            foreach (Renderer r in interiorRenderers)
            {
                if (r != null) r.enabled = show;
            }
        }

        /// <summary>Wiring for the builder, which owns this prefab.</summary>
        public void Configure(Renderer[] renderers, Bounds[] volumes, Transform[] openingMarks)
        {
            interiorRenderers = renderers;
            localVolumes = volumes;
            openings = openingMarks;
        }
    }
}
