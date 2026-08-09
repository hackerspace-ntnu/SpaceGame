using NUnit.Framework;
using SpaceGame.Walker;
using UnityEngine;

public class WalkerGaitTests
{
    private const int LegCount = 6;

    private static float[] MetachronalOffsets()
    {
        var offsets = new float[LegCount];
        for (int i = 0; i < LegCount; i++)
            offsets[i] = WalkerGait.MetachronalOffset(i, LegCount);
        return offsets;
    }

    private static int SwingingAt(float phase, float duty)
    {
        int n = 0;
        foreach (float offset in MetachronalOffsets())
            if (WalkerGait.IsSwinging(phase, offset, duty, out _)) n++;
        return n;
    }

    // ─────────── support is arithmetic, not negotiation ───────────

    [Test]
    public void RippleGait_KeepsExactlyOneLegInTheAir_AtEveryPointInTheCycle()
    {
        // The old gait decided who could step from urgency scores and a minimum-planted count,
        // which could deadlock: every leg overdue, none allowed to lift, and the hull frozen.
        // With a phase table the support guarantee is a property of the arithmetic instead.
        float duty = WalkerGait.SwingDuty(1, LegCount);

        for (float phase = 0f; phase < 1f; phase += 0.0013f)
            Assert.AreEqual(1, SwingingAt(phase, duty), $"at phase {phase}");
    }

    [Test]
    public void TripodGait_KeepsExactlyThreeLegsInTheAir()
    {
        float duty = WalkerGait.SwingDuty(3, LegCount);

        for (float phase = 0f; phase < 1f; phase += 0.0013f)
            Assert.AreEqual(3, SwingingAt(phase, duty), $"at phase {phase}");
    }

    [Test]
    public void EveryLegGetsEqualTimeInTheAir()
    {
        float duty = WalkerGait.SwingDuty(1, LegCount);
        float[] offsets = MetachronalOffsets();
        var samples = new int[LegCount];

        for (float phase = 0f; phase < 1f; phase += 0.0005f)
            for (int i = 0; i < LegCount; i++)
                if (WalkerGait.IsSwinging(phase, offsets[i], duty, out _)) samples[i]++;

        for (int i = 1; i < LegCount; i++)
            Assert.AreEqual(samples[0], samples[i], 3, $"leg {i} steps for a different length of time");
    }

    // ─────────── the clock runs on distance, not time ───────────

    [Test]
    public void StationaryWalker_DoesNotStep()
    {
        var gait = new WalkerGait();
        float duty = WalkerGait.SwingDuty(1, LegCount);
        int before = SwingingAt(gait.Phase, duty);

        for (int i = 0; i < 200; i++) gait.Advance(0f, 10f);

        Assert.AreEqual(0f, gait.Phase, 1e-6f, "the phase must not creep while the hull is still");
        Assert.AreEqual(before, SwingingAt(gait.Phase, duty));
    }

    /// Distance between two phases around the cycle. Phase is an angle, so 0.9999 and 0.0001 are
    /// neighbours, not opposites -- comparing them linearly would fail on the wrap.
    private static float PhaseDistance(float a, float b)
    {
        float d = Mathf.Abs(Mathf.Repeat(a - b, 1f));
        return Mathf.Min(d, 1f - d);
    }

    [Test]
    public void Phase_AdvancesOneFullCyclePerStride()
    {
        var gait = new WalkerGait();
        const float stride = 12.5f;

        for (int i = 0; i < 100; i++) gait.Advance(stride / 100f, stride);
        Assert.Less(PhaseDistance(gait.Phase, 0f), 1e-3f, "one stride is exactly one cycle");

        gait.Phase = 0f;
        gait.Advance(stride * 0.25f, stride);
        Assert.AreEqual(0.25f, gait.Phase, 1e-5f, "and a quarter stride is a quarter cycle");
    }

    [Test]
    public void SlowWalker_TakesProportionallySlowerSteps()
    {
        const float stride = 12f;
        var fast = new WalkerGait();
        var slow = new WalkerGait();

        fast.Advance(4f * 0.1f, stride);   // 4 units/s for 0.1s
        slow.Advance(1f * 0.1f, stride);   // 1 unit/s for the same 0.1s

        Assert.AreEqual(4f, fast.Phase / slow.Phase, 1e-3f,
            "cadence must scale with speed, or the legs cycle on the spot");
    }

    // ─────────── top speed is derived, not chosen ───────────

    [Test]
    public void MaxSpeed_IsTheSpeedAtWhichAFootExactlyCoversItsStrideDuringStance()
    {
        const float stride = 12.96f;
        const float stepDuration = 0.45f;
        float duty = WalkerGait.SwingDuty(1, LegCount);

        float maxSpeed = WalkerGait.MaxSpeed(stride, stepDuration, duty);

        // One cycle is stepDuration / duty; stance is the rest of it. Travelling at maxSpeed for
        // that long must drag the foot back through exactly one stride -- no more, or it slides.
        float cycle = stepDuration / duty;
        float stance = cycle * (1f - duty);
        Assert.AreEqual(stride, maxSpeed * stance, 1e-3f);
    }

