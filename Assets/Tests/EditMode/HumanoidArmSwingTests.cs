// The arms and the torso: the two things on this machine that are driven by the gait clock without
// being part of the gait.
//
// `LeggedLocomotion` discovers an `Arm_` limb, measures it, gives it a solver -- and then
// deliberately never touches it. `SolveArms` is not part of `Step`, because an arm's target belongs
// to neither the gait nor the body. So everything below is about the seam holding: an arm that is
// posed but never walked on, in step with the feet at any speed, and never asked to reach further
// than it can.
//
// THE DRIFT TEST IS THE POINT. A counter-swing driven off its own timer looks right for ten seconds
// and then slides out of step with the footfalls and back again, and that is exactly the bug a
// video does not show you. So what is asserted is not "the arm swings" but "the phase of the swing
// against the gait clock is the SAME at two very different speeds".
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;

public class HumanoidArmSwingTests
{
    private const string PrefabPath = "Assets/Prefabs/agents/creatures/HumanoidRobot.prefab";
    private const float Dt = 1f / 60f;
    private const int Settle = 200;
    private const int Sample = 300;

    private GameObject ground;
    private GameObject machine;
    private HumanoidLocomotion loco;
    private HumanoidArmSwing arms;
    private HumanoidSpineMotion spine;

    [SetUp]
    public void SetUp()
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "TestGround";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(900f, 1f, 900f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "Humanoid prefab missing at " + PrefabPath);

        machine = Object.Instantiate(prefab);
        machine.transform.position = new Vector3(0f, 0.3f, 0f);
        // The driver is disabled by TYPE rather than by class: it lives in Assembly-CSharp, which
        // no asmdef -- including this one -- may reference.
        foreach (MonoBehaviour mb in machine.GetComponents<MonoBehaviour>())
            if (!(mb is LeggedLocomotion) && !(mb is HumanoidArmSwing) &&
                !(mb is HumanoidSpineMotion)) mb.enabled = false;
        Physics.SyncTransforms();

        loco = machine.GetComponent<HumanoidLocomotion>();
        arms = machine.GetComponent<HumanoidArmSwing>();
        spine = machine.GetComponent<HumanoidSpineMotion>();

