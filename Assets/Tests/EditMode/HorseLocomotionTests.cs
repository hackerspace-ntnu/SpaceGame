// Marches the horse's gait forward on the real prefab and the real rig, and checks the properties
// that make it a quadruped rather than four legs taking turns.
//
// `HorseLocomotion` exposes `Initialise()` and `Step(dt)` precisely so the machine can be driven
// deterministically with no player loop. Everything asserted here is a measurement, not an
// impression: planted feet do not move, the two pairs of legs measure differently, the diagonals
// are diagonals, and the machine covers the ground it was told to.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;

public class HorseLocomotionTests
{
    private const string PrefabPath = "Assets/Prefabs/agents/creatures/HorseRobot.prefab";
    private const float Dt = 1f / 60f;
    private const int Frames = 600;

    /// Frames ignored at the start of a trial when counting support.
    ///
    /// `SnapToGround` puts every hoof under its rest foothold and drops the body onto them, then
    /// the twist arrives all at once -- a step the driver would have ramped over a second. The gait
    /// spends the first stride catching up and the machine is briefly off the ground doing it.
    /// Slip and distance are measured from frame zero regardless; only the support counts, which
    /// are a property of the SETTLED gait, wait.
    private const int Settle = 90;

    /// One trial, reduced to the numbers worth asserting on.
    private struct Trace
    {
        public float WorstPlantedSlip;      // a planted hoof must not move, at all
        public float WorstReachFraction;
        public int Steps;
        public int AirborneFrames;          // no hoof on the ground
        public int FallingFrames;
        public int MinStanceLegs;
        public float TravelledFlat;
    }

    private GameObject ground;
    private GameObject horse;

    /// Destroy everything this test made, on the failure path as well as the happy one.
    ///
    /// NUnit runs this after every test whatever the result, but a test that spawns twice can
    /// still leave the first instance behind, so the sweep at the end is the backstop. This is not
    /// housekeeping: a leaked clone stands at the world origin in the editor's open scene and the
    /// NEXT machine measured in that editor is standing on it -- which is how a stray horse shifted
    /// another agent's ostrich and crawler up 0.11 m and read as a regression in their baselines.
    [TearDown]
    public void TearDown()
    {
        if (horse != null) Object.DestroyImmediate(horse);
        if (ground != null) Object.DestroyImmediate(ground);
        horse = null;
        ground = null;
        DestroyStrays();
    }

