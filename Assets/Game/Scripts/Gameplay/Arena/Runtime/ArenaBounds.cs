using UnityEngine;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// The edge of the arena: a ring of invisible walls round this object's position.
    ///
    /// <para>
    /// Built in <c>Awake</c> on every machine rather than checked on the server alone, because a
    /// player's movement is decided by the machine that owns them — a wall only the server knew
    /// about would be walked straight through and then corrected. The server still checks
    /// <see cref="Contains"/> as a safety net for anything that gets past a wall (a blast, a fall
    /// through a seam); see <see cref="ArenaMatch"/>.
    /// </para>
    /// </summary>
    public class ArenaBounds : MonoBehaviour
    {
        [Tooltip("Distance from this object's position to the inside face of the walls.")]
        [SerializeField, Min(1f)] private float radius = 90f;

        [Tooltip("How many straight wall panels the ring is made of. More is rounder.")]
        [SerializeField, Range(8, 128)] private int segments = 48;

        [Tooltip("How far the walls reach above this object's position.")]
        [SerializeField, Min(1f)] private float height = 60f;

        [Tooltip("How far the walls reach below this object's position, for ground that dips at the edge.")]
        [SerializeField, Min(0f)] private float depth = 40f;

        [SerializeField, Min(0.1f)] private float thickness = 2f;

        [Tooltip("How far past the walls a player may be before the server puts them back.")]
        [SerializeField, Min(0f)] private float escapeMargin = 4f;

        public Vector3 Center => transform.position;

        public float Radius => radius;

        /// <summary>Whether a point is inside the walls, give or take the escape margin. Height is ignored.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 offset = point - Center;
            offset.y = 0f;
            float limit = radius + escapeMargin;
            return offset.sqrMagnitude <= limit * limit;
        }

        private void Awake() => BuildWalls();

        private void BuildWalls()
        {
            // Each panel is the chord of its slice of the circle, pushed out by half its thickness
            // so the INSIDE face sits on the radius, and widened a touch so neighbours overlap at
            // the corners instead of leaving a gap a body could squeeze through.
            float step = 2f * Mathf.PI / segments;
            float chord = 2f * radius * Mathf.Sin(step * 0.5f);
            float panelHeight = height + depth;
            float centreY = (height - depth) * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float angle = (i + 0.5f) * step;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                var panel = new GameObject($"Wall_{i:00}");
                panel.transform.SetParent(transform, false);
                panel.transform.localPosition = outward * (radius * Mathf.Cos(step * 0.5f) + thickness * 0.5f) +
                                                Vector3.up * centreY;
                panel.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

                var box = panel.AddComponent<BoxCollider>();
                box.size = new Vector3(chord + thickness, panelHeight, thickness);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.8f);
            const int lines = 64;
            for (int i = 0; i < lines; i++)
            {
                float a = 2f * Mathf.PI * i / lines;
                float b = 2f * Mathf.PI * (i + 1) / lines;
                Gizmos.DrawLine(Center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius,
                                Center + new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * radius);
            }
        }
    }
}