        loco.Initialise();
        loco.SnapToGround();
        arms.Bind();
        spine.Resolve();
    }

    [TearDown]
    public void TearDown()
    {
        if (machine != null) Object.DestroyImmediate(machine);
        if (ground != null) Object.DestroyImmediate(ground);
        machine = null;
        ground = null;
    }

    /// One frame of the whole machine. EditMode never runs LateUpdate, so the execution order the
    /// components declare is reproduced here by hand: locomotion (100), arms (120), spine (150).
    private void Tick(float speedFraction, float yawRate = 0f)
    {
        loco.SetTwist(loco.MaxSpeed * speedFraction, yawRate);
        loco.Step(Dt);
        arms.Step(Dt);
        spine.Step(Dt);
        Physics.SyncTransforms();
    }

    private int LeftLeg()
    {
        int best = 0;
        float bestX = float.MaxValue;
        for (int i = 0; i < loco.LegCount; i++)
            if (loco.LegHomeLocal(i).x < bestX) { bestX = loco.LegHomeLocal(i).x; best = i; }
        return best;
    }

    private WalkerArm LeftArm()
    {
        WalkerArm best = loco.Arms[0];
        float bestX = float.MaxValue;
        foreach (WalkerArm a in loco.Arms)
        {
            float x = machine.transform.InverseTransformPoint(a.Rig.Anchor.position).x;
            if (x < bestX) { bestX = x; best = a; }
        }
        return best;
    }

    private float Fore(Vector3 world) => machine.transform.InverseTransformPoint(world).z;

    // ─────────── the seam ───────────

    [Test]
    public void TheArmsAreDiscoveredAndSizedFromTheRig()
    {
        Assert.AreEqual(2, loco.ArmCount);
        Assert.Greater(arms.SwingAngle, 1f,
            "the swing came out at " + arms.SwingAngle.ToString("F3") +
            " degrees; it is derived from the leg's stride, so this near zero means the stride is");
        Assert.Less(arms.SwingAngle, 45f, "the swing is implausibly large");
    }

    /// An arc swing keeps the hand at a constant distance from the shoulder, so the reach fraction
    /// is whatever it is at rest and no swing can over-extend the arm. If this ever goes above 1
    /// the hand has come off its target and the swing has stopped being an arc.
    [Test]
    public void AnArmIsNeverAskedToReachFurtherThanItCan()
    {
        float worst = 0f;
        for (int i = 0; i < Settle + Sample; i++)
        {
            Tick(i < Settle ? 0.3f : 0.9f);
            if (i > 60) worst = Mathf.Max(worst, arms.WorstArmReach());
        }
        Assert.LessOrEqual(worst, 1f,
            "worst arm reach fraction was " + worst.ToString("F4"));
    }

    /// Not walked on means exactly that: the arms carry no gait slot and no foothold, so the leg
    /// count stays 2 however far the machine walks.
    [Test]
    public void TheArmsAreNeverWalkedOn()
    {
        for (int i = 0; i < 300; i++) Tick(0.5f);
        Assert.AreEqual(2, loco.LegCount);
        Assert.AreEqual(2, loco.ArmCount);
        Assert.LessOrEqual(loco.LastFrame.StanceLegs, 2,
            "something other than the two legs is being counted as stance");
    }

    // ─────────── phase ───────────

    /// The gait phase at which the left hand is furthest FORWARD, over one sampling window.
    private float PeakHandPhase(float speedFraction)
    {
        for (int i = 0; i < Settle; i++) Tick(speedFraction);

        WalkerArm arm = LeftArm();
        float bestFore = float.MinValue;
        float bestPhase = 0f;
        for (int i = 0; i < Sample; i++)
        {
            Tick(speedFraction);
            float fore = Fore(arm.Tip);
            if (fore > bestFore) { bestFore = fore; bestPhase = loco.LastFrame.Phase; }
        }
        return bestPhase;
    }

    /// The acceptance measurement for the counter-swing. The hand's position is a pure function of
    /// the gait clock, so the phase it peaks at must not move when the speed does -- and a swing
    /// driven off a timer of its own is exactly what would move it.
    [Test]
    public void TheArmsStayInPhaseWithTheGaitAcrossASpeedChange()
    {
        float slow = PeakHandPhase(0.3f);
        TearDown();
        SetUp();
        float fast = PeakHandPhase(0.9f);

        float drift = Mathf.Abs(Mathf.DeltaAngle(slow * 360f, fast * 360f)) / 360f;
        Assert.Less(drift, 0.05f,
            "the hand peaked at phase " + slow.ToString("F3") + " at a walk and " +
            fast.ToString("F3") + " at a run -- " + drift.ToString("F3") +
            " of a cycle of drift; the swing is not locked to the clock");
    }

    /// The counter-swing itself: the left hand goes forward with the RIGHT foot. Measured at the
    /// extremes only, because near the crossings both signals are passing through zero and their
    /// signs say nothing.
    [Test]
    public void EachArmSwingsWithItsDiagonalLeg()
    {
        int left = LeftLeg();
        int right = 1 - left;
        WalkerArm arm = LeftArm();

        for (int i = 0; i < Settle; i++) Tick(0.5f);

        // Amplitudes first, so "the extremes" is measured rather than guessed.
        float handMin = float.MaxValue, handMax = float.MinValue;
        float footMin = float.MaxValue, footMax = float.MinValue;
        for (int i = 0; i < Sample; i++)
        {
            Tick(0.5f);
            float h = Fore(arm.Tip);
            loco.TryGetFoot(right, out Vector3 foot, out _);
            float f = Fore(foot);
            handMin = Mathf.Min(handMin, h); handMax = Mathf.Max(handMax, h);
            footMin = Mathf.Min(footMin, f); footMax = Mathf.Max(footMax, f);
        }

        Assert.Greater(handMax - handMin, 0.01f,
            "the hand barely moved (" + (handMax - handMin).ToString("F4") + " m); nothing to test");

        float handMid = (handMin + handMax) * 0.5f;
        float footMid = (footMin + footMax) * 0.5f;
        float handSpan = (handMax - handMin) * 0.5f;
        float footSpan = (footMax - footMin) * 0.5f;

        int agreed = 0, disagreed = 0;
        for (int i = 0; i < Sample; i++)
        {
            Tick(0.5f);
            float h = (Fore(arm.Tip) - handMid) / handSpan;
            loco.TryGetFoot(right, out Vector3 foot, out _);
            float f = (Fore(foot) - footMid) / footSpan;
            if (Mathf.Abs(h) < 0.6f || Mathf.Abs(f) < 0.6f) continue;   // near a crossing
            if (Mathf.Sign(h) == Mathf.Sign(f)) agreed++; else disagreed++;
        }

        Assert.Greater(agreed, 0, "no frames at the extremes; the sampling window is too short");
        Assert.AreEqual(0, disagreed,
            "on " + disagreed + " of " + (agreed + disagreed) +
            " frames the left hand was at one extreme while the RIGHT foot was at the other; the " +
            "arm is swinging with its own leg instead of its diagonal");
    }

    // ─────────── the torso ───────────

    /// The chest turns against the pelvis: when the left leg is behind the machine, the left
    /// shoulder is in front of it. A positive yaw about the body's up axis brings the left shoulder
    /// forward, so the two must have opposite signs at the extremes.
    [Test]
    public void TheTorsoCounterRotatesAgainstTheLegs()
    {
        int left = LeftLeg();
        for (int i = 0; i < Settle; i++) Tick(0.5f);

        float footMin = float.MaxValue, footMax = float.MinValue, yawPeak = 0f;
        for (int i = 0; i < Sample; i++)
        {
            Tick(0.5f);
            loco.TryGetFoot(left, out Vector3 foot, out _);
            float f = Fore(foot);
            footMin = Mathf.Min(footMin, f); footMax = Mathf.Max(footMax, f);
            yawPeak = Mathf.Max(yawPeak, Mathf.Abs(spine.ChestYaw));
        }

        Assert.Greater(yawPeak, 0.5f,
            "the chest never turned; peak yaw was " + yawPeak.ToString("F3") + " degrees");
        Assert.Less(yawPeak, 20f, "the chest is wringing itself out: " + yawPeak.ToString("F3"));

        float mid = (footMin + footMax) * 0.5f;
        float span = (footMax - footMin) * 0.5f;
        int agreed = 0, disagreed = 0;
        for (int i = 0; i < Sample; i++)
        {
            Tick(0.5f);
            loco.TryGetFoot(left, out Vector3 foot, out _);
            float f = (Fore(foot) - mid) / span;
            float yaw = spine.ChestYaw / Mathf.Max(yawPeak, 1e-4f);
            if (Mathf.Abs(f) < 0.6f || Mathf.Abs(yaw) < 0.6f) continue;
            if (Mathf.Sign(f) != Mathf.Sign(yaw)) agreed++; else disagreed++;
        }

        Assert.Greater(agreed, 0, "no frames at the extremes; the sampling window is too short");
        Assert.AreEqual(0, disagreed,
            "on " + disagreed + " of " + (agreed + disagreed) +
            " frames the chest turned WITH the left leg rather than against it");
    }

    /// A machine standing still holds its arms and its torso where they were modelled. The envelope
    /// is what does this, and it is the one place a filter is legitimate here.
    [Test]
    public void StandingStillTheArmsAndTorsoComeToRest()
    {
        for (int i = 0; i < 200; i++) Tick(0.6f);
        for (int i = 0; i < 400; i++) Tick(0f);

        WalkerArm arm = LeftArm();
        float first = Fore(arm.Tip);
        float travel = 0f;
        for (int i = 0; i < 120; i++)
        {
            Tick(0f);
            travel = Mathf.Max(travel, Mathf.Abs(Fore(arm.Tip) - first));
        }

        Assert.Less(travel, 0.005f,
            "the arms are still swinging while the machine stands still: " +
            travel.ToString("F5") + " m");
        Assert.Less(Mathf.Abs(spine.ChestYaw), 0.5f,
            "the chest is still turning while the machine stands still: " +
            spine.ChestYaw.ToString("F3") + " degrees");
    }
}
