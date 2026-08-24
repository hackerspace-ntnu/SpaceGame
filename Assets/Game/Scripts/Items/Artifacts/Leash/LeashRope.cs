using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The cord, and only the cord.
    ///
    /// <para>
    /// Split out of <see cref="Leash"/> for the same reason <c>GrappleRope</c> is split out of the
    /// grappling hook: none of it is physics. It reads two knots and a length and writes points into
    /// a LineRenderer. It runs on every machine, including the ones that never resolve the
    /// constraint — a peer watching somebody else drag a crate sees this and nothing else, so it is
    /// worth drawing well.
    /// </para>
    /// <para>
    /// A plain serializable class rather than a MonoBehaviour, so it tunes under its own Inspector
    /// foldout on the artifact without a component to wire or a GameObject to find.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LeashRope
    {
        [Tooltip("Points in the line. Below about 20 a hanging curve reads as a zigzag.")]
        [SerializeField] private int segments = 32;

        [Tooltip("Extra vertices at each joint. Zero mitres every bend into a hard corner, which " +
                 "is what the old rope did at all eighteen of them.")]
        [SerializeField, Range(0, 6)] private int cornerVertices = 3;

        [Tooltip("Extra vertices rounding each end of the cord.")]
        [SerializeField, Range(0, 6)] private int capVertices = 3;

        [Header("Look")]
        [SerializeField] private Material material;
        [SerializeField] private Color color = new(0.62f, 0.51f, 0.34f);

        [Tooltip("Thickness of the slack rope, in metres.")]
        [SerializeField] private float width = 0.05f;

        [Tooltip("How much thinner the rope is drawn at full tension. A rope about to break " +
                 "should look like one.")]
        [SerializeField, Range(0f, 0.6f)] private float tensionThinning = 0.3f;

        [Tooltip("Braid repeats per metre. Set per metre rather than per rope so the strands stay " +
                 "the same size on a 2 m rope and a 20 m one.")]
        [SerializeField] private float braidsPerMetre = 6f;

        [Header("Hang")]
        [Tooltip("Ceiling on sag depth in metres, for a very long rope between two close points.")]
        [SerializeField] private float maxSag = 3f;

        [Header("Ground")]
        [Tooltip("Lay the rope on top of the world instead of letting it sag through it.")]
        [SerializeField] private bool restOnGround = true;

        [Tooltip("What counts as ground. Loose bodies and the rope's own two ends are always " +
                 "ignored regardless of this — see LeashGround.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the surface the rope's centre line sits, in metres. About its own " +
                 "radius, so it looks like it is lying on the ground rather than in it.")]
        [SerializeField] private float groundClearance = 0.03f;

        [Tooltip("How far above the straight line between the knots to start looking. This is what " +
                 "lets the rope climb a rise between its two ends; a whole hill taller than this " +
                 "will still be cut through.")]
        [SerializeField] private float groundProbeAbove = 1.5f;

        [Tooltip("Metres of idle drift in a rope hanging with slack in it. Zero is a dead line.")]
        [SerializeField] private float swayAmplitude = 0.06f;
        [SerializeField] private float swaySpeed = 0.9f;

        [Header("Tension")]
        [Tooltip("Metres of shiver in a rope under full tension.")]
        [SerializeField] private float shiverAmplitude = 0.035f;
        [SerializeField] private float shiverSpeed = 26f;

        [Header("Bite — the crack when it goes tight")]
        [SerializeField] private float biteAmplitude = 0.35f;
        [SerializeField] private float biteWaves = 4f;
        [SerializeField] private float biteSpeed = 30f;
        [SerializeField] private float biteDuration = 0.4f;

        private LineRenderer line;
        private float biteUntil;

        /// <summary>
        /// The downward probe, built on first use because <see cref="groundMask"/> is not known
        /// until this object has been deserialized or copied.
        /// </summary>
        private LeashGround ground;

        /// <summary>Build the renderer this rope draws into, on the rope's own GameObject.</summary>
        public void Build(GameObject host)
        {
            line = host.AddComponent<LineRenderer>();

            if (material != null) line.material = material;
            line.startColor = color;
            line.endColor = color;
            line.useWorldSpace = true;
            line.numCornerVertices = cornerVertices;
            line.numCapVertices = capVertices;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.positionCount = Mathf.Max(2, segments);

            // Off for casting — a camera-facing billboard casts a shadow that swivels with the
            // viewer — but on for receiving, so a rope lying in a building's shade is not the one
            // bright thing in it. The braid normal map does the rest of the shaping.
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = true;
        }

        /// <summary>Copy tuning from another rope. Used to build a saved rope like an authored one.</summary>
        public void CopyFrom(LeashRope other)
        {
            if (other == null) return;

            segments = other.segments;
            cornerVertices = other.cornerVertices;
            capVertices = other.capVertices;
            material = other.material;
            color = other.color;
            width = other.width;
            tensionThinning = other.tensionThinning;
            braidsPerMetre = other.braidsPerMetre;
            maxSag = other.maxSag;
            restOnGround = other.restOnGround;
            groundMask = other.groundMask;
            groundClearance = other.groundClearance;
            groundProbeAbove = other.groundProbeAbove;
            swayAmplitude = other.swayAmplitude;
            swaySpeed = other.swaySpeed;
            shiverAmplitude = other.shiverAmplitude;
            shiverSpeed = other.shiverSpeed;
            biteAmplitude = other.biteAmplitude;
            biteWaves = other.biteWaves;
            biteSpeed = other.biteSpeed;
            biteDuration = other.biteDuration;
        }

        /// <summary>The rope has just gone tight, or has just been tied. Start the crack.</summary>
        public void Bite() => biteUntil = Time.time + biteDuration;

        /// <summary>
        /// How deep a rope of <paramref name="length"/> hangs across a span of <paramref name="span"/>.
        ///
        /// <para>
        /// <c>0.5·L·√(1 − (d/L)²)</c>. Exact at both ends that matter — zero sag when the rope is
        /// pulled straight, half its length when the two knots meet and it doubles over — and within
        /// a fifth of the parabolic arc-length answer everywhere between. Which is the point: the
        /// rope this replaces sagged by <c>1 − dist/maxLength</c>, a RATIO, so how far it drooped
        /// depended on how long the rope was rather than on how much spare rope there was.
        /// </para>
        /// </summary>
        public static float SagDepth(float span, float length)
        {
            if (length <= 0.001f || span >= length) return 0f;

            float t = span / length;
            return 0.5f * length * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        }

        /// <summary>
        /// Lay the rope between two knots.
        ///
        /// <paramref name="tension01"/> is zero for a slack rope and one for a rope at its breaking
        /// stretch; it drives the thinning and the shiver.
        /// </summary>
        public void Draw(Vector3 a, Vector3 b, float length, float tension01)
        {
            if (line == null) return;

            int count = Mathf.Max(2, segments);
            if (line.positionCount != count) line.positionCount = count;
            if (drawn.Length != count) drawn = new Vector3[count];
            drawnCount = count;

            Vector3 axis = b - a;
            float span = axis.magnitude;

            if (span < 0.01f)
            {
                for (int i = 0; i < count; i++)
                {
                    line.SetPosition(i, a);
                    drawn[i] = a;
                }
                return;
            }

            float tension = Mathf.Clamp01(tension01);
            float sag = Mathf.Min(maxSag, SagDepth(span, length));

            float thickness = width * (1f - tensionThinning * tension);
            line.startWidth = thickness;
            line.endWidth = thickness;

            // Tiling is per metre of rope, so pulling a rope out longer lays down more braid rather
            // than stretching the braid it already has.
            line.textureScale = new Vector2(Mathf.Max(0.01f, span * braidsPerMetre), 1f);

            Vector3 right = Vector3.Cross(axis / span, Vector3.up);
            right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;
            Vector3 up = Vector3.Cross(right, axis / span);

            Displacement wobble = WobbleFor(span, tension);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);

                // Pinned at both knots: an envelope that does not vanish at t=0 and t=1 detaches the
                // rope from the things it is tied to.
                float envelope = 4f * t * (1f - t);

                Vector3 chord = a + axis * t;

                Vector3 p = chord;
                p.y -= envelope * sag;

                if (wobble.Amplitude > 0.0001f)
                {
                    float amp = wobble.Amplitude * envelope;
                    float phase = Time.time * wobble.Speed;

                    // Two axes, deliberately out of step in both frequency and speed, so the motion
                    // turns over in space. One axis alone is a flat ripple, and a flat ripple is
                    // invisible from half of all viewing angles.
                    p += right * (Mathf.Sin(t * Mathf.PI * 2f * wobble.Waves - phase) * amp);
                    p += up * (Mathf.Sin(t * Mathf.PI * 2f * wobble.Waves * 0.63f - phase * 1.31f) * amp * 0.7f);
                }

                // Last, after the wave. The sway has a vertical component, so clamping before it
                // lets the rope be pushed straight back down through the surface it was just lifted
                // out of — by less than the clearance, which is exactly the amount that shows.
                //
                // The two knots are exempt: they are tied to things, and lifting one would detach
                // the rope from what it is attached to.
                if (i > 0 && i < count - 1) p.y = Mathf.Max(p.y, FloorUnder(chord, sag));

                line.SetPosition(i, p);
                drawn[i] = p;
            }
        }

        // ── Aiming at the rope ─────────────────────────────────────────────────

        /// <summary>
        /// The points last drawn, so the rope can be aimed at.
        ///
        /// <para>
        /// A rope has no collider and is not going to get one — a chain of capsules along a curve
        /// that moves every frame would cost more than the rope does and would start blocking
        /// bullets, footsteps and every other raycast in the game. So being able to click one is
        /// done analytically against the shape actually on screen, which is this.
        /// </para>
        /// </summary>
        private Vector3[] drawn = System.Array.Empty<Vector3>();

        private int drawnCount;

        /// <summary>
        /// Where this rope comes closest to a ray, if that is within <paramref name="radius"/>.
        ///
        /// <para>
        /// Tested against the drawn points rather than the straight line between the knots, so a
        /// rope sagging on the ground is grabbed where it actually lies rather than where it would
        /// be if it were taut.
        /// </para>
        /// </summary>
        public bool Aimed(Ray ray, float maxDistance, float radius, out float distance, out Vector3 point)
        {
            distance = float.MaxValue;
            point = Vector3.zero;

            float best = radius;
            bool found = false;

            for (int i = 0; i + 1 < drawnCount; i++)
            {
                float gap = RayToSegment(ray.origin, ray.direction, drawn[i], drawn[i + 1],
                                         maxDistance, out float along, out Vector3 on);

                // Nearest to the EYE among the segments close enough to count, not the closest
                // approach overall: a rope crossing the aim twice is being pointed at where it is
                // in front, the same way any other pick works.
                if (gap > best || along >= distance) continue;

                best = gap;
                distance = along;
                point = on;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Distance between a forward ray and a line segment, with where on each the two are
        /// closest.
        ///
        /// <para>
        /// Pure and static so the picking can be tested without a scene. The clamps are what make it
        /// a SEGMENT and a forward RAY rather than two infinite lines — solved unclamped, a rope
        /// behind the player is as pickable as one in front.
        /// </para>
        /// </summary>
        public static float RayToSegment(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b,
                                         float maxDistance, out float alongRay, out Vector3 onSegment)
        {
            Vector3 seg = b - a;
            Vector3 toStart = a - origin;

            float segSq = Vector3.Dot(seg, seg);
            float project = Vector3.Dot(seg, direction);
            float segDot = Vector3.Dot(toStart, seg);
            float rayDot = Vector3.Dot(toStart, direction);

            // Zero when the ray and the segment are parallel, and when the segment has no length —
            // both of which mean any point on it is as good as any other, so take its start.
            float denom = segSq - project * project;

            float s = denom > 1e-6f ? Mathf.Clamp01((project * rayDot - segDot) / denom) : 0f;

            onSegment = a + seg * s;
            alongRay = Mathf.Clamp(s * project + rayDot, 0f, maxDistance);

            return Vector3.Distance(onSegment, origin + direction * alongRay);
        }

        /// <summary>How close this rope passes to a point. Used to find the same rope on every machine.</summary>
        public float DistanceTo(Vector3 worldPoint)
        {
            float best = float.MaxValue;

            for (int i = 0; i + 1 < drawnCount; i++)
                best = Mathf.Min(best, PointToSegment(worldPoint, drawn[i], drawn[i + 1]));

            return best;
        }

        private static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 seg = b - a;
            float segSq = Vector3.Dot(seg, seg);
            if (segSq < 1e-6f) return Vector3.Distance(p, a);

            float s = Mathf.Clamp01(Vector3.Dot(p - a, seg) / segSq);
            return Vector3.Distance(p, a + seg * s);
        }

        /// <summary>
        /// The height this point of the rope may not go below, or negative infinity where there is
        /// nothing underneath.
        ///
        /// <para>
        /// The ray starts a little above the straight line between the knots rather than at the
        /// sagged point, for two reasons. A point that has sagged into a hillside is INSIDE the
        /// mesh, and a downward ray from inside a mesh reports nothing — so probing from where the
        /// rope currently is would go blind in exactly the case this exists to fix. And starting
        /// above the chord is what lets the rope ride over a rise between its two ends rather than
        /// only ever being pushed up from below.
        /// </para>
        /// <para>
        /// It is also what keeps this cheap: the ray is only as long as the sag plus the head start,
        /// so a taut rope strung high in the air casts a two-metre ray that hits nothing, rather
        /// than hunting the ground all the way down.
        /// </para>
        /// </summary>
        private float FloorUnder(Vector3 chord, float sag)
        {
            if (!restOnGround) return float.NegativeInfinity;

            ground ??= new LeashGround(groundMask);

            Vector3 from = chord + Vector3.up * groundProbeAbove;
            float reach = groundProbeAbove + sag + groundClearance;

            float height = ground.HeightUnder(from, reach);
            return height > float.NegativeInfinity ? height + groundClearance : height;
        }

        /// <summary>Tell the probe what this rope is tied to, so it does not rest the rope on that.</summary>
        public void TiedBetween(Transform a, Transform b)
        {
            ground ??= new LeashGround(groundMask);
            ground.TiedBetween(a, b);
        }

        /// <summary>The travelling wave on the rope this frame, whichever of the three is loudest.</summary>
        private readonly struct Displacement
        {
            public readonly float Amplitude;
            public readonly float Waves;
            public readonly float Speed;

            public Displacement(float amplitude, float waves, float speed)
            {
                Amplitude = amplitude;
                Waves = waves;
                Speed = speed;
            }
        }

        private Displacement WobbleFor(float span, float tension)
        {
            // The crack outranks everything while it lasts. Squared rather than linear, so it is a
            // snap that is over rather than a wobble that fades — a rope going tight is sudden.
            if (Time.time < biteUntil)
            {
                float k = (biteUntil - Time.time) / Mathf.Max(biteDuration, 0.0001f);
                return new Displacement(biteAmplitude * k * k, biteWaves, biteSpeed);
            }

            if (tension > 0.01f)
                return new Displacement(shiverAmplitude * tension, 5f, shiverSpeed);

            // Wave count scales with length or a 30 m rope carries the same single hump a 3 m one
            // does, and reads as a rubber band.
            return new Displacement(swayAmplitude, Mathf.Clamp(span / 6f, 1f, 3f), swaySpeed);
        }
    }
}
