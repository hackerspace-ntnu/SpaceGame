// The two pieces of the neck's autonomous motion, driven directly.
//
// OstrichNeckMotion itself is a MonoBehaviour hanging off a real rig, so what is asserted here is
// the decision-making underneath it: the gaze's snap-and-hold shape, and the spring's stability
// and lag. Both are plain classes inside the Ostrich assembly precisely so this is possible.
using NUnit.Framework;
using UnityEngine;

public class OstrichNeckGazeTests
{
    private const float Dt = 1f / 60f;

    private static OstrichNeckGazeSettings Settings() => OstrichNeckGazeSettings.Default;

    [Test]
    public void SameSeedGivesTheSameSequence()
    {
        var a = new OstrichNeckGaze(Settings(), 1234);
        var b = new OstrichNeckGaze(Settings(), 1234);
        for (int i = 0; i < 600; i++)
        {
            a.Step(Dt, 0f);
            b.Step(Dt, 0f);
        }
        Assert.AreEqual(a.Current.x, b.Current.x, 1e-5f, "a seeded gaze must be reproducible");
        Assert.AreEqual(a.Current.y, b.Current.y, 1e-5f);
    }

    [Test]
    public void DifferentSeedsDoNotScanInLockstep()
    {
        var a = new OstrichNeckGaze(Settings(), 1);
        var b = new OstrichNeckGaze(Settings(), 99);
        float worst = 0f;
        for (int i = 0; i < 600; i++)
        {
            a.Step(Dt, 0f);
            b.Step(Dt, 0f);
            worst = Mathf.Max(worst, Mathf.Abs(a.Current.x - b.Current.x));
        }
        Assert.Greater(worst, 5f, "two birds with different seeds should not look alike");
    }

    [Test]
    public void HeadHoldsStillMostOfTheTime()
    {
        // The point of a saccade is the hold. If the head is in motion most frames it is panning,
        // which is the periscope failure this model exists to avoid.
        var g = new OstrichNeckGaze(Settings(), 7);
        int holding = 0;
        const int frames = 3000;
        for (int i = 0; i < frames; i++)
        {
            g.Step(Dt, 0f);
            if (g.Holding) holding++;
        }
        Assert.Greater(holding / (float)frames, 0.6f,
                       "the head should be holding a heading for most frames, not sweeping");
    }

    [Test]
    public void GazeStaysWithinItsRange()
    {
        var s = Settings();
        var g = new OstrichNeckGaze(s, 42);
        for (int i = 0; i < 5000; i++)
        {
            g.Step(Dt, 0f);
            Assert.LessOrEqual(Mathf.Abs(g.Current.x), s.startleYaw + 1f, "yaw ran past its limit");
            Assert.LessOrEqual(Mathf.Abs(g.Current.y), s.pitchRange + 1f, "pitch ran past its limit");
        }
    }

    [Test]
    public void RunningPullsTheHeadForward()
    {
        var s = Settings();
        var walking = new OstrichNeckGaze(s, 3);
        var running = new OstrichNeckGaze(s, 3);

        float walkSum = 0f, runSum = 0f;
        for (int i = 0; i < 3000; i++)
        {
            walking.Step(Dt, 0f);
            running.Step(Dt, 1f);
            walkSum += Mathf.Abs(walking.Current.x);
            runSum += Mathf.Abs(running.Current.x);
        }
        Assert.Less(runSum, walkSum * 0.6f,
                    "a bird at top speed should look around far less than a standing one");
    }
}

public class OstrichNeckSpringTests
{
    private const float Dt = 1f / 60f;

    private static OstrichNeckSpringSettings Settings() => OstrichNeckSpringSettings.Default;

    [Test]
    public void UndrivenSpringSettlesToZero()
    {
        var s = new OstrichNeckSpring(Settings());
        s.Step(Dt, new Vector3(0f, 30f, 0f));            // one kick
        for (int i = 0; i < 600; i++) s.Step(Dt, Vector3.zero);
        Assert.Less(s.Value.magnitude, 0.05f, "the bounce must die away, not ring forever");
    }

