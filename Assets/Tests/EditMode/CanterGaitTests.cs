// The horse's gait ladder, as a policy: no scene, no rig, no prefab.
//
// The expensive failure this guards is a table swapped at a threshold. Offsets are re-read every
// frame, so an offset that JUMPS closes the slice a hoof is mid-swing in and teleports it. Every
// assertion about continuity below is that fault, written down.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

public class CanterGaitTests
{
    // Unity space after the FBX axis conversion: +X is the machine's starboard, +Z its nose.
    private const int LF = 0, RF = 1, LH = 2, RH = 3;

    private static List<LegMeasurement> Quadruped()
        => new List<LegMeasurement>
        {
            Leg(LF, -1f, 1f), Leg(RF, 1f, 1f), Leg(LH, -1f, -1f), Leg(RH, 1f, -1f),
        };

    private static LegMeasurement Leg(int index, float x, float z) => new LegMeasurement
    {
        Index = index,
        HomeLocal = new Vector3(x, 0f, z),
        StrideLength = 1f,
        LegReach = 1f,
    };

    private static CanterGait Bound(bool leadRight = true)
    {
        var gait = new CanterGait(0.38f, 0.62f, leadRight);
        gait.Bind(Quadruped());
        return gait;
    }

    /// Circular distance between two phase offsets, 0..0.5.
    private static float Apart(float a, float b) => Mathf.Abs(Mathf.Repeat(a - b + 0.5f, 1f) - 0.5f);

    // ─────────── the walk ───────────

    /// A horse's walk is four-beat and LATERAL: left hind, left fore, right hind, right fore. Not
    /// diagonal, and not an arbitrary even spread -- the order is what makes it a walk.
    [Test]
    public void WalkIsAFourBeatLateralSequence()
    {
        CanterGait gait = Bound();

        float lh = gait.PhaseOffset(LH, 0f);
        var order = new[]
        {
            Mathf.Repeat(gait.PhaseOffset(LH, 0f) - lh, 1f),
            Mathf.Repeat(gait.PhaseOffset(LF, 0f) - lh, 1f),
            Mathf.Repeat(gait.PhaseOffset(RH, 0f) - lh, 1f),
            Mathf.Repeat(gait.PhaseOffset(RF, 0f) - lh, 1f),
        };

        Assert.AreEqual(0.00f, order[0], 1e-4f, "left hind leads the walk");
        Assert.AreEqual(0.25f, order[1], 1e-4f, "left fore follows it");
        Assert.AreEqual(0.50f, order[2], 1e-4f, "then the right hind");
        Assert.AreEqual(0.75f, order[3], 1e-4f, "then the right fore");
    }

    // ─────────── the trot ───────────

    /// The plateau between the two transitions is a real trot, not a place the blend happens to be
    /// passing through.
    [Test]
    public void TrotPutsDiagonalPairsOnTheSamePhase()
    {
        CanterGait gait = Bound();
        const float atTrot = 0.5f;

        Assert.AreEqual(gait.PhaseOffset(LF, atTrot), gait.PhaseOffset(RH, atTrot), 1e-4f,
                        "front-left and rear-right are a diagonal");
        Assert.AreEqual(gait.PhaseOffset(RF, atTrot), gait.PhaseOffset(LH, atTrot), 1e-4f,
                        "front-right and rear-left are a diagonal");
        Assert.AreEqual(0.5f, Apart(gait.PhaseOffset(LF, atTrot), gait.PhaseOffset(RF, atTrot)),
                        1e-4f, "the two diagonals must be half a cycle apart");
    }

    // ─────────── the gallop ───────────

    /// The three things that make a gallop a gallop and not a fast trot: it is asymmetric, it has a
    /// leading foreleg, and the footfalls bunch into half the cycle so the other half can be a
    /// suspension.
    [Test]
    public void GallopIsAsymmetricWithALeadingForelegAndASuspension()
    {
        CanterGait gait = Bound(leadRight: true);
        const float atGallop = 1f;

        float lf = gait.PhaseOffset(LF, atGallop);
        float rf = gait.PhaseOffset(RF, atGallop);
        float lh = gait.PhaseOffset(LH, atGallop);
        float rh = gait.PhaseOffset(RH, atGallop);

        Assert.Greater(Mathf.Abs(Apart(lf, rh) - 0.5f), 0.1f,
                       "a gallop is asymmetric: the diagonals must have stopped being diagonals");

        // Right lead: the right foreleg is the LAST foot to leave, a fifth of a cycle after the
        // left one, which is what gives the gait its handedness.
        Assert.AreEqual(0.14f, Mathf.Repeat(rf - lf, 1f), 1e-3f,
                        "the leading foreleg follows the trailing one");
        Assert.AreEqual(0.14f, Mathf.Repeat(rh - lh, 1f), 1e-3f,
                        "and the leading hind follows the trailing hind by the same");

        // Sequence, read cyclically from the trailing hind: LH, RH, LF, RF.
        float[] offsets = { Mathf.Repeat(lh - lh, 1f), Mathf.Repeat(rh - lh, 1f),
                            Mathf.Repeat(lf - lh, 1f), Mathf.Repeat(rf - lh, 1f) };
        for (int i = 1; i < offsets.Length; i++)
            Assert.Greater(offsets[i], offsets[i - 1],
                           "the footfall order must be LH, RH, LF, RF");

        Assert.Greater(gait.SuspensionFraction(1f), 0.05f,
                       "a gallop with no suspension is a fast trot");
    }