    [Test]
    public void MaxSpeed_RisesWithStrideAndFallsWithStepDuration()
    {
        float duty = WalkerGait.SwingDuty(1, LegCount);
        float baseline = WalkerGait.MaxSpeed(12f, 0.45f, duty);

        Assert.Greater(WalkerGait.MaxSpeed(18f, 0.45f, duty), baseline, "a longer stride goes faster");
        Assert.Less(WalkerGait.MaxSpeed(12f, 0.9f, duty), baseline, "slower steps go slower");
        Assert.Greater(WalkerGait.MaxSpeed(12f, 0.45f, WalkerGait.SwingDuty(3, LegCount)), baseline,
            "a tripod lifts three at a time and so covers ground faster");
    }

    [Test]
    public void MaxSpeed_IsZero_WhenNoLegIsEverPlanted()
    {
        Assert.AreEqual(0f, WalkerGait.MaxSpeed(12f, 0.45f, 1f), 1e-6f);
    }

    // ─────────── footholds ───────────

    [Test]
    public void Foothold_CentresTheFootsExcursionOnItsRestPosition()
    {
        // The property that keeps a leg off its stops: over a full cycle the foot should sit as
        // far ahead of home at touchdown as it does behind at lift-off.
        Vector3 home = Vector3.zero;
        Vector3 drift = new Vector3(0f, 0f, 4f);
        const float stance = 2f;
        const float swing = 0.5f;

        Vector3 target = WalkerGait.Foothold(home, drift, stance, swing, 100f);

        // Home travels with the hull, so by touchdown it has moved on by drift * swing.
        Vector3 homeAtTouchdown = home + drift * swing;
        Vector3 aheadAtTouchdown = target - homeAtTouchdown;

        // ...and by lift-off, a whole stance later.
        Vector3 homeAtLiftOff = homeAtTouchdown + drift * stance;
        Vector3 behindAtLiftOff = homeAtLiftOff - target;

        Assert.AreEqual(aheadAtTouchdown.magnitude, behindAtLiftOff.magnitude, 1e-4f,
            "the foot must be as far ahead when it lands as it is behind when it lifts");
    }

    [Test]
    public void Foothold_AimsAtWhereHomeWillBe_NotWhereItIs()
    {
        // Regression: the foothold is committed at lift-off but the foot lands a swing later, by
        // which time home has moved. Ignoring that lands every step short by drift * swing, and
        // the leg finishes each stance pinned against its yaw limit.
        Vector3 drift = new Vector3(0f, 0f, 4f);

        Vector3 withSwing = WalkerGait.Foothold(Vector3.zero, drift, 2f, 0.5f, 100f);
        Vector3 withoutSwing = WalkerGait.Foothold(Vector3.zero, drift, 2f, 0f, 100f);

        Assert.AreEqual(2f, (withSwing - withoutSwing).z, 1e-4f, "= drift * swingDuration");
    }

    [Test]
    public void Foothold_ScalesWithEachLegsOwnDrift_NotAFixedFractionOfTheStride()
    {
        // The legs on the inside of a turn have their translation and rotation partly cancel, so
        // they barely drift. Leading those by the same amount as the outside legs plants them far
        // from home with nothing to carry them back, and they sit against their yaw stop all cycle.
        float outLead = WalkerGait.Foothold(
            Vector3.zero, new Vector3(0f, 0f, 6f), 2f, 0f, 100f).magnitude;
        float inLead = WalkerGait.Foothold(
            Vector3.zero, new Vector3(0f, 0f, 0.5f), 2f, 0f, 100f).magnitude;

        Assert.AreEqual(12f, outLead / inLead, 1e-3f, "the lead must track the drift proportionally");
    }

    [Test]
    public void Foothold_NeverAimsBeyondTheLegsArc()
    {
        Vector3 target = WalkerGait.Foothold(Vector3.zero, new Vector3(0f, 0f, 500f), 10f, 0f, 12f);
        Assert.AreEqual(6f, target.magnitude, 1e-4f, "clamped to half a stride");
    }

    [Test]
    public void Foothold_StaysHome_WhenTheHullIsNotMoving()
    {
        Vector3 home = new Vector3(3f, 0f, 9f);
        Assert.AreEqual(home, WalkerGait.Foothold(home, Vector3.zero, 2f, 0.5f, 10f));
    }

    [Test]
    public void SwingAndStance_TogetherMakeOneCycle()
    {
        const float stride = 12f;
        const float pace = 5f;
        float duty = WalkerGait.SwingDuty(1, LegCount);

        float stance = WalkerGait.StanceDuration(pace, stride);
        float swing = WalkerGait.SwingDuration(pace, stride, duty);

        Assert.AreEqual(duty, swing / (swing + stance), 1e-4f,
            "the swing must be exactly the duty share of the cycle");
    }

