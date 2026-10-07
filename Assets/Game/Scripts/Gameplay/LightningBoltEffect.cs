// A lightning bolt drawn as geometry, spanning exactly the distance it is asked to span.
//
// This exists because Lightning.vfx does not. That asset is a VFX Graph whose bolt length is
// baked into the graph and exposes no parameter for it -- fine at the 10 m the player's spell
// was authored against, useless the moment anything wants a different drop. Raising the
// conjurer's draw height to 100 m left a bolt hanging in the sky with nothing under it, and
// there is no way to fix that from outside the graph.
//
// So the length is the whole point of this one. Strike(from, to) lays the ribbon between two
// world points and pushes the real world distance into _BeamLength, which is what keeps the
// crackle the same size on a 3 m arc and a 300 m one.
//
// ---- the split, which is the same one LaserStaffArtifact makes -------------------
//
// SHAPE is geometry, DISCHARGE is the shader. A fragment shader can darken a pixel but it
// cannot move the ribbon, so a bolt faked in UV is a painted squiggle on a straight strip and
// reads as one. The kinks below are real vertex positions; SpaceGame/LightningBeam does the
// filament, the segment breaks and the strobe on top of them.
//
// Two displacements, doing two different jobs:
//   QUANTISED  re-rolled restrikeRate times a second, from a hash rather than Perlin so it
//              SNAPS to an unrelated shape. This is what makes it lightning.
//   SMOOTH     Perlin, drifting continuously, so the whole channel sways.
// Quantised alone strobes in place like a failing fluorescent tube; smooth alone is a rope in
// the wind. Both are faded out at the ends by a sine envelope so the bolt stays pinned to the
// two points it was given -- a strike whose ends wander is a strike that visibly misses the
// thing it is billing for damage.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(LineRenderer))]
    public class LightningBoltEffect : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;

        [Header("Shape")]
        [Tooltip("Points along the bolt. More is smoother and costs more; the kink size is set " +
                 "by spread, not by this.")]
        [SerializeField] private int segments = 32;

        [Tooltip("Sideways wander as a fraction of the bolt's LENGTH, so a long drop bends more " +
                 "than a short one instead of staying a needle.")]
        [SerializeField] private float spread = 0.035f;

        [Tooltip("Ceiling on that wander in metres. Without it a 100 m bolt would swing five " +
                 "metres either side and stop reading as a single channel.")]
        [SerializeField] private float maxOffset = 3.5f;

        [Tooltip("How many times a second the kinks are re-rolled.")]
        [SerializeField] private float restrikeRate = 22f;

        [Header("Life")]
        [Tooltip("Seconds the bolt is visible. It destroys itself afterwards - nothing else " +
                 "cleans it up, and this spawns on a loop.")]
        [SerializeField] private float duration = 0.45f;

        [Tooltip("Fraction of the duration spent at full brightness before it starts fading. " +
                 "The fade is driven into the shader's _Ignite.")]
        [SerializeField] [Range(0f, 1f)] private float holdFraction = 0.35f;

        [Tooltip("Width at the top of the bolt and at the ground. Wider at the ground reads as " +
                 "the discharge spreading into what it hit.")]
        [SerializeField] private float startWidth = 0.6f;
        [SerializeField] private float endWidth = 1.4f;

        [Header("Standalone fallback")]
        [Tooltip("If nothing calls Strike() - the prefab is dropped in a scene by hand, say - " +
                 "the bolt falls this far straight down from its own transform.")]
        [SerializeField] private float fallbackDrop = 100f;

        private static readonly int IgniteId = Shader.PropertyToID("_Ignite");
        private static readonly int BeamLengthId = Shader.PropertyToID("_BeamLength");

        private MaterialPropertyBlock _props;
        private Vector3[] _points;
        private Vector3 _from, _to;
        private float _seed;
        private float _elapsed;
        private bool _struck;

        private void Awake()
        {
            if (!line) line = GetComponent<LineRenderer>();

            // World space, always. A bolt is pinned to two world points, and a LineRenderer in
            // local space would drag the whole channel along if anything ever parented this.
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;

            _props = new MaterialPropertyBlock();
            _seed = Random.value * 1000f;
        }

        private void Start()
        {
            // Nobody called Strike between Instantiate and here, so this is a hand-placed prefab
            // or a caller that does not know about this component. Fall straight down.
            if (!_struck) Strike(transform.position, transform.position + Vector3.down * fallbackDrop);
        }

        /// <summary>
        /// Draw a bolt between two world points. Safe to call immediately after Instantiate --
        /// it does its own initialisation, because Awake has not necessarily run yet when the
        /// spawner wants to aim it.
        /// </summary>
        public void Strike(Vector3 from, Vector3 to)
        {
            if (!line) line = GetComponent<LineRenderer>();
            if (_props == null)
            {
                _props = new MaterialPropertyBlock();
                _seed = Random.value * 1000f;
                line.useWorldSpace = true;
            }

            _from = from;
            _to = to;
            _struck = true;
            _elapsed = 0f;

            line.widthCurve = AnimationCurve.Linear(0f, Mathf.Max(0.01f, startWidth),
                                                    1f, Mathf.Max(0.01f, endWidth));

            // The reason this component exists. The shader scrolls its crackle in METRES, so it
            // needs the true world span or a 100 m bolt crawls while a 3 m one seethes.
            line.GetPropertyBlock(_props);
            _props.SetFloat(BeamLengthId, Vector3.Distance(from, to));
            _props.SetFloat(IgniteId, 1f);
            line.SetPropertyBlock(_props);

            Build();
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (duration > 0f && _elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            Build();

            float t = duration > 0f ? _elapsed / duration : 0f;
            float hold = Mathf.Clamp01(holdFraction);
            // Full brightness through the hold, then a linear collapse. Driven into _Ignite
            // rather than into the line's colour so the shader's own falloff does the fading --
            // the filament outlives the glow, which is what a real discharge does.
            float ignite = t <= hold ? 1f : 1f - Mathf.InverseLerp(hold, 1f, t);

            line.GetPropertyBlock(_props);
            _props.SetFloat(IgniteId, ignite);
            _props.SetFloat(BeamLengthId, Vector3.Distance(_from, _to));
            line.SetPropertyBlock(_props);
        }

        private void Build()
        {
            int segs = Mathf.Max(1, segments);
            int count = segs + 1;

            if (_points == null || _points.Length != count) _points = new Vector3[count];

            Vector3 span = _to - _from;
            float length = span.magnitude;

            if (length < 1e-4f)
            {
                line.positionCount = 2;
                line.SetPosition(0, _from);
                line.SetPosition(1, _to);
                return;
            }

            Vector3 forward = span / length;

            // Any two axes across the bolt will do -- the noise has no preferred direction -- but
            // they must be perpendicular to it, or the sideways wander would shorten and lengthen
            // the bolt instead of bending it. The near-vertical guard matters here specifically:
            // this thing falls straight down, so crossing with up would give a zero vector.
            Vector3 right = Vector3.Cross(
                forward, Mathf.Abs(forward.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(forward, right);

            float amplitude = Mathf.Min(length * Mathf.Max(0f, spread), Mathf.Max(0f, maxOffset));
            float phase = Mathf.Floor(Time.time * Mathf.Max(1f, restrikeRate));

            _points[0] = _from;
            _points[count - 1] = _to;

            for (int i = 1; i < count - 1; i++)
            {
                float t = (float)i / segs;
                float envelope = Mathf.Sin(t * Mathf.PI);

                float kinkR = Jitter(i * 1.37f + _seed, phase);
                float kinkU = Jitter(i * 2.71f + _seed + 19.3f, phase + 7.1f);

                float swayR = Mathf.PerlinNoise(_seed + t * 2.2f, Time.time * 1.7f) * 2f - 1f;
                float swayU = Mathf.PerlinNoise(_seed + 51.7f + t * 2.2f, Time.time * 1.4f) * 2f - 1f;

                Vector3 offset = right * (kinkR * 0.75f + swayR * 0.5f)
                               + up * (kinkU * 0.75f + swayU * 0.5f);

                _points[i] = _from + span * t + offset * (amplitude * envelope);
            }

            line.positionCount = count;
            line.SetPositions(_points);
        }

        /// A hash rather than Perlin, and that is the whole point: it is DISCONTINUOUS. Fed a
        /// quantised time it snaps to an unrelated shape instead of easing into a neighbouring
        /// one, which is the difference between lightning and a wobble.
        private static float Jitter(float a, float b)
        {
            float h = Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f;
            return (h - Mathf.Floor(h)) * 2f - 1f;
        }

        private void OnValidate()
        {
            segments = Mathf.Max(1, segments);
            spread = Mathf.Max(0f, spread);
            maxOffset = Mathf.Max(0f, maxOffset);
            restrikeRate = Mathf.Max(1f, restrikeRate);
            duration = Mathf.Max(0f, duration);
            startWidth = Mathf.Max(0.01f, startWidth);
            endWidth = Mathf.Max(0.01f, endWidth);
        }
    }
}