    /// Swap the lead and the handedness swaps with it, and nothing else does.
    [Test]
    public void TheLeadChoiceMirrorsTheGait()
    {
        CanterGait right = Bound(leadRight: true);
        CanterGait left = Bound(leadRight: false);

        Assert.AreEqual(Mathf.Repeat(right.PhaseOffset(RF, 1f) - right.PhaseOffset(LF, 1f), 1f),
                        Mathf.Repeat(left.PhaseOffset(LF, 1f) - left.PhaseOffset(RF, 1f), 1f),
                        1e-4f, "the two leads must be mirror images");
        Assert.AreEqual(right.SuspensionFraction(1f), left.SuspensionFraction(1f), 1e-4f);
    }

    // ─────────── the rule the whole class exists for ───────────

    /// Offsets are a continuous function of runBlend, over the WHOLE range and not just across one
    /// transition. Swapping a table teleports a hoof that is in the air; blending walks it over.
    [Test]
    public void OffsetsStayContinuousAcrossTheWholeSpeedRange()
    {
        CanterGait gait = Bound();
        const int steps = 400;

        for (int leg = 0; leg < 4; leg++)
        {
            float previous = gait.PhaseOffset(leg, 0f);
            float worst = 0f;
            for (int step = 1; step <= steps; step++)
            {
                float current = gait.PhaseOffset(leg, step / (float)steps);
                worst = Mathf.Max(worst, Apart(current, previous));
                previous = current;
            }
            Assert.Less(worst, 0.02f,
                $"leg {leg} jumped {worst:F4} of a cycle between adjacent speeds");
        }
    }

    /// Duty is continuous too, and never zero: a duty of zero makes the derived top speed zero,
    /// which makes SetTwist clamp every command to zero, which stops a clock advanced by distance
    /// travelled -- and nothing reopens a slice after that. Invariant I1.
    [Test]
    public void DutyIsContinuousAndNeverZero()
    {
        var unbound = new CanterGait(0.38f, 0.62f);
        Assert.Greater(unbound.Duty(0f), 0f, "an unbound pattern must still report a real duty");
        Assert.Greater(unbound.Duty(1f), 0f);

        CanterGait gait = Bound();
        float previous = gait.Duty(0f);
        for (int step = 1; step <= 200; step++)
        {
            float current = gait.Duty(step / 200f);
            Assert.Greater(current, 0f);
            Assert.Less(Mathf.Abs(current - previous), 0.01f);
            previous = current;
        }
    }

    // ─────────── layout, not indices ───────────

    /// Which leg is diagonal to which comes from where the feet ARE. Hand the same four legs in a
    /// different order and the same physical pairs must come out.
    [Test]
    public void PairsAreDerivedFromHomeLocalNotFromLegIndices()
    {
        var shuffled = new List<LegMeasurement>
        {
            Leg(0, 1f, -1f),    // rear-right
            Leg(1, -1f, 1f),    // front-left
            Leg(2, 1f, 1f),     // front-right
            Leg(3, -1f, -1f),   // rear-left
        };
        var gait = new CanterGait(0.38f, 0.62f);
        gait.Bind(shuffled);

        Assert.AreEqual(gait.PhaseOffset(1, 0.5f), gait.PhaseOffset(0, 0.5f), 1e-4f,
                        "front-left and rear-right are still a diagonal after a reorder");
        Assert.AreEqual(gait.PhaseOffset(2, 0.5f), gait.PhaseOffset(3, 0.5f), 1e-4f,
                        "front-right and rear-left are still a diagonal after a reorder");
        Assert.AreEqual(0.14f, Mathf.Repeat(gait.PhaseOffset(2, 1f) - gait.PhaseOffset(1, 1f), 1f),
                        1e-3f, "and the lead is still the right foreleg");
    }

    /// A gallop table only means anything on four legs. On anything else it degrades to the trot
    /// rather than inventing a lead -- the honest failure, and one that still walks and trots.
    [Test]
    public void ALeadIsNotInventedForAMachineThatIsNotAQuadruped()
    {
        var hexapod = new List<LegMeasurement>();
        for (int i = 0; i < 6; i++)
            hexapod.Add(Leg(i, i < 3 ? -1f : 1f, (i % 3) - 1f));

        var gait = new CanterGait(0.38f, 0.62f);
        gait.Bind(hexapod);

        for (int i = 0; i < 6; i++)
            Assert.AreEqual(gait.PhaseOffset(i, 0.5f), gait.PhaseOffset(i, 1f), 1e-4f,
                            "a six-legged machine must top out at the trot, not at a gallop");
        Assert.AreEqual(0f, gait.SuspensionFraction(1f), 1e-4f);
    }

    /// Nothing may gate motion on its own output. A step-early rule that could fire every frame
    /// pulls the legs into step with each other and the machine hops.
    [Test]
    public void StepEarlyIsRefusedForALegThatHasJustLanded()
    {
        CanterGait gait = Bound();

        Assert.IsFalse(gait.MayStepEarly(new StepEarlyRequest
        {
            Unreachable = true, StanceTime = 0.05f, SwingDuration = 0.3f,
            PlantedCount = 3, LegCount = 4,
        }), "a leg that landed a frame ago must not be allowed to re-step");

        Assert.IsTrue(gait.MayStepEarly(new StepEarlyRequest
        {
            Unreachable = true, StanceTime = 0.9f, SwingDuration = 0.3f,
            PlantedCount = 3, LegCount = 4,
        }), "a leg genuinely stranded past its reach must be able to step");

        Assert.IsFalse(gait.MayStepEarly(new StepEarlyRequest
        {
            Unreachable = false, StanceTime = 9f, SwingDuration = 0.3f,
            PlantedCount = 3, LegCount = 4,
        }), "a leg that can reach its foothold has no business stepping early");
    }
}
