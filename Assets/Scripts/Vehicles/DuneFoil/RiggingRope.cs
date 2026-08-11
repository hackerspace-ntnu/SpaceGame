using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// Draws one rope between two anchors, with sag.
    ///
    /// The source model's rigging was ten static curves shaped for one particular sail pose, so
    /// none of it could follow a sail that moves. Ropes are drawn here instead: a sheet whose
    /// far end is the sail's clew tracks the sail automatically, and a stay between two fixed
    /// points looks exactly as static as it should.
    ///
    /// The sag is what makes it read as rope. A straight line between two points looks like a
    /// steel rod; slack rope hangs, and rope hauled tight straightens out — which also gives
    /// the player visible feedback on what the winch is doing.
    /// </summary>
    // Draws in edit mode too, so the rigging is visible while the prefab is being authored
    // rather than collapsing to a dot at the origin until someone presses play.
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public class RiggingRope : MonoBehaviour
    {
        [Header("Ends")]
        [SerializeField] private Transform anchorA;
        [SerializeField] private Transform anchorB;

        [Header("Shape")]
        [Tooltip("Points along the rope. 8 is plenty for a catenary at this scale.")]
        [SerializeField, Range(2, 32)] private int segments = 10;

        [Tooltip("Deepest the rope hangs below the straight line, in metres, at full slack.")]
        [SerializeField, Min(0f)] private float maxSag = 1.2f;

        [Tooltip("Rope diameter, metres.")]
        [SerializeField, Min(0.01f)] private float thickness = 0.09f;

        [Header("Slack")]
        [Tooltip("Slack 0..1. Driven by whatever pays the rope out; hand-set for standing " +
                 "rigging, which is always taut.")]
        [SerializeField, Range(0f, 1f)] private float slack;

        [Tooltip("Sail whose sheet this is. When set, slack follows how far it is eased, so " +
                 "the rope answers the winch without anything having to wire them together.")]
        [SerializeField] private SailSurface sheetedSail;

        private LineRenderer line;
        private Vector3[] points;

        /// <summary>Slack, 0..1. 0 is bar-taut.</summary>
        public float Slack { get => slack; set => slack = Mathf.Clamp01(value); }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            Rebuild();
        }

        private void OnValidate()
        {
            if (line == null) line = GetComponent<LineRenderer>();
            Rebuild();
        }

        private void Rebuild()
        {
            if (line == null) return;
            segments = Mathf.Max(2, segments);
            points = new Vector3[segments];
            line.useWorldSpace = true;
            line.positionCount = segments;
            line.startWidth = thickness;
            line.endWidth = thickness;
            line.numCapVertices = 2;
        }

        private void LateUpdate()
        {
            if (anchorA == null || anchorB == null || line == null) return;
            if (points == null || points.Length != segments) Rebuild();

            if (sheetedSail != null) slack = sheetedSail.SheetOut;

            Vector3 a = anchorA.position;
            Vector3 b = anchorB.position;
            float sag = maxSag * slack;

            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                // 4t(1-t) is a parabola pinned at both ends and deepest in the middle — close
                // enough to a catenary at rope lengths this short, for none of the cost.
                p.y -= sag * 4f * t * (1f - t);
                points[i] = p;
            }

            line.SetPositions(points);
        }

        /// <summary>Wire the rope up. Used by the prefab builder.</summary>
        public void Bind(Transform a, Transform b, SailSurface sail, float sagMetres)
        {
            anchorA = a;
            anchorB = b;
            sheetedSail = sail;
            maxSag = sagMetres;
        }
    }
}
