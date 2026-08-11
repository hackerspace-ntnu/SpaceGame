// Walk -> trot -> canter/gallop, as one continuous function of speed.
//
// ─────────── why this is not TrotGait ───────────
//
// `TrotGait` blends a ripple into diagonal pairs and stops there, and that covers everything a
// quadruped does up to a trot. It cannot express what a horse does above one, because a canter and
// a gallop are ASYMMETRIC: there is a LEADING foreleg, the two diagonals stop being diagonals, and
// the cycle has a SUSPENSION -- a stretch with no feet on the ground at all. No symmetric pattern
// has a shape that can produce those, so this is a new `IGaitPattern` rather than a tuning of the
// shipped one. It is the only new policy this machine adds; stride, body motion and foot style are
// all shipped implementations.
//
// ─────────── the one rule that makes it safe ───────────
//
// OFFSETS ARE A CONTINUOUS FUNCTION OF `runBlend`, never a table swapped at a threshold. A foot
// that is in the air when its offset jumps is teleported: the gait re-reads the offset every frame,
// so the slice it was swinging in closes underneath it. Every transition here is a
// `WalkerGait.BlendOffset` -- which walks the SHORT way round the cycle -- driven by a parameter
// that is itself continuous in `runBlend`:
//
//     runBlend   0 ......... walkToTrot ......... trotToCanter ......... 1
//     offsets    walk -----> trot -------------- trot -----> canter/gallop
//
// The two segments meet at the trot, so the whole function is continuous at both joins as well as
// inside them. The plateau between them is deliberate: a horse holds a trot over a range of speeds
// rather than passing through it.
//
// ─────────── the three tables, all derived from where the feet are ───────────
//
//   walk    four-beat lateral sequence: LH, LF, RH, RF at 0, 1/4, 1/2, 3/4.
//   trot    diagonal pairs: LF+RH against RF+LH, half a cycle apart.
//   gallop  transverse, right lead: LH, RH, LF, RF, then a suspension.
//
// Which leg is which comes from `HomeLocal` against the machine's own mean foothold, never from a
// leg index -- re-author the rig with its legs in a different order and it still gallops rather
// than merely having the right number of feet in the air.
//
// Invariant I1: `Duty` never returns 0, bound or unbound. A duty of zero makes the derived top
// speed zero, which makes `SetTwist` clamp every command to zero, which stops a clock advanced by
// distance travelled -- and nothing reopens a slice after that.
using System.Collections.Generic;
using SpaceGame.Locomotion;
using UnityEngine;

/// A quadruped's gait ladder: four-beat walk, two-beat trot, asymmetric canter and gallop.
public class CanterGait : IGaitPattern
{
    // ─────────── the gallop table ───────────
    //
    // Phases at which each leg leaves the ground, anchored so the TRAILING FORELEG sits at zero.
    // The anchor is arbitrary as far as the machine is concerned -- the phase origin has no
    // meaning -- but it is chosen to keep the walk-over from the trot short, and a short walk-over
    // is a transition that reads as one gait becoming another rather than as four feet scrambling.
    //
    // Read them cyclically from the trailing hind and they are the textbook transverse gallop:
    // LH (0.64), RH (0.78), LF (1.00 = 0.00), RF (0.14). The 0.50 gap between the leading foreleg
    // and the trailing hind is the SUSPENSION: with the run duty at 0.62, every foot is in the air
    // for the 0.12 of the cycle that gap leaves over.
    private const float TrailingForeOffset = 0.00f;
    private const float LeadingForeOffset = 0.14f;
    private const float TrailingHindOffset = 0.64f;
    private const float LeadingHindOffset = 0.78f;

    /// Circular spread of the gallop offsets: the largest gap between consecutive footfalls is
    /// 0.50 of a cycle, so the four of them occupy the other half. A suspension exists exactly
    /// when the duty exceeds this, and lasts `duty - Spread`.
    public const float GallopSpread = 1f - (TrailingHindOffset - LeadingForeOffset);

    private readonly float walkDuty;
    private readonly float runDuty;
    private readonly bool leadRight;
    private readonly float walkToTrot;
    private readonly float trotToCanter;

    private float[] walk;
    private float[] trot;
    private float[] gallop;

    /// `walkDuty` and `runDuty` are the fraction of the cycle a foot is airborne at either end of
    /// the speed range. `leadRight` picks the leading foreleg; it is fixed at construction because
    /// a lead change is a table swap by another name, and swapping mid-stride is the one thing this
    /// class exists to avoid. `walkToTrot` and `trotToCanter` are where on the speed range the two
    /// transitions happen, and the plateau between them is the trot.
    public CanterGait(float walkDuty, float runDuty, bool leadRight = true,
                      float walkToTrot = 0.45f, float trotToCanter = 0.55f)
    {
        this.walkDuty = walkDuty;
        this.runDuty = runDuty;
        this.leadRight = leadRight;
        this.walkToTrot = Mathf.Clamp(walkToTrot, 0.05f, 0.95f);
        this.trotToCanter = Mathf.Clamp(trotToCanter, this.walkToTrot, 0.99f);
    }

