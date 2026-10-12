using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The current running down the harpoon's wire: a crackling arc laid along the cable, with
    /// bright pulses travelling from the gun to the harpoon, while <see cref="Live"/> is set.
    ///
    /// <para>
    /// It follows the cable's own points (the <see cref="wire"/> line, which <see cref="GrappleRope"/>
    /// writes and <see cref="WireTube"/> skins), so the discharge hangs and whips with the wire
    /// rather than cutting a straight chord across its sag. Drawn with the lightning shader, which
    /// scrolls its crackle in metres — the length is pushed into <c>_BeamLength</c> every frame.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WireCurrent : MonoBehaviour
    {
        private static readonly int BeamLengthId = Shader.PropertyToID("_BeamLength");
        private static readonly int IgniteId = Shader.PropertyToID("_Ignite");

        [Tooltip("The cable's point list. Assigned by the builder.")]
        [SerializeField] private LineRenderer wire;
        [Tooltip("Width of the arc along the wire, world metres.")]
        [SerializeField] private float width = 0.016f;
        [Tooltip("Extra width at the centre of a travelling pulse.")]
        [SerializeField] private float pulseWidth = 0.06f;
        [Tooltip("Pulses on the wire at once.")]
        [SerializeField, Range(1, 6)] private int pulses = 3;
        [Tooltip("Seconds a pulse takes from the gun to the harpoon.")]
        [SerializeField] private float pulseSeconds = 0.3f;
        [Tooltip("Length of a pulse, as a share of the wire.")]
        [SerializeField, Range(0.01f, 0.5f)] private float pulseLength = 0.07f;
        [Tooltip("Metres the arc strays from the cable.")]
        [SerializeField] private float jitter = 0.025f;
        [Tooltip("How many times a second the arc's kinks are re-rolled.")]
        [SerializeField] private float restrikeRate = 30f;

        /// <summary>Set by the harpoon while the body on the other end is being shocked.</summary>
        public bool Live { get; set; }

        private LineRenderer line;
        private MaterialPropertyBlock props;
        private readonly AnimationCurve widths = new AnimationCurve();
        private Vector3[] points = new Vector3[0];
        private Vector3[] kinks = new Vector3[0];
        private Keyframe[] keys = new Keyframe[0];
        private float nextStrike;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.enabled = false;
            props = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            bool on = Live && wire != null && wire.enabled && wire.positionCount > 1;
            line.enabled = on;
            if (!on) return;

            int count = wire.positionCount;
            if (points.Length != count)
            {
                points = new Vector3[count];
                kinks = new Vector3[count];
                keys = new Keyframe[count];
                nextStrike = 0f;
            }
            wire.GetPositions(points);

            if (Time.time >= nextStrike)
            {
                nextStrike = Time.time + 1f / Mathf.Max(restrikeRate, 1f);
                // Pinned at both ends: the gun and the harpoon are where the current enters and leaves.
                for (int i = 0; i < count; i++)
                    kinks[i] = Random.insideUnitSphere * (jitter * Mathf.Sin(i / (float)(count - 1) * Mathf.PI));
            }

            float length = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) length += Vector3.Distance(points[i - 1], points[i]);
                float t = i / (float)(count - 1);
                keys[i] = new Keyframe(t, width + pulseWidth * Pulse(t));
                points[i] += kinks[i];
            }

            line.positionCount = count;
            line.SetPositions(points);
            widths.keys = keys;
            line.widthCurve = widths;
            line.widthMultiplier = 1f;

            line.GetPropertyBlock(props);
            props.SetFloat(BeamLengthId, length);
            props.SetFloat(IgniteId, Random.Range(0.75f, 1f));
            line.SetPropertyBlock(props);
        }

        /// <summary>0..1: how much of a travelling pulse is at <paramref name="t"/> along the wire.</summary>
        private float Pulse(float t)
        {
            float best = 0f;
            for (int k = 0; k < pulses; k++)
            {
                float centre = Mathf.Repeat(Time.time / Mathf.Max(pulseSeconds, 0.01f) + k / (float)pulses, 1f);
                float d = Mathf.Abs(t - centre) / Mathf.Max(pulseLength, 0.001f);
                best = Mathf.Max(best, Mathf.Clamp01(1f - d));
            }
            return best * best;
        }
    }
}
