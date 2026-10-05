// The sun shrine: mirrors that bounce sunlight onto a sealed vault door. Hold the seal lit for a few
// seconds and the door opens for good.
//
// Nothing about the light crosses the wire. The sun is a pure function of the shared clock
// (DayNightCycle), the storms are too (Sandstorms), and the mirror arrangement is DialSet state
// every machine already holds — so each machine traces the beam itself, the way every machine draws
// the same storm. Only the verdict is the server's: its own trace fills the seal and opens the door
// through a one-way latch (SunSeal). A client's fill is presentation, and agrees with the server's
// to within the frame its clock is behind.
//
// The sun turns about the world's X axis, so its light always arrives in the world YZ plane and
// only its elevation drifts. That is why only the first mirror tilts, and why the shrine must be
// placed square to the world axes — see docs/AI/systems/SunShrine.md.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World;
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class SunShrine : DialSet
    {
        /// <summary>A beam that finds nothing is drawn this far and no further.</summary>
        private const float EscapeLength = 60f;

        /// <summary>How far up the sky the incoming shaft is drawn from the first mirror.</summary>
        private const float SkyShaftLength = 40f;

        /// <summary>Linecasts start and stop this far inside a hop, clear of the mirrors' own colliders.</summary>
        private const float OcclusionSkin = 0.3f;

        [Header("Mirrors")]
        [Tooltip("In beam order. The first catches the sun; each mirror has a turn dial and a tilt " +
                 "dial (2i, 2i+1), the tilt one inert on a mirror with a single tilt step.")]
        [SerializeField] private SunMirror[] mirrors;

        [Tooltip("How many mirrors a beam may bounce off before it is spent.")]
        [SerializeField, Min(1)] private int maxBounces = 6;

        [Header("Waking window")]
        [Tooltip("Points at the middle of the sky the first mirror can see the sun in.")]
        [SerializeField] private Transform oculus;

        [Tooltip("How far from the oculus axis the sun may be and still reach the first mirror, " +
                 "degrees. The sun sweeps 360° a day, so 60° is a third of the day.")]
        [SerializeField, Range(1f, 90f)] private float oculusHalfAngle = 60f;

        [Tooltip("Storm intensity at the first mirror that snuffs the beam out entirely.")]
        [SerializeField, Range(0f, 1f)] private float stormCutoff = 0.65f;

        [Header("Seal")]
        [Tooltip("The sun glyph. Its position is the centre and its forward faces out of the door.")]
        [SerializeField] private Transform sealTarget;

        [SerializeField, Min(0.05f)] private float sealRadius = 0.7f;

        [Tooltip("The glyph is sunk in a recess: light striking it further off face-on than this " +
                 "never reaches it. What stops the first mirror shining straight at it.")]
        [SerializeField, Range(1f, 89f)] private float sealAcceptDegrees = 15f;

        [Tooltip("Seconds the seal must stay fully lit to open.")]
        [SerializeField, Min(0.1f)] private float holdSeconds = 5f;

        [Tooltip("Seconds a full seal takes to go dark again once the light leaves it.")]
        [SerializeField, Min(0.1f)] private float drainSeconds = 3f;

        [SerializeField] private SunSeal seal;

        [SerializeField] private Renderer sealGlow;
        [SerializeField, ColorUsage(false, true)] private Color sealGlowColour = new(4f, 2.6f, 0.9f);

        [Header("Beam")]
        [Tooltip("How far from a disc's rim the beam's centre may pass and still count as a hit. " +
                 "Half the shaft's apparent width: the light is a column, not a line.")]
        [SerializeField, Min(0f)] private float beamRadius = 0.45f;

        [Tooltip("What stops a beam between mirrors: walls, pillars, a player standing in it.")]
        [SerializeField] private LayerMask occluders = 1;

        [SerializeField] private LineRenderer beam;
        [Tooltip("Alpha-blended, not additive: an additive shaft vanishes against pale sand in daylight.")]
        [SerializeField, ColorUsage(true, true)] private Color beamColour = new(1f, 0.78f, 0.32f, 0.75f);

        private static readonly int EmissionColourId = Shader.PropertyToID("_EmissionColor");

        private readonly List<Vector3> path = new(8);
        private readonly List<int> litMirrors = new(8);
        private MaterialPropertyBlock glowBlock;
        private float fill;

        /// <summary>How full the seal is, 0–1: the crank's bar, and 1 for good once the door is open.</summary>
        public override float? Progress => seal != null && seal.IsOpen ? 1f : fill;

        protected override int DialCount => 2 * mirrors.Length;

        protected override void Start()
        {
            base.Start();
            glowBlock = new MaterialPropertyBlock();
        }

        // ─────────── Dials: two per mirror, packed mixed-radix (a digit per dial) ───────────

        private int StepsOf(int dial) =>
            dial % 2 == 0 ? mirrors[dial / 2].YawSteps : mirrors[dial / 2].TiltSteps;

        private int PlaceOf(int dial)
        {
            int place = 1;
            for (int d = 0; d < dial; d++) place *= StepsOf(d);
            return place;
        }

        /// <summary>One dial's step in <paramref name="arrangement"/>.</summary>
        public int StepOf(int arrangement, int dial) => arrangement / PlaceOf(dial) % StepsOf(dial);

        /// <summary>How many arrangements there are. Tests walk them all.</summary>
        public int ArrangementCount => PlaceOf(DialCount);

        protected override int Turn(int arrangement, int dial)
        {
            int step = StepOf(arrangement, dial);
            int next = (step + 1) % StepsOf(dial);
            return arrangement + (next - step) * PlaceOf(dial);
        }

        protected override int Fresh(System.Random random)
        {
            int arrangement = 0;
            for (int d = 0; d < DialCount; d++)
                arrangement += random.Next(StepsOf(d)) * PlaceOf(d);
            return arrangement;
        }

        protected override void Show(int arrangement, int turnedDial, bool instant)
        {
            for (int i = 0; i < mirrors.Length; i++)
            {
                int cranked = turnedDial / 2 == i ? turnedDial % 2 : -1;
                mirrors[i].Show(StepOf(arrangement, 2 * i), StepOf(arrangement, 2 * i + 1), instant, cranked);
            }
        }

        public override string Describe(int dial) =>
            litMirrors.Contains(dial / 2) ? "in the sunbeam" : string.Empty;

        // ─────────── The light ───────────

        private void Update()
        {
            if (!IsKnown) return;

            float strength = SunStrength(out Vector3 toSun);
            bool sealLit = strength > 0f && Trace(i => mirrors[i].Normal, toSun, path, litMirrors, occlude: true);

            DrawBeam(strength, toSun);
            Fill(sealLit ? strength : 0f);
        }

        /// <summary>
        /// How strongly the sun reaches the first mirror, 0–1, and which way it lies. Zero at night,
        /// outside the waking window, and in the thick of a sandstorm.
        /// </summary>
        private float SunStrength(out Vector3 toSun)
        {
            toSun = Vector3.up;
            DayNightCycle sun = LiveSun();
            if (sun == null) return 0f;

            toSun = -sun.transform.forward;
            if (!InWakingWindow(toSun)) return 0f;

            float storm = Sandstorms.IntensityAt(mirrors[0].Centre);
            return Mathf.Clamp01(1f - storm / Mathf.Max(0.01f, stormCutoff));
        }

        private static DayNightCycle LiveSun()
        {
            IReadOnlyList<DayNightCycle> live = DayNightCycle.Live;
            for (int i = 0; i < live.Count; i++)
                if (!live[i].freezeCycle) return live[i];
            return null;
        }

        /// <summary>Whether the first mirror can see a sun lying toward <paramref name="toSun"/>.</summary>
        public bool InWakingWindow(Vector3 toSun) =>
            toSun.y > 0f && Vector3.Angle(toSun, oculus.forward) <= oculusHalfAngle;

        /// <summary>
        /// Would <paramref name="arrangement"/> light the seal with the sun toward
        /// <paramref name="toSun"/>? Pure geometry, no occlusion: tests ask it of the prefab to prove
        /// the puzzle can be solved at every hour of the waking window.
        /// </summary>
        public bool LightsSeal(int arrangement, Vector3 toSun) =>
            Trace(i => mirrors[i].NormalAt(StepOf(arrangement, 2 * i), StepOf(arrangement, 2 * i + 1)),
                  toSun, null, null, occlude: false);

        /// <summary>
        /// Follow the sunlight from the first mirror, bouncing off whichever mirror's face it meets
        /// next, until it reaches the seal, strikes something else, or escapes. Order-free on
        /// purpose: the puzzle is the geometry, and any path the light can really take counts.
        /// </summary>
        private bool Trace(System.Func<int, Vector3> normalOf, Vector3 toSun,
                           List<Vector3> points, List<int> lit, bool occlude)
        {
            points?.Clear();
            lit?.Clear();

            int current = 0;
            Vector3 from = mirrors[0].Centre;
            Vector3 dir = -toSun;
            points?.Add(from);

            for (int bounce = 0; bounce <= maxBounces; bounce++)
            {
                Vector3 normal = normalOf(current);
                if (Vector3.Dot(dir, normal) >= 0f) return false;   // light on the mirror's back

                lit?.Add(current);
                dir = Vector3.Reflect(dir, normal);

                int next = NearestMirror(normalOf, from, dir, current, out float mirrorDistance);
                bool toSeal = HitsDisc(from, dir, sealTarget.position, sealTarget.forward, sealRadius,
                                       out float sealDistance) && sealDistance < mirrorDistance;
                float reach = toSeal ? sealDistance : next >= 0 ? mirrorDistance : EscapeLength;

                if (occlude && Blocked(from, dir, reach, out float blockedAt))
                {
                    points?.Add(from + dir * blockedAt);
                    return false;
                }

                Vector3 to = from + dir * reach;
                points?.Add(to);

                if (toSeal)
                    return Vector3.Angle(-dir, sealTarget.forward) <= sealAcceptDegrees;
                if (next < 0) return false;

                current = next;
                from = to;
            }

            return false;
        }

        private int NearestMirror(System.Func<int, Vector3> normalOf, Vector3 from, Vector3 dir,
                                  int except, out float distance)
        {
            int best = -1;
            distance = float.MaxValue;
            for (int i = 0; i < mirrors.Length; i++)
            {
                if (i == except) continue;
                if (!HitsDisc(from, dir, mirrors[i].Centre, normalOf(i), mirrors[i].Radius, out float d)) continue;
                if (d >= distance) continue;
                best = i;
                distance = d;
            }
            return best;
        }

        /// <summary>Where a ray crosses a disc's plane, if it passes within the disc plus the beam's own radius.</summary>
        private bool HitsDisc(Vector3 from, Vector3 dir, Vector3 centre, Vector3 normal, float radius,
                              out float distance)
        {
            distance = float.MaxValue;
            float along = Vector3.Dot(dir, normal);
            if (Mathf.Abs(along) < 1e-4f) return false;

            float t = Vector3.Dot(centre - from, normal) / along;
            if (t <= 1e-3f) return false;

            float reach = radius + beamRadius;
            if ((from + dir * t - centre).sqrMagnitude > reach * reach) return false;

            distance = t;
            return true;
        }

        private bool Blocked(Vector3 from, Vector3 dir, float reach, out float at)
        {
            at = reach;
            float span = reach - 2f * OcclusionSkin;
            if (span <= 0f) return false;

            if (!Physics.Raycast(from + dir * OcclusionSkin, dir, out RaycastHit hit, span, occluders,
                                 QueryTriggerInteraction.Ignore))
                return false;

            at = OcclusionSkin + hit.distance;
            return true;
        }

        // ─────────── What every machine sees ───────────

        private void DrawBeam(float strength, Vector3 toSun)
        {
            if (beam == null) return;

            beam.enabled = strength > 0f && path.Count > 1;
            if (!beam.enabled) return;

            beam.positionCount = path.Count + 1;
            beam.SetPosition(0, path[0] + toSun * SkyShaftLength);
            for (int i = 0; i < path.Count; i++) beam.SetPosition(i + 1, path[i]);

            Color colour = beamColour;
            colour.a *= strength;
            beam.startColor = beam.endColor = colour;
        }

        private void Fill(float light)
        {
            bool open = seal != null && seal.IsOpen;
            if (!open)
            {
                fill = light > 0f
                    ? Mathf.MoveTowards(fill, 1f, light * Time.deltaTime / holdSeconds)
                    : Mathf.MoveTowards(fill, 0f, Time.deltaTime / drainSeconds);

                // The verdict is the server's. Decides rather than Simulates, like the first roll: a
                // client whose clock ran a frame ahead must not open a door nobody else has opened.
                if (fill >= 1f && Network.Decides && seal != null) seal.Open();
            }

            if (sealGlow == null) return;
            sealGlow.GetPropertyBlock(glowBlock);
            glowBlock.SetColor(EmissionColourId, sealGlowColour * (open ? 1f : fill));
            sealGlow.SetPropertyBlock(glowBlock);
        }
    }
}
