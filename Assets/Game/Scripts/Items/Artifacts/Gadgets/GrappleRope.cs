using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The cable, and only the cable.
    ///
    /// <para>
    /// Split out of <see cref="GrapplingHookArtifact"/> because none of it is physics: every method
    /// here reads a start, an end and a length and writes points into a LineRenderer. It runs on
    /// every machine, including the peers who never simulate the swing, which is the whole reason
    /// the rope is worth drawing well — a peer sees this and nothing else.
    /// </para>
    /// <para>
    /// A plain serializable class rather than a MonoBehaviour, so it tunes in the Inspector under
    /// its own foldout without adding a component to wire up or a GameObject to find.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class GrappleRope
    {
        [Tooltip("Points in the line. The old rope used 12, which is too few for a wave to read " +
                 "as a curve rather than a zigzag.")]
        [SerializeField] private int segments = 28;

        [Tooltip("Extra vertices at each joint. Zero mitres every bend into a hard corner, which " +
                 "is fine on a straight line and wrong on a waving one.")]
        [SerializeField, Range(0, 6)] private int cornerVertices = 2;

        [Tooltip("Extra vertices rounding each end of the cable.")]
        [SerializeField, Range(0, 6)] private int capVertices = 2;

        [Header("Flight — the whip")]
        [Tooltip("Metres the cable swings off-axis while it is being paid out.")]
        [SerializeField] private float whipAmplitude = 0.55f;
        [SerializeField] private float whipWaves = 1.6f;
        [SerializeField] private float whipSpeed = 9f;

        [Header("Bite — the tension crack")]
        [Tooltip("Metres of shock travelling back down the rope at the instant the dart lands.")]
        [SerializeField] private float snapAmplitude = 0.4f;
        [SerializeField] private float snapWaves = 4f;
        [SerializeField] private float snapSpeed = 34f;
        [SerializeField] private float snapDuration = 0.45f;

        [Header("Hanging — the sag")]
        [Tooltip("Metres of droop per metre of slack. At zero slack the cable is dead straight, " +
                 "which is what makes a taut rope read as taut.")]
        [SerializeField] private float slackSag = 0.6f;
        [SerializeField] private float maxSag = 2.5f;

        private LineRenderer line;
        private float snapUntil;

        /// <summary>Whether there is a renderer to draw into at all.</summary>
        public bool IsBound => line != null;

        /// <summary>Hand over the renderer. Safe with null — the hook simply draws no rope.</summary>
        public void Bind(LineRenderer renderer) => line = renderer;

        public void Show()
        {
            if (line == null) return;

            line.positionCount = Mathf.Max(2, segments);

            // Set here rather than on the asset, because the LineRenderer prefab this reads is
            // shared with the lasso and only this instance is being changed. Without rounded joints
            // every wave below is drawn as a run of hard mitred angles — which is fine for the
            // straight two-point line the asset was authored as, and wrong for a curve.
            line.numCornerVertices = cornerVertices;
            line.numCapVertices = capVertices;

            line.enabled = true;
        }

        public void Hide()
        {
            if (line == null) return;
            line.enabled = false;
            snapUntil = 0f;
        }

        /// <summary>
        /// The dart is in the air. <paramref name="progress"/> is 0 at the muzzle, 1 at the anchor.
        /// </summary>
        public void DrawFlight(Vector3 start, Vector3 target, float progress)
        {
            // The whip eases off as the line runs out: a rope with 3 m still to travel is being
            // dragged straight by the head, one with 30 m to go is still cracking.
            float amp = whipAmplitude * (1f - progress * 0.55f);

            Draw(start, Vector3.Lerp(start, target, progress), amp, whipWaves, whipSpeed, sag: 0f);
        }

        /// <summary>The moment the dart lands. Starts the shock that runs back down the cable.</summary>
        public void Bite() => snapUntil = Time.time + snapDuration;

        /// <summary>
        /// The dart is set and the player is on the rope.
        ///
        /// <paramref name="ropeLength"/> is the constraint length, not the gap — the difference
        /// between the two IS the slack, and it is the only thing the sag is allowed to come from.
        /// The rope the hook shipped with sagged by a constant scaled to the span, so a rope under
        /// full tension hung as limply as one with ten metres of slack in it.
        /// </summary>
        public void DrawTether(Vector3 start, Vector3 anchor, float ropeLength)
        {
            float slack = Mathf.Max(0f, ropeLength - Vector3.Distance(start, anchor));
            float sag = Mathf.Min(maxSag, slack * slackSag);

            float amp = 0f, waves = 0f, speed = 0f;
            if (Time.time < snapUntil)
            {
                // Squared rather than linear, so this is a crack that is over rather than a wobble
                // that fades — the ear and the eye both expect a rope going taut to be sudden.
                float k = (snapUntil - Time.time) / Mathf.Max(snapDuration, 0.0001f);
                amp = snapAmplitude * k * k;
                waves = snapWaves;
                speed = snapSpeed;
            }

            Draw(start, anchor, amp, waves, speed, sag);
        }

        /// <summary>
        /// Lay <see cref="segments"/> points from start to end, displaced by a travelling wave and
        /// a droop.
        /// </summary>
        private void Draw(Vector3 start, Vector3 end, float amp, float waves, float speed, float sag)
        {
            if (line == null || !line.enabled) return;

            int count = Mathf.Max(2, segments);
            if (line.positionCount != count) line.positionCount = count;

            Vector3 axis = end - start;
            float span = axis.magnitude;

            // Degenerate: the muzzle is on top of the anchor. Two points and nothing to displace.
            if (span < 0.01f)
            {
                for (int i = 0; i < count; i++) line.SetPosition(i, start);
                return;
            }

            Vector3 dir = axis / span;

            // A perpendicular basis to displace within. Up is the sensible reference for a rope
            // that mostly goes upward; the fallback covers one aimed straight up or straight down,
            // where that cross product collapses to zero and every point would land on the axis.
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;
            Vector3 up = Vector3.Cross(right, dir);

            // Wave count scales with length, or a 50 m cable would carry the same single hump a
            // 5 m one does and read as a rubber band rather than a rope.
            float cycles = waves * Mathf.Clamp(span / 10f, 1f, 4f);

            // Likewise the amplitude: a 2 m rope thrashing half a metre looks like a bug.
            float reach = amp * Mathf.Clamp01(span / 8f);

            float phase = Time.time * speed;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 p = start + axis * t;

                // Pinned at both ends. The muzzle is in a hand and the tip is in a dart, so an
                // envelope that does not vanish at t=0 and t=1 detaches the cable from both.
                float envelope = Mathf.Sin(t * Mathf.PI);

                if (reach > 0.0001f)
                {
                    float a = reach * envelope;

                    // Two axes, deliberately incommensurate in both frequency and speed, so the
                    // whip turns over in the air. One axis alone is a flat ripple, and a flat
                    // ripple is invisible from the half of all angles that view it edge-on.
                    p += right * (Mathf.Sin(t * Mathf.PI * 2f * cycles - phase) * a);
                    p += up * (Mathf.Sin(t * Mathf.PI * 2f * cycles * 0.63f - phase * 1.31f) * a * 0.7f);
                }

                if (sag > 0.0001f) p.y -= envelope * sag;

                line.SetPosition(i, p);
            }
        }
    }
}
