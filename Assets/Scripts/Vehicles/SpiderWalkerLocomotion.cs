// Locomotion for the six-legged walking station.
//
// Three rules, and between them they are the whole component:
//
//  1. A joint may only rotate about its own measured axle. The IK emits one scalar per hinge
//     (WalkerLegSolver), bounded by that joint's travel, so an out-of-plane rotation or an
//     unnatural fold is not representable rather than merely discouraged.
//  2. A planted foot does not move. Each leg has a yaw axle as well as its pitch hinges, so any
//     reachable point can be hit exactly and stance feet are held to the world, not approximated.
//  3. The hull may only ask for what the legs can deliver. Top speed is derived from stride and
//     cadence (WalkerGait.MaxSpeed) and the commanded twist is clamped to it, so the machine
//     slows down instead of skating ahead of its own feet.
//
// The deck is held level. This is a crewed platform: the legs absorb the terrain so that riders
// on the deck are not tipped around, which is the point of building a station on legs at all.
//
// This is a kinematic layer. SpiderWalkerDriver hands it a twist; nothing else writes the hull's
// transform. WalkerPlatformCarrier (order 200) runs afterwards and carries riders along.
using System.Collections.Generic;
using SpaceGame.Walker;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class SpiderWalkerLocomotion : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Armature holding the Coxa_*/Hip_*/Knee_*/Ankle_* bones. Auto-found if empty.")]
    [SerializeField] private Transform armatureRoot;
    [Tooltip("Transform that gets height-corrected and yawed. Defaults to this object.")]
    [SerializeField] private Transform body;

    [Header("Joint travel (degrees either side of rest)")]
    [Tooltip("Azimuth swing at the hip. This is what sets the machine's stride, and so its speed.")]
    [SerializeField] private float yawRange = 40f;
    [SerializeField] private float hipRange = 45f;
    [SerializeField] private float kneeRange = 60f;
    [SerializeField] private float ankleRange = 45f;
    [Tooltip("Sole roll, for meeting slopes the leg is walking ACROSS. Needs a Foot_* bone in " +
             "the model; without one the sole can only pitch.")]
    [SerializeField] private float rollRange = 30f;

    [Header("Gait")]
    [Tooltip("Legs allowed in the air at once. 1 is a ripple wave (five feet always down); " +
             "3 is an alternating tripod, twice as fast and still statically stable.")]
    [Range(1, 3)]
    [SerializeField] private int swingLegs = 1;
    [Tooltip("Seconds one leg spends in the air, at full speed.")]
    [SerializeField] private float stepDuration = 0.45f;
    [Tooltip("Foot lift on level ground, as a fraction of the leg's reach. The swing also probes " +
             "the ground it is crossing and lifts further to clear anything in the way.")]
    [SerializeField] private float stepClearance = 0.08f;
    [Tooltip("Extra height held over the tallest obstacle found under a swing.")]
    [SerializeField] private float obstacleClearance = 1.5f;

    [Header("Ground")]
    [Tooltip("Layers treated as ground. The walker's OWN colliders are always ignored regardless " +
             "of this mask, so leaving it as Everything is safe.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float rayStartAbove = 12f;
    [SerializeField] private float rayLength = 400f;
    [Tooltip("On Start, drop the walker onto the ground so the legs begin within reach.")]
    [SerializeField] private bool snapToGroundOnStart = true;

    [Header("Body")]
    [Tooltip("Measure ride height from the rest pose on Awake. The rig's origin sits at foot " +
             "level, so a hand-guessed value here will launch the walker on the first frame.")]
    [SerializeField] private bool autoCalibrateRideHeight = true;
    [Tooltip("Ride height above the average planted foot. Overwritten when auto-calibrating.")]
    [SerializeField] private float rideHeight;
    [SerializeField] private float heightSmooth = 5f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private class LegState
    {
        public WalkerRig.Leg Rig;
        public Vector3 HomeLocal;       // rest foothold, in body space
        public Vector3 Foot;            // world contact point; fixed while planted
        public Vector3 GroundNormal = Vector3.up;
        public Vector3 SwingFrom, SwingTo;
        public float PhaseOffset;       // this leg's slice of the gait cycle
        public bool Swinging;
        public float SwingT;            // 0..1 across the current swing
        public float SwingLift;         // arc height for THIS step, sized to what it must clear
        public bool WasInSlice;         // for spotting the moment the slice opens
        public bool Unreachable;        // last solve could not honour the foot; step it early
        public float ReachFraction;
    }

    /// Feet that must stay down for the hull to be statically supported.
    private const int MinPlantedLegs = 3;
    /// Fresh footholds are pulled inside this fraction of reach, so a leg that has just planted
    /// is not immediately at the edge of what it can hold.
    private const float FootholdReachFraction = 0.85f;
    /// Fraction of the yaw travel the stride is sized against, leaving the joint some margin
    /// rather than running it onto its stop at the end of every step.
    private const float StrideYawFraction = 0.85f;

    private readonly List<LegState> legs = new List<LegState>();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[24];

    private WalkerGait gait;
    private Transform selfRoot;
    private float commandedSpeed;       // world units/second along body forward
    private float commandedYawRate;     // degrees/second about world up
    private float currentYaw;           // single owner of the hull's heading
    private Vector3 lastBodyPos;
    private Vector3 velocity;
    private bool ready;

    private float strideLength;         // how far a foot travels through the body frame per cycle
    private float cycleDistance;        // how far the HULL travels in that same cycle
    private float stepHeight;
    private float maxFootRadius;        // furthest foot from the hull centre, at rest
    private float footprintRadius;      // how far the sole reaches from its contact point

    public bool IsReady => ready;
    public int LegCount => legs.Count;
    /// Smoothed world velocity of the hull, as achieved rather than as commanded.
    public Vector3 MeasuredVelocity => velocity;

    /// Fastest the legs can carry the hull. Derived from stride and cadence, not authored: asking
    /// for more than this would require the feet to slide.
    public float MaxSpeed { get; private set; }
    /// Fastest the hull may turn before the outermost foot has to slide.
    public float MaxYawRate { get; private set; }

    /// Ground speed of the hardest-worked foot. A pivot moves the outer legs even when the hull's
    /// centre is still, so cadence has to be paced by this rather than by the hull's own speed.
    private float Pace =>
        Mathf.Abs(commandedSpeed) + Mathf.Abs(commandedYawRate) * Mathf.Deg2Rad * maxFootRadius;

    /// Last frame's gait state. A legged machine is hard to reason about from the outside; this
    /// is what tells you whether a stall is the speed clamp or the gait failing to step.
    public struct Diagnostics
    {
        public float AchievedSpeed;
        public int StanceLegs;
        public int SwingingLegs;
        /// Legs whose solve could not honour their foot. Should be zero on reasonable ground.
        public int UnreachableLegs;
        /// Largest reach fraction across the legs. Above 1 means a foot is visibly detached.
        public float WorstReachFraction;
        public float Phase;
    }

    private Diagnostics diagnostics;
    public Diagnostics LastFrame => diagnostics;

    /// World contact point of leg `index`, and whether it is mid-swing. For debug overlays and
    /// tests that need to prove planted feet do not slide.
    public bool TryGetFoot(int index, out Vector3 foot, out bool swinging)
    {
        if (index < 0 || index >= legs.Count) { foot = default; swinging = false; return false; }
        foot = legs[index].Foot;
        swinging = legs[index].Swinging;
        return true;
    }

    /// What the driver calls each frame. Speed is world units/second along the hull's forward and
    /// yaw rate is degrees/second; both are clamped to what the legs can actually deliver.
    public void SetTwist(float speed, float yawRate)
    {
        commandedSpeed = Mathf.Clamp(speed, -MaxSpeed, MaxSpeed);
        commandedYawRate = Mathf.Clamp(yawRate, -MaxYawRate, MaxYawRate);
    }

    // ─────────── setup ───────────

    private void Awake() => Initialise();

    /// Discovers and measures the rig. Separated from Awake so the whole locomotion can be driven
    /// deterministically from a test or an editor tool, where Unity's callbacks never fire.
    public void Initialise()
    {
        legs.Clear();
        if (body == null) body = transform;
        selfRoot = transform;
        if (armatureRoot == null) armatureRoot = WalkerRig.FindArmature(transform);

        foreach (WalkerRig.Leg rig in WalkerRig.Build(armatureRoot, body))
        {
            Vector3 contact = rig.Ankle.TransformPoint(rig.Geometry.ContactLocalAnkle);
            legs.Add(new LegState
            {
                Rig = rig,
                Foot = contact,
                HomeLocal = body.InverseTransformPoint(contact),
            });
        }

        ready = legs.Count > 0;
        if (!ready)
        {
            Debug.LogWarning("[SpiderWalkerLocomotion] No leg chains found; disabled.", this);
            return;
        }

        // Fixed ordering around the hull, taken once from the rest footholds, so the gait's leg
        // sequence is stable. Sorting by live foot position would let the order flip between
        // frames as feet move, and the sequence would stutter.
        legs.Sort((a, b) =>
            Mathf.Atan2(a.HomeLocal.z, a.HomeLocal.x).CompareTo(
            Mathf.Atan2(b.HomeLocal.z, b.HomeLocal.x)));

        AssignGaitOrder();
        MeasureGait();

        if (autoCalibrateRideHeight)
        {
            float avgFootY = 0f;
            foreach (LegState leg in legs) avgFootY += leg.Foot.y;
            rideHeight = body.position.y - avgFootY / legs.Count;
        }

        currentYaw = body.eulerAngles.y;
        lastBodyPos = body.position;
    }

    /// A metachronal wave: legs lift in sequence up one side and then the other, so consecutive
    /// steps are never taken by neighbours. Derived from where the feet actually are, so it stays
    /// correct if the rig is re-authored with a different leg count or layout.
    private void AssignGaitOrder()
    {
        var sequence = new List<LegState>(legs);
        sequence.Sort((a, b) =>
        {
            int sideA = a.HomeLocal.x < 0f ? 0 : 1;
            int sideB = b.HomeLocal.x < 0f ? 0 : 1;
            if (sideA != sideB) return sideA.CompareTo(sideB);
            return a.HomeLocal.z.CompareTo(b.HomeLocal.z);   // rear to front along each side
        });

        for (int i = 0; i < sequence.Count; i++)
            sequence[i].PhaseOffset = WalkerGait.MetachronalOffset(i, sequence.Count);
    }

    /// Stride, cadence and the speeds they imply. Everything is derived from the measured rig, so
    /// rescaling it or widening the yaw travel re-tunes the machine with no numbers to edit.
    private void MeasureGait()
    {
        float radius = 0f, reach = 0f;
        maxFootRadius = 0f;
        foreach (LegState leg in legs)
        {
            radius += leg.Rig.Geometry.RestFootRadius;
            reach += leg.Rig.Geometry.MaxReach;
            maxFootRadius = Mathf.Max(maxFootRadius, new Vector2(leg.HomeLocal.x, leg.HomeLocal.z).magnitude);
        }
        radius /= legs.Count;
        reach /= legs.Count;

        // How far the sole spreads around its contact point, measured off the foot's own meshes so
        // the ground sampling covers the real footprint rather than a guessed radius.
        footprintRadius = 0f;
        foreach (LegState leg in legs)
        {
            Transform sole = leg.Rig.Foot != null ? leg.Rig.Foot : leg.Rig.Ankle;
            foreach (MeshRenderer r in sole.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("COL_")) continue;
                Vector3 size = r.bounds.size;
                footprintRadius = Mathf.Max(footprintRadius, Mathf.Max(size.x, size.z) * 0.35f);
            }
        }
        if (footprintRadius < 1e-3f) footprintRadius = reach * 0.05f;

        // The foot sweeps an arc when the coxa turns, so the usable stride is that arc's chord --
        // taken over most of the travel rather than all of it, so the joint keeps some margin.
        strideLength = 2f * radius * Mathf.Sin(yawRange * StrideYawFraction * Mathf.Deg2Rad);
        stepHeight = reach * stepClearance;

        float duty = WalkerGait.SwingDuty(swingLegs, legs.Count);
        cycleDistance = WalkerGait.CycleDistance(strideLength, duty);
        MaxSpeed = WalkerGait.MaxSpeed(strideLength, stepDuration, duty);
        // The outermost foot travels furthest per degree of turn, so it is what bounds the pivot.
        MaxYawRate = maxFootRadius > 1e-3f ? MaxSpeed / maxFootRadius * Mathf.Rad2Deg : 0f;
    }

    private void Start()
    {
        if (snapToGroundOnStart) SnapToGround();
    }

    /// Drop the hull so the legs start within reach of the ground. Placed in a scene by hand the
    /// walker is usually floating, and every foot would then start at full extension.
    public void SnapToGround()
    {
        if (!ready) return;
        if (!GroundRay(body.position + Vector3.up * 200f, 5000f, out RaycastHit hit)) return;

        body.position += Vector3.up * ((hit.point.y + rideHeight) - body.position.y);

        foreach (LegState leg in legs)
        {
            Vector3 home = body.TransformPoint(leg.HomeLocal);
            if (SampleGround(home, out Vector3 ground, out Vector3 groundNormal))
            {
                leg.Foot = ground;
                leg.GroundNormal = groundNormal;
            }
            else
            {
                leg.Foot = home;
                leg.GroundNormal = Vector3.up;
            }
            leg.Swinging = false;
            leg.SwingT = 0f;
            leg.WasInSlice = false;
            leg.Unreachable = false;
        }
        lastBodyPos = body.position;
    }

    // ─────────── frame ───────────

    private void LateUpdate() => Step(Time.deltaTime);

    /// One frame of locomotion. Public and dt-driven so a test can march it forward without a
    /// running player loop; nothing in here reads Time directly.
    public void Step(float deltaTime)
    {
        if (!ready) return;
        float dt = Mathf.Max(deltaTime, 1e-5f);

        MoveHull(dt);
        UpdateGait(dt);
        LevelBody(dt);
        SolveLegs();

        velocity = Vector3.Lerp(velocity, (body.position - lastBodyPos) / dt, 1f - Mathf.Exp(-8f * dt));
        lastBodyPos = body.position;
        diagnostics.Phase = gait.Phase;
    }

    /// Advance the hull, then advance the gait clock by the distance it covered. Because the twist
    /// was already clamped to what the stride can carry, there is nothing left to negotiate here.
    private void MoveHull(float dt)
    {
        currentYaw += commandedYawRate * dt;

        Vector3 forward = Quaternion.AngleAxis(currentYaw, Vector3.up) * Vector3.forward;
        Vector3 moved = forward * (commandedSpeed * dt);
        body.position += moved;

        gait.Advance(Pace * dt, cycleDistance);

        diagnostics.AchievedSpeed = moved.magnitude / dt * Mathf.Sign(commandedSpeed);
    }

    private void UpdateGait(float dt)
    {
        float duty = WalkerGait.SwingDuty(swingLegs, legs.Count);
        Vector3 up = Vector3.up;
        Vector3 linear = Quaternion.AngleAxis(currentYaw, Vector3.up) * Vector3.forward * commandedSpeed;
        float yawRate = commandedYawRate * Mathf.Deg2Rad;

        // How long the foot about to be planted will spend in the air and then on the ground, at
        // the pace currently commanded. Footholds are aimed using both.
        //
        // A slow walk takes long steps in time, which is right, but the relationship diverges as
        // the hull approaches a standstill -- and a stationary machine can still be forced to step
        // by terrain moving under it. Bound the swing so those steps stay watchable.
        float stance = WalkerGait.StanceDuration(Pace, strideLength);
        float swing = Pace > 1e-3f
            ? Mathf.Min(WalkerGait.SwingDuration(Pace, strideLength, duty), stepDuration * 6f)
            : stepDuration;

        int planted = 0;
        foreach (LegState leg in legs) if (!leg.Swinging) planted++;

        foreach (LegState leg in legs)
        {
            // The phase decides when a swing STARTS; the swing's own timer owns its progress.
            // Reading progress straight off the phase slice looks equivalent and is not: a leg
            // stepped early is by definition outside its slice, so it would be planted again on
            // the very next frame -- teleporting the foot to the new foothold with no arc at all.
            bool inSlice = WalkerGait.IsSwinging(gait.Phase, leg.PhaseOffset, duty, out _);
            bool sliceOpened = inSlice && !leg.WasInSlice;
            leg.WasInSlice = inSlice;

            if (leg.Swinging)
            {
                leg.SwingT += dt / Mathf.Max(swing, 1e-3f);
                if (leg.SwingT >= 1f)
                {
                    leg.Foot = leg.SwingTo;
                    leg.Swinging = false;
                    planted++;
                }
                else
                {
                    leg.Foot = WalkerGait.SwingPoint(leg.SwingFrom, leg.SwingTo, leg.SwingT, up, leg.SwingLift);
                }
                continue;
            }

            // A leg whose foot the linkage can no longer honour is stepped early. This is the only
            // adaptive rule in the gait, and it is what makes broken ground work: the clock alone
            // cannot know that terrain has pulled a foothold out of reach. Scheduled steps are not
            // gated on the planted count -- the phase table already guarantees support for those,
            // and skipping one would cost the leg its slice entirely.
            bool forced = leg.Unreachable && planted - 1 >= MinPlantedLegs;
            if (!sliceOpened && !forced) continue;

            Vector3 home = body.TransformPoint(leg.HomeLocal);
            Vector3 drift = WalkerGait.FootDrift(
                leg.Rig.Hip.position - body.position, linear, yawRate, up);

            leg.SwingFrom = leg.Foot;
            leg.SwingTo = ResolveFoothold(leg, home, drift, stance, swing);
            leg.SwingLift = SwingLiftFor(leg.SwingFrom, leg.SwingTo);
            leg.SwingT = 0f;
            leg.Swinging = true;
            leg.Unreachable = false;
            planted--;
        }

        int swinging = 0;
        foreach (LegState leg in legs) if (leg.Swinging) swinging++;
        diagnostics.SwingingLegs = swinging;
        diagnostics.StanceLegs = legs.Count - swinging;
    }

    /// How high this particular step has to be lifted.
    ///
    /// A fixed arc height is only ever right on level ground. The swing is a straight line
    /// between two footholds, so anything standing between them gets walked through -- and on
    /// broken ground that is most of them. Probing what is actually under the path and clearing
    /// the tallest thing found is the difference between stepping over a rock and through it.
    private float SwingLiftFor(Vector3 from, Vector3 to)
    {
        float highest = Mathf.Max(from.y, to.y);
        const int probes = 5;
        for (int i = 1; i < probes; i++)
        {
            Vector3 along = Vector3.Lerp(from, to, i / (float)probes);
            if (GroundRay(along + Vector3.up * rayStartAbove, rayLength, out RaycastHit hit))
                highest = Mathf.Max(highest, hit.point.y);
        }

        // Measured from the higher end, because that is the one the arc has least room over.
        float needed = highest - Mathf.Max(from.y, to.y) + obstacleClearance;
        return Mathf.Max(stepHeight, needed);
    }

    /// Ground under a foot, sampled across the whole sole rather than at one point.
    ///
    /// A single ray makes the foot pivot on whatever one point it happened to hit, so a sole this
    /// size sinks a corner into the ground whenever that point is not representative -- and the
    /// normal snaps as the ray crosses an edge. Averaging over the footprint gives the plane the
    /// sole will actually rest on, and taking the highest hit keeps the toe out of the dirt.
    private bool SampleGround(Vector3 at, out Vector3 point, out Vector3 normal)
    {
        point = at;
        normal = Vector3.up;
        if (!GroundRay(at + Vector3.up * rayStartAbove, rayLength, out RaycastHit centre)) return false;

        Vector3 sumNormal = centre.normal;
        float highest = centre.point.y;
        int hits = 1;

        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * footprintRadius;
            if (!GroundRay(at + offset + Vector3.up * rayStartAbove, rayLength, out RaycastHit hit)) continue;
            sumNormal += hit.normal;
            highest = Mathf.Max(highest, hit.point.y);
            hits++;
        }

        normal = sumNormal.sqrMagnitude > 1e-6f ? (sumNormal / hits).normalized : Vector3.up;
        point = new Vector3(centre.point.x, highest, centre.point.z);
        return true;
    }

    /// Where this leg plants next: half its coming drift ahead of its rest foothold, dropped onto
    /// the ground, then pulled inside the linkage's reach.
    private Vector3 ResolveFoothold(LegState leg, Vector3 home, Vector3 drift, float stance, float swing)
    {
        Vector3 probe = WalkerGait.Foothold(home, drift, stance, swing, strideLength);

        Vector3 target;
        if (SampleGround(probe, out Vector3 ground, out Vector3 groundNormal))
        {
            target = ground;
            leg.GroundNormal = groundNormal;
        }
        else
        {
            target = leg.Foot;      // nothing under us: hold the current foothold
        }

        // Plant comfortably inside reach. Landing exactly at the limit would leave the leg
        // unreachable on the very next frame, and it would thrash instead of walk.
        Vector3 hip = leg.Rig.Hip.position;
        Vector3 offset = target - hip;
        float limit = leg.Rig.Geometry.MaxReach * FootholdReachFraction;
        if (offset.magnitude > limit) target = hip + offset.normalized * limit;

        return target;
    }

    /// Hold the deck level and ride at a fixed height above the planted feet. The hull's only
    /// rotation is the heading this component owns, so nothing can fight it for the pose.
    private void LevelBody(float dt)
    {
        float sum = 0f;
        int planted = 0;
        foreach (LegState leg in legs)
            if (!leg.Swinging) { sum += leg.Foot.y; planted++; }

        if (planted == 0)
        {
            foreach (LegState leg in legs) sum += leg.Foot.y;
            planted = legs.Count;
        }

        body.rotation = Quaternion.AngleAxis(currentYaw, Vector3.up);

        Vector3 pos = body.position;
        pos.y = Mathf.Lerp(pos.y, sum / planted + rideHeight, 1f - Mathf.Exp(-heightSmooth * dt));
        body.position = pos;
    }

    // ─────────── IK ───────────

    private void SolveLegs()
    {
        var limits = new WalkerLegSolver.Limits
        {
            Yaw = yawRange, Hip = hipRange, Knee = kneeRange, Ankle = ankleRange, Roll = rollRange,
        };

        int unreachable = 0;
        float worst = 0f;

        foreach (LegState leg in legs)
        {
            WalkerLegGeometry g = leg.Rig.Geometry;
            var frame = new WalkerLegSolver.Frame
            {
                Hip = leg.Rig.Hip.position,
                YawAxis = body.TransformDirection(g.YawAxisBody).normalized,
                RestFwd = body.TransformDirection(g.RestFwdBody).normalized,
            };

            WalkerLegSolver.Result r = WalkerLegSolver.Solve(frame, g, limits, leg.Foot, leg.GroundNormal);
            WalkerLegSolver.Apply(leg.Rig, r);

            // Only a planted leg's failure matters: a swinging foot is on its way somewhere else
            // and will be re-aimed anyway.
            leg.Unreachable = r.Clamped && !leg.Swinging;
            leg.ReachFraction = r.ReachFraction;

            if (leg.Unreachable) unreachable++;
            worst = Mathf.Max(worst, r.ReachFraction);
        }

        diagnostics.UnreachableLegs = unreachable;
        diagnostics.WorstReachFraction = worst;
    }

    // ─────────── ground ───────────

    // The walker is wrapped in its own colliders (deck, hull, one box per leg segment), so a plain
    // masked raycast plants the feet on the machine itself. Always reject self-hits.
    private bool GroundRay(Vector3 origin, float distance, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, distance,
                                        groundMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider col = hitBuffer[i].collider;
            if (col == null || col.transform.IsChildOf(selfRoot)) continue;
            if (hitBuffer[i].distance < bestDist)
            {
                bestDist = hitBuffer[i].distance;
                best = hitBuffer[i];
                found = true;
            }
        }
        return found;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying || !ready) return;
        foreach (LegState leg in legs)
        {
            Gizmos.color = leg.Swinging ? Color.yellow : (leg.Unreachable ? Color.red : Color.green);
            Gizmos.DrawSphere(leg.Foot, 0.35f);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
            Gizmos.DrawLine(leg.Rig.Hip.position, leg.Foot);

            // the arc this foot can sweep by yawing, which is the leg's whole stride
            Vector3 home = body.TransformPoint(leg.HomeLocal);
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireSphere(home, strideLength * 0.5f);
        }
    }
}