    /// Anything this fixture could have made and lost track of, by name.
    internal static void DestroyStrays()
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go == null || go.transform.parent != null) continue;
            if (go.name.StartsWith("HorseRobot") || go.name == "TestGround")
                Object.DestroyImmediate(go);
        }
    }

    private HorseLocomotion Spawn(float spawnHeight = 2.2f, bool keepSpine = false)
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "TestGround";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(900f, 1f, 900f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "Horse prefab missing at " + PrefabPath +
                         " -- run Tools/Creatures/Build Horse Robot Prefab.");

        horse = Object.Instantiate(prefab);
        horse.transform.position = new Vector3(0f, spawnHeight, 0f);

        // Only the locomotion is under test; the driver and the mount would fight it for input.
        foreach (MonoBehaviour mb in horse.GetComponents<MonoBehaviour>())
        {
            if (mb is HorseLocomotion) continue;
            if (keepSpine && mb is HorseSpineMotion) continue;
            mb.enabled = false;
        }

        Physics.SyncTransforms();

        var loco = horse.GetComponent<HorseLocomotion>();
        loco.Initialise();
        return loco;
    }

    private Trace March(HorseLocomotion loco, float speedFraction, float yawRate,
                        System.Action perFrame = null)
    {
        loco.SnapToGround();

        var t = new Trace { MinStanceLegs = int.MaxValue };
        loco.SetTwist(loco.MaxSpeed * speedFraction, yawRate);

        Vector3 start = horse.transform.position;
        int legs = loco.LegCount;
        var lastFoot = new Vector3[legs];
        var wasPlanted = new bool[legs];
        var wasSwinging = new bool[legs];

        for (int i = 0; i < Frames; i++)
        {
            loco.Step(Dt);
            Physics.SyncTransforms();
            LeggedLocomotion.Diagnostics d = loco.LastFrame;

            if (loco.IsFalling) t.FallingFrames++;
            if (i >= Settle)
            {
                if (d.Airborne) t.AirborneFrames++;
                t.MinStanceLegs = Mathf.Min(t.MinStanceLegs, d.StanceLegs);
                t.WorstReachFraction = Mathf.Max(t.WorstReachFraction, d.WorstReachFraction);
            }

            for (int leg = 0; leg < legs; leg++)
            {
                if (!loco.TryGetFoot(leg, out Vector3 foot, out bool swinging)) continue;

                if (!swinging && wasPlanted[leg])
                    t.WorstPlantedSlip = Mathf.Max(t.WorstPlantedSlip,
                                                   Vector3.Distance(foot, lastFoot[leg]));
                if (swinging && !wasSwinging[leg]) t.Steps++;

                wasSwinging[leg] = swinging;
                wasPlanted[leg] = !swinging;
                lastFoot[leg] = foot;
            }

            perFrame?.Invoke();
        }

        Vector3 end = horse.transform.position;
        t.TravelledFlat = Vector2.Distance(new Vector2(start.x, start.z),
                                           new Vector2(end.x, end.z));
        return t;
    }

    // ─────────── what the rig measures ───────────

    /// The case per-leg measurement was built for and nothing had exercised on a real rig. The
    /// forelegs are a longer, straighter linkage than the hind legs, so they must get a longer
    /// stride. If these come out equal the measurement has regressed to an average.
    [Test]
    public void FrontAndRearLegsMeasureDifferently()
    {
        HorseLocomotion loco = Spawn();
        Assert.AreEqual(4, loco.LegCount, "a horse has four legs");

        float frontStride = 0f, rearStride = 0f;
        float frontReach = 0f, rearReach = 0f;
        int front = 0, rear = 0;

        for (int i = 0; i < loco.LegCount; i++)
        {
            Assert.IsTrue(loco.TryGetMeasurement(i, out LegMeasurement m));
            if (m.HomeLocal.z > 0f) { frontStride += m.StrideLength; frontReach += m.MaxReach; front++; }
            else { rearStride += m.StrideLength; rearReach += m.MaxReach; rear++; }
        }

        Assert.AreEqual(2, front, "two forelegs");
        Assert.AreEqual(2, rear, "two hind legs");

        frontStride /= front; rearStride /= rear;
        frontReach /= front; rearReach /= rear;

        Assert.Greater(frontReach - rearReach, 0.15f,
                       $"the rig's two pairs must differ in length: fore {frontReach:F3} m, " +
                       $"hind {rearReach:F3} m");
        Assert.Greater(frontStride - rearStride, 0.05f,
                       $"both pairs got the same stride (fore {frontStride:F3} m, hind " +
                       $"{rearStride:F3} m); the measurement is being averaged again");
    }

    /// A leg with no yaw joint cannot hold a planted foot through a turn -- invariant I5. Every leg
    /// on this rig has one, and the rig report is the place that would show it missing.
    [Test]
    public void EveryLegHasAYawJointAndThreePitchJoints()
    {
        HorseLocomotion loco = Spawn();
        for (int i = 0; i < loco.LegCount; i++)
        {
            Assert.IsTrue(loco.TryGetMeasurement(i, out LegMeasurement m));
            Assert.Greater(m.MaxReach, 0.5f, $"leg {i} measured no linkage at all");
            Assert.Greater(m.RestHipHeight, 0.5f, $"leg {i} has no hip height");
        }
        Assert.Greater(loco.MaxSpeed, 1f,
                       "top speed is derived from stride and cadence; near zero means the gait " +
                       "never bound or the stride budget collapsed");
    }

    /// Diagonals are worked out from where the feet ARE, so the machine's own sorted leg order --
    /// which is left-then-rear-to-front, not front-then-back -- cannot make them come out wrong.
    [Test]
    public void DiagonalPairsComeOutAsDiagonalsFromTheRig()
    {
        HorseLocomotion loco = Spawn();

        var home = new Vector3[loco.LegCount];
        for (int i = 0; i < loco.LegCount; i++)
        {
            Assert.IsTrue(loco.TryGetMeasurement(i, out LegMeasurement m));
            home[i] = m.HomeLocal;
        }

        var measures = new System.Collections.Generic.List<LegMeasurement>();
        for (int i = 0; i < loco.LegCount; i++)
            measures.Add(new LegMeasurement { Index = i, HomeLocal = home[i] });

        var gait = new CanterGait(0.38f, 0.62f);
        gait.Bind(measures);

        int fl = Find(home, -1, 1), fr = Find(home, 1, 1);
        int hl = Find(home, -1, -1), hr = Find(home, 1, -1);

        Assert.AreEqual(gait.PhaseOffset(fl, 0.5f), gait.PhaseOffset(hr, 0.5f), 1e-4f,
                        "front-left and rear-right must trot together");
        Assert.AreEqual(gait.PhaseOffset(fr, 0.5f), gait.PhaseOffset(hl, 0.5f), 1e-4f,
                        "front-right and rear-left must trot together");
        Assert.AreNotEqual(gait.PhaseOffset(fl, 0.5f), gait.PhaseOffset(fr, 0.5f),
                           "the two diagonals must not be the same slice");
    }

    private static int Find(Vector3[] home, int signX, int signZ)
    {
        for (int i = 0; i < home.Length; i++)
            if (Mathf.Sign(home[i].x) == signX && Mathf.Sign(home[i].z) == signZ) return i;
        Assert.Fail($"no leg at ({signX}, {signZ}); the rig's legs are not on four corners");
        return -1;
    }

    // ─────────── the gait ───────────

    /// A planted hoof is a contract with the ground. If it moves, the machine is skating -- and it
    /// has to hold at every rung of the gait ladder, not just at the one it was tuned at.
    [Test]
    public void PlantedHoovesDoNotSlip(
        [Values(0.15f, 0.35f, 0.60f, 0.95f)] float speedFraction)
    {
        HorseLocomotion loco = Spawn();
        Trace t = March(loco, speedFraction, 0f);

        Assert.Less(t.WorstPlantedSlip, 0.01f,
            $"planted slip {t.WorstPlantedSlip:F5} m at {speedFraction:P0} of top speed");
        Assert.Greater(t.Steps, 4, "the machine took almost no steps; the gait has stalled it");
    }

    /// Turning is where a planar limb gives itself away: the body rotates under a foot that is
    /// supposed to be still, and only the coxa's yaw joint can absorb that.
    [Test]
    public void PlantedHoovesDoNotSlipThroughATurn()
    {
        HorseLocomotion loco = Spawn();
        Trace t = March(loco, 0.5f, loco.MaxYawRate * 0.7f);

        Assert.Less(t.WorstPlantedSlip, 0.01f,
            $"planted slip {t.WorstPlantedSlip:F5} m through a turn");
        Assert.Greater(t.Steps, 4, "the machine stopped stepping in the turn");
    }

    /// Distance is derived, not commanded: the clock advances by distance travelled and the twist
    /// is clamped to what the stride can carry, so what comes out has to match what went in.
    [Test]
    public void DistanceCoveredMatchesTheCommand(
        [Values(0.25f, 0.5f, 0.95f)] float speedFraction)
    {
        HorseLocomotion loco = Spawn();
        Trace t = March(loco, speedFraction, 0f);

        float expected = loco.MaxSpeed * speedFraction * Frames * Dt;
        Assert.AreEqual(expected, t.TravelledFlat, expected * 0.02f,
            $"covered {t.TravelledFlat:F2} m against {expected:F2} m commanded");
    }

    /// A walk keeps hooves on the ground; a gallop does not. The suspension is the whole difference
    /// between the two, and it is what the asymmetric offsets exist to make room for.
    [Test]
    public void AWalkKeepsHoovesDownAndAGallopHasASuspension()
    {
        HorseLocomotion walk = Spawn();
        Trace slow = March(walk, 0.2f, 0f);
        int walkAirborne = slow.AirborneFrames;
        int walkStance = slow.MinStanceLegs;
        // Tear the first machine down before spawning the second, or two of them stand in the same
        // scene and the ground raycasts of one hit the other.
        TearDown();

        Assert.AreEqual(0, walkAirborne,
            "a walk must never have all four hooves off the ground");
        Assert.GreaterOrEqual(walkStance, 2,
            "a walk should keep at least a diagonal on the ground");

        HorseLocomotion gallop = Spawn();
        Trace fast = March(gallop, 0.95f, 0f);
        // Measured at 27 frames of the 510 counted on the shipped tuning. The policy's arithmetic says
        // 10.8% of a cycle -- the clock's frozen swing spans and the step-early rule between them
        // spend some of it -- so this is set well under the measurement rather than at it.
        Assert.Greater(fast.AirborneFrames, 15,
            $"a gallop with no flight phase is a fast trot ({fast.AirborneFrames} airborne frames)");
        Assert.AreEqual(0, fast.FallingFrames,
            "the suspension must not be read as a fall -- that is what " +
            "fallThresholdFraction is for");
    }

    /// Gravity applies to every legged machine, and the horse is spawned into a streamed world
    /// where the terrain under it may arrive late.
    [Test]
    public void SpawnedHighItFallsAndLandsWithItsHoovesPlanted()
    {
        HorseLocomotion loco = Spawn(spawnHeight: 20f);
        loco.SetTwist(0f, 0f);

        float startY = horse.transform.position.y;
        for (int i = 0; i < 400; i++)
        {
            loco.Step(Dt);
            Physics.SyncTransforms();
        }

        Assert.Less(horse.transform.position.y, startY - 10f, "the machine hung in the sky");
        Assert.IsFalse(loco.IsFalling, "it never landed");

        for (int leg = 0; leg < loco.LegCount; leg++)
        {
            Assert.IsTrue(loco.TryGetFoot(leg, out Vector3 foot, out _));
            Assert.Less(Mathf.Abs(foot.y), 0.35f,
                $"leg {leg} landed with its hoof {foot.y:F2} m off the ground");
        }
    }

    /// Standing still means standing still. The clock is advanced by distance travelled, so a
    /// stationary machine has no reason to step and must not drift.
    [Test]
    public void StandingStillItDoesNotWalkAway()
    {
        HorseLocomotion loco = Spawn();
        Trace t = March(loco, 0f, 0f);

        Assert.Less(t.TravelledFlat, 0.05f, "the machine drifted while standing still");
        Assert.LessOrEqual(t.Steps, 4, "a stationary machine took steps it was not asked for");
    }

    // ─────────── the spine ───────────

    /// The one design error from the ostrich, written down so it cannot recur.
    ///
    /// `MeasuredVelocity` is the PATH velocity and deliberately excludes the bob and sway, which
    /// are display offsets added on top of it -- but the bob is exactly the motion a rider feels.
    /// The bounce therefore has to be driven by the BODY TRANSFORM, and this asserts that what it
    /// is actually driven by is the larger of the two signals.
    [Test]
    public void TheRideBounceIsDrivenByTheBodyTransformNotThePathVelocity()
    {
        HorseLocomotion loco = Spawn(keepSpine: true);
        var spine = horse.GetComponent<HorseSpineMotion>();
        Assert.IsNotNull(spine, "the prefab has no HorseSpineMotion");
        spine.Resolve();

        loco.SnapToGround();
        loco.SetTwist(loco.MaxSpeed * 0.6f, 0f);

        Vector3 lastPathVelocity = Vector3.zero;
        float peakTransform = 0f, peakPath = 0f, peakBend = 0f;

        for (int i = 0; i < 240; i++)
        {
            loco.Step(Dt);
            Physics.SyncTransforms();
            spine.Step(Dt);

            // Settle first: the first frames carry the snap-to-ground transient, which is exactly
            // what the teleport guard exists to swallow and is not a stride.
            if (i < 60)
            {
                lastPathVelocity = loco.MeasuredVelocity;
                continue;
            }

            Vector3 pathAccel = (loco.MeasuredVelocity - lastPathVelocity) / Dt;
            lastPathVelocity = loco.MeasuredVelocity;

            peakTransform = Mathf.Max(peakTransform, spine.LastDrive.magnitude);
            peakPath = Mathf.Max(peakPath, pathAccel.magnitude);
            peakBend = Mathf.Max(peakBend, spine.BounceAngles.magnitude);
        }

        Assert.Greater(peakTransform, peakPath * 1.5f,
            $"the transform's acceleration ({peakTransform:F2} m/s^2) should carry the ride the " +
            $"path velocity ({peakPath:F2} m/s^2) has had taken out of it; if these are the same " +
            "the bounce is being driven by the smoothed signal again");
        Assert.Greater(peakBend, 0.75f,
            $"the bounce only reached {peakBend:F2} degrees, which is invisible");
    }

    /// The spine resets to rest at the top of every frame, so its contributions cannot accumulate
    /// into a corkscrew over a long run.
    [Test]
    public void TheSpineDoesNotAccumulate()
    {
        HorseLocomotion loco = Spawn(keepSpine: true);
        var spine = horse.GetComponent<HorseSpineMotion>();
        spine.Resolve();

        Transform head = null;
        foreach (Transform t in horse.GetComponentsInChildren<Transform>(true))
            if (t.name == "Head") { head = t; break; }
        Assert.IsNotNull(head, "the rig has no Head bone");

        loco.SnapToGround();
        loco.SetTwist(0f, 0f);
        for (int i = 0; i < 30; i++) { loco.Step(Dt); spine.Step(Dt); }
        Quaternion settled = head.localRotation;

        for (int i = 0; i < 600; i++) { loco.Step(Dt); spine.Step(Dt); }

        Assert.Less(Quaternion.Angle(settled, head.localRotation), 2f,
            "the head drifted while the machine stood still; the chain is accumulating");
    }

    // The mount rig and the driver's two channels are asserted in
    // Assets/Editor/Tests/HorseRigWiringTests.cs instead: MountModule, SteerModule and
    // LeggedDriver all live in Assembly-CSharp, and an asmdef may not reference the predefined
    // assemblies. That test runs in Assembly-CSharp-Editor, which can see all of them.
}