    [Test]
    public void RespondsToVerticalAccelerationAndLagsIt()
    {
        var s = new OstrichNeckSpring(Settings());
        s.Step(Dt, new Vector3(0f, 20f, 0f));
        float afterOneFrame = Mathf.Abs(s.Value.x);

        for (int i = 0; i < 6; i++) s.Step(Dt, new Vector3(0f, 20f, 0f));
        float afterSeven = Mathf.Abs(s.Value.x);

        Assert.Greater(afterSeven, afterOneFrame,
                       "a spring builds over several frames; instant response would mean no lag");
    }

    [Test]
    public void NeverExceedsItsClamp()
    {
        var s = Settings();
        var spring = new OstrichNeckSpring(s);
        for (int i = 0; i < 2000; i++)
        {
            // Shake it far harder than a gait ever would.
            float drive = (i % 7 < 3) ? 400f : -400f;
            spring.Step(Dt, new Vector3(drive, drive, drive));
            Assert.LessOrEqual(spring.Value.magnitude, s.maxAngle + 1e-3f,
                               "the bounce folded the neck past its limit");
        }
    }

    [Test]
    public void SurvivesALongFrameWithoutExploding()
    {
        // A stiff spring integrated over one big dt is exactly where explicit integration blows
        // up and throws the head across the map. The sub-stepping is what stops it.
        var spring = new OstrichNeckSpring(Settings());
        for (int i = 0; i < 200; i++) spring.Step(0.5f, new Vector3(0f, 25f, 0f));

        Assert.IsFalse(float.IsNaN(spring.Value.x) || float.IsInfinity(spring.Value.x),
                       "a long frame must not produce NaN");
        Assert.LessOrEqual(spring.Value.magnitude, Settings().maxAngle + 1e-3f);
    }

    [Test]
    public void ATeleportSpikeDoesNotSlamTheNeckToItsLimit()
    {
        // Snap-to-ground on spawn and scene migration both appear as one frame of effectively
        // infinite acceleration. Before the drive clamp this pinned the neck at maxAngle and rang
        // for a full two seconds afterwards.
        var settings = Settings();
        var spring = new OstrichNeckSpring(settings);

        spring.Step(Dt, new Vector3(0f, 90000f, 0f));         // a teleport's worth of acceleration
        float peak = spring.Value.magnitude;

        for (int i = 0; i < 120; i++) spring.Step(Dt, Vector3.zero);

        Assert.Less(peak, settings.maxAngle,
                    "an unphysical spike should be clamped before it reaches the bend limit");
        Assert.Less(spring.Value.magnitude, 0.5f, "and it should have rung out within two seconds");
    }

    [Test]
    public void ASprintDriveStaysBelowTheBendLimit()
    {
        // Measured on the real rig: a full sprint peaks near 54 m/s^2 vertical. That has to remain
        // expressive rather than sitting pinned against the clamp.
        var settings = Settings();
        var spring = new OstrichNeckSpring(settings);
        float peak = 0f;
        for (int i = 0; i < 600; i++)
        {
            float a = 54f * Mathf.Sin(2f * Mathf.PI * 2.5f * i * Dt);   // ~2.5 Hz stride
            spring.Step(Dt, new Vector3(0f, a, 0f));
            if (i > 60) peak = Mathf.Max(peak, spring.Value.magnitude);
        }
        Assert.Less(peak, settings.maxAngle,
                    "a sprint should not sit against the clamp; it has nowhere left to express");
        Assert.Greater(peak, 1f, "and it should still be clearly visible");
    }

    [Test]
    public void LateralAccelerationRollsTheNeck()
    {
        var spring = new OstrichNeckSpring(Settings());
        for (int i = 0; i < 20; i++) spring.Step(Dt, new Vector3(0f, 0f, 15f));
        Assert.Greater(Mathf.Abs(spring.Value.y), 0.1f, "a sideways shove should roll the neck");
    }
}