    public void Bind(IReadOnlyList<LegMeasurement> legs)
    {
        int count = legs.Count;
        walk = new float[count];
        trot = new float[count];
        gallop = new float[count];

        int[] order = RippleOrder(legs);
        Centre(legs, out float midX, out float midZ);

        for (int i = 0; i < count; i++)
        {
            // Up one side and then the other, rear to front. On four legs that is exactly the
            // lateral-sequence walk a horse uses: LH, LF, RH, RF.
            walk[i] = WalkerGait.MetachronalOffset(order[i], count);

            // +X is the machine's own starboard and +Z its nose, both read against the mean
            // foothold rather than against the body origin, which on some rigs sits at a corner.
            bool right = legs[i].HomeLocal.x > midX;
            bool front = legs[i].HomeLocal.z > midZ;

            // One diagonal takes the first half of the cycle, the other the second.
            trot[i] = right != front ? 0f : 0.5f;

            bool leading = right == leadRight;
            gallop[i] = front
                ? (leading ? LeadingForeOffset : TrailingForeOffset)
                : (leading ? LeadingHindOffset : TrailingHindOffset);
        }

        // A gallop table only means anything on four legs. On anything else this degrades to the
        // trot rather than inventing a lead, which is the honest failure: the machine still walks
        // and still trots, it just never breaks into a canter.
        if (count != 4)
        {
            for (int i = 0; i < count; i++) gallop[i] = trot[i];
        }
    }

    public float PhaseOffset(int leg, float runBlend)
    {
        if (walk == null || leg < 0 || leg >= walk.Length) return 0f;
        runBlend = Mathf.Clamp01(runBlend);

        float toTrot = Mathf.Clamp01(runBlend / walkToTrot);
        float toCanter = trotToCanter >= 1f
            ? 0f
            : Mathf.Clamp01((runBlend - trotToCanter) / (1f - trotToCanter));

        // Both stages go through BlendOffset, which travels the SHORT way round: offsets live on a
        // circle, so 0.9 and 0.1 are a fifth of a cycle apart and a plain lerp between them walks
        // a leg backwards through four fifths of the cycle, swinging when nothing asked it to.
        float walking = WalkerGait.BlendOffset(walk[leg], trot[leg], toTrot);
        return WalkerGait.BlendOffset(walking, gallop[leg], toCanter);
    }

    /// Invariant I1, defended at the policy. Both ends are authored above zero and a lerp between
    /// them cannot reach it, bound or not.
    public float Duty(float runBlend)
        => Mathf.Max(0.05f, Mathf.Lerp(walkDuty, runDuty, Mathf.Clamp01(runBlend)));

    /// How long every foot is off the ground at once, as a fraction of the cycle. Zero at anything
    /// below a canter; the defining feature of a gallop above one. Reported rather than used, so a
    /// test can assert the machine actually leaves the ground instead of taking it on trust.
    public float SuspensionFraction(float runBlend)
    {
        if (gallop == null || gallop.Length != 4) return 0f;
        float blended = trotToCanter >= 1f
            ? 0f
            : Mathf.Clamp01((Mathf.Clamp01(runBlend) - trotToCanter) / (1f - trotToCanter));
        // The offsets are spread over half a cycle only once the blend has fully arrived; before
        // that the trot's own half-cycle spread applies, which no duty below 0.5 can clear.
        float spread = Mathf.Lerp(0.5f, GallopSpread, blended);
        return Mathf.Max(0f, Duty(runBlend) - spread);
    }

    /// A quadruped at a walk or a trot has feet down by construction, so the step-early rule only
    /// has to stop a leg re-stepping the instant it lands. At a gallop it is the same rule: a
    /// suspension is the clock's doing, not this one's.
    public bool MayStepEarly(in StepEarlyRequest r)
        => r.Unreachable && r.StanceTime > r.SwingDuration;

    // ─────────── working the machine's shape out from where its feet are ───────────
    //
    // `GaitLayout` in SpaceGame.Locomotion does this for the shipped patterns and is internal to
    // that assembly, so these are its two rules restated. They are three lines each and copying
    // them is cheaper than widening the core's API for one creature -- and the rule that matters,
    // "derive it from HomeLocal, never from a leg index", is the same rule.

    /// Sequence position for each leg: up one side, then the other, rear to front.
    private static int[] RippleOrder(IReadOnlyList<LegMeasurement> legs)
    {
        int count = legs.Count;
        Centre(legs, out float midX, out _);

        var sequence = new List<int>(count);
        for (int i = 0; i < count; i++) sequence.Add(i);

        sequence.Sort((a, b) =>
        {
            int sideA = legs[a].HomeLocal.x < midX ? 0 : 1;
            int sideB = legs[b].HomeLocal.x < midX ? 0 : 1;
            if (sideA != sideB) return sideA.CompareTo(sideB);
            return legs[a].HomeLocal.z.CompareTo(legs[b].HomeLocal.z);
        });

        var order = new int[count];
        for (int i = 0; i < count; i++) order[sequence[i]] = i;
        return order;
    }

    /// Mean foothold, so left/right and front/rear are read against the machine's own centre.
    private static void Centre(IReadOnlyList<LegMeasurement> legs, out float midX, out float midZ)
    {
        midX = 0f;
        midZ = 0f;
        if (legs.Count == 0) return;

        for (int i = 0; i < legs.Count; i++)
        {
            midX += legs[i].HomeLocal.x;
            midZ += legs[i].HomeLocal.z;
        }
        midX /= legs.Count;
        midZ /= legs.Count;
    }
}