    [Test]
    public void StanceDuration_IsTheTimeToDragAFootThroughOneStride()
    {
        const float stride = 12f;
        const float stepDuration = 0.45f;
        float duty = WalkerGait.SwingDuty(1, LegCount);
        float maxSpeed = WalkerGait.MaxSpeed(stride, stepDuration, duty);

        // At full speed the cycle is stepDuration per swing slot, and stance is the rest of it.
        float cycle = stepDuration / duty;
        Assert.AreEqual(cycle * (1f - duty), WalkerGait.StanceDuration(maxSpeed, stride), 1e-3f);

        // Half the pace, twice as long on the ground.
        Assert.AreEqual(2f * WalkerGait.StanceDuration(maxSpeed, stride),
                        WalkerGait.StanceDuration(maxSpeed * 0.5f, stride), 1e-3f);
    }

    [Test]
    public void StanceDuration_IsZero_WhenTheHullIsStill()
    {
        Assert.AreEqual(0f, WalkerGait.StanceDuration(0f, 12f), 1e-6f);
    }

    [Test]
    public void CycleDistance_ExceedsTheStride_BecauseTheHullKeepsMovingDuringTheSwing()
    {
        // These two are easy to conflate and the bug is silent when you do: the clock runs on how
        // far the HULL travels per cycle, while the stride is only how far a FOOT travels while
        // it is on the ground. Feeding the stride to the clock makes stepDuration a lie.
        const float stride = 12f;
        float duty = WalkerGait.SwingDuty(1, LegCount);

        float cycle = WalkerGait.CycleDistance(stride, duty);
        Assert.AreEqual(stride / (1f - duty), cycle, 1e-4f);
        Assert.Greater(cycle, stride);
    }

    [Test]
    public void OneSwingLastsExactlyTheAuthoredStepDuration_AtFullSpeed()
    {
        // The property that makes stepDuration mean what its tooltip says.
        const float stride = 12f;
        const float stepDuration = 0.45f;
        float duty = WalkerGait.SwingDuty(1, LegCount);

        float maxSpeed = WalkerGait.MaxSpeed(stride, stepDuration, duty);
        float cycleTime = WalkerGait.CycleDistance(stride, duty) / maxSpeed;

        Assert.AreEqual(stepDuration, cycleTime * duty, 1e-4f);
    }

    [Test]
    public void FootDrift_PureYaw_PushesOppositeSidesOppositeWays()
    {
        // Turning needs no special case: the drift term already carries the hull's rotation, so a
        // leg on the outside of a turn is handed a longer stride than one on the inside.
        Vector3 port = WalkerGait.FootDrift(
            new Vector3(6f, 0f, 5f), Vector3.zero, 0.5f, Vector3.up);
        Vector3 starboard = WalkerGait.FootDrift(
            new Vector3(-6f, 0f, 5f), Vector3.zero, 0.5f, Vector3.up);

        Assert.AreNotEqual(Mathf.Sign(port.z), Mathf.Sign(starboard.z));
    }

    [Test]
    public void FootDrift_StationaryHull_DragsNoFoot()
    {
        Vector3 drift = WalkerGait.FootDrift(
            new Vector3(6f, 0f, 5f), Vector3.zero, 0f, Vector3.up);
        Assert.Less(drift.magnitude, 1e-6f);
    }

    // ─────────── the swing arc ───────────

    [Test]
    public void SwingPoint_StartsAtLiftOff_EndsAtTouchDown_AndClearsTheGroundBetween()
    {
        Vector3 from = new Vector3(0f, 0f, 0f);
        Vector3 to = new Vector3(4f, 0f, 0f);

        Assert.Less(Vector3.Distance(WalkerGait.SwingPoint(from, to, 0f, Vector3.up, 2f), from), 1e-4f);
        Assert.Less(Vector3.Distance(WalkerGait.SwingPoint(from, to, 1f, Vector3.up, 2f), to), 1e-4f);
        Assert.AreEqual(2f, WalkerGait.SwingPoint(from, to, 0.5f, Vector3.up, 2f).y, 1e-4f);
    }

    [Test]
    public void SwingPoint_EasesInAndOut_SoTheFootDoesNotStabAtTheGround()
    {
        Vector3 from = Vector3.zero;
        Vector3 to = new Vector3(10f, 0f, 0f);

        float early = WalkerGait.SwingPoint(from, to, 0.1f, Vector3.up, 0f).x;
        float middle = WalkerGait.SwingPoint(from, to, 0.5f, Vector3.up, 0f).x;
        float late = WalkerGait.SwingPoint(from, to, 0.9f, Vector3.up, 0f).x;

        Assert.AreEqual(5f, middle, 1e-4f);
        Assert.Less(early, 1f, "the foot should still be leaving the ground");
        Assert.Greater(late, 9f, "and settling onto it");
    }

    [Test]
    public void IsSwinging_ReportsProgressAcrossTheWholeSwing()
    {
        float duty = WalkerGait.SwingDuty(1, LegCount);

        Assert.IsTrue(WalkerGait.IsSwinging(0f, 0f, duty, out float atStart));
        Assert.AreEqual(0f, atStart, 1e-4f);

        Assert.IsTrue(WalkerGait.IsSwinging(duty * 0.5f, 0f, duty, out float halfway));
        Assert.AreEqual(0.5f, halfway, 1e-3f);

        Assert.IsFalse(WalkerGait.IsSwinging(duty, 0f, duty, out _), "the swing ends when the slice does");
    }
}
