// Whole machines at leg counts nothing in this project models.
//
// The base was extracted from a biped and then a hexapod, so those two prove very little about
// whether it GENERALISES -- they are the two shapes it was fitted to. These rigs are built
// procedurally, so a tripod and a quadruped with mismatched front and rear legs cost no art.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

/// A machine whose four policies are set by the test rather than serialized.
public class TestMachine : LeggedLocomotion
{
    public IStrideModel Stride = new YawArcStride(40f);
    public IGaitPattern Gait = new RippleGait(1, 3);
    public IBodyMotion Motion = new LevelDeckBody();
    public IFootStyle Feet = new FlatSole();

    protected override IStrideModel CreateStride() => Stride;
    protected override IGaitPattern CreateGait() => Gait;
    protected override IBodyMotion CreateBody() => Motion;
    protected override IFootStyle CreateFeet() => Feet;
}

public class SyntheticMachineTests
{
    private GameObject ground;
    private GameObject machine;
    private Mesh cube;

    [SetUp]
    public void SetUp()
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "TestGround";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(600f, 1f, 600f);

        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);

        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (machine != null) Object.DestroyImmediate(machine);
        if (ground != null) Object.DestroyImmediate(ground);
    }

    // ─────────── building a machine ───────────

    /// `legs` is one entry per leg: where it attaches in body space, and how long its thigh and
    /// shin are. Splayed outward so the coxa sweeps a real arc, which is what a yaw-arc stride
    /// needs to size itself from.
    private TestMachine Build(params (Vector3 attach, float upper, float lower)[] legs)
    {
        machine = new GameObject("Synthetic");
        machine.transform.position = new Vector3(0f, 2f, 0f);

        for (int i = 0; i < legs.Length; i++)
        {
            (Vector3 attach, float upper, float lower) = legs[i];
            Vector3 outward = new Vector3(attach.x, 0f, attach.z).normalized;

            Transform coxa = Joint("Coxa_" + i, machine.transform, attach, Vector3.up);
            // The pitch axle is across the leg's own outboard direction, so the leg swings in a
            // vertical plane containing it.
            Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

            Transform hip = Joint("Hip_" + i, coxa, Vector3.zero, pitch);
            Transform knee = Joint("Knee_" + i, hip, outward * upper * 0.7f - Vector3.up * upper * 0.3f, pitch);
            Transform ankle = Joint("Ankle_" + i, knee, outward * lower * 0.2f - Vector3.up * lower, pitch);
            Sole(ankle, -Vector3.up * 0.15f);
        }

        Physics.SyncTransforms();
        return machine.AddComponent<TestMachine>();
    }

    private Transform Joint(string name, Transform parent, Vector3 offset, Vector3 hinge)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;

        var pin = new GameObject(name.Split('_')[0] + "Pin");
        pin.transform.SetParent(go.transform, false);
        pin.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, hinge.normalized);
        pin.transform.localScale = new Vector3(0.03f, 0.03f, 0.3f);
        pin.AddComponent<MeshFilter>().sharedMesh = cube;
        return go.transform;
    }

    private void Sole(Transform parent, Vector3 offset)
    {
        var go = new GameObject("SoleMesh");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localScale = new Vector3(0.25f, 0.05f, 0.3f);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        go.AddComponent<MeshRenderer>();
    }

    private static (Vector3, float, float) Leg(float x, float z, float upper = 1f, float lower = 1.4f)
        => (new Vector3(x, 0f, z), upper, lower);

    private void March(TestMachine m, float speed, float yawRate, int frames,
                       System.Action perFrame = null)
    {
        for (int i = 0; i < frames; i++)
        {
            m.SetTwist(speed, yawRate);
            m.Step(1f / 60f);
            Physics.SyncTransforms();
            perFrame?.Invoke();
        }
    }

    // ─────────── three legs ───────────

    [Test]
    public void AThreeLeggedMachineIsDiscoveredAndWalks()
    {
        TestMachine m = Build(Leg(0f, 1.2f), Leg(-1.1f, -0.7f), Leg(1.1f, -0.7f));
        m.Initialise();

        Assert.IsTrue(m.IsReady, "no limb chains were found on a three-legged rig");
        Assert.AreEqual(3, m.LegCount);
        Assert.Greater(m.MaxSpeed, 0f, "a machine whose top speed is zero cannot be commanded");

        m.SnapToGround();
        Vector3 start = m.transform.position;
        March(m, m.MaxSpeed * 0.5f, 0f, 400);

        float travelled = Vector2.Distance(
            new Vector2(start.x, start.z),
            new Vector2(m.transform.position.x, m.transform.position.z));

        Assert.Greater(travelled, 1f, "the tripod went nowhere; the gait has stalled it");
    }

    /// Invariant I1, on the machine rather than on the policy. A ripple gait asked to keep three
    /// feet planted on a three-legged machine can never satisfy its own gate; if that latched, the
    /// machine would stop and nothing would restart it.
    [Test]
    public void AThreeLeggedMachineDoesNotStallOnAnUnsatisfiableMinPlanted()
    {
        TestMachine m = Build(Leg(0f, 1.2f), Leg(-1.1f, -0.7f), Leg(1.1f, -0.7f));
        m.Gait = new RippleGait(1, 3);          // three planted, out of three legs
        m.Initialise();
        m.SnapToGround();

        Vector3 start = m.transform.position;
        March(m, m.MaxSpeed * 0.6f, 15f, 500);

        float travelled = Vector2.Distance(
            new Vector2(start.x, start.z),
            new Vector2(m.transform.position.x, m.transform.position.z));

        Assert.Greater(travelled, 1f,
            "the min-planted gate latched the machine; it must degrade to never-block");
    }

    // ─────────── four legs ───────────

    [Test]
    public void AQuadrupedWithUnequalLegsGetsAStridePerLeg()
    {
        // Short front pair, long rear pair.
        TestMachine m = Build(
            Leg(-1f, 1f, 0.6f, 0.8f), Leg(1f, 1f, 0.6f, 0.8f),
            Leg(-1f, -1f, 1.2f, 1.6f), Leg(1f, -1f, 1.2f, 1.6f));
        m.Stride = new HipBudgetStride(0.86f);
        m.Initialise();

        Assert.AreEqual(4, m.LegCount);

        var strides = new List<float>();
        var reaches = new List<float>();
        for (int i = 0; i < m.LegCount; i++)
        {
            Assert.IsTrue(m.TryGetMeasurement(i, out LegMeasurement measure));
            strides.Add(measure.StrideLength);
            reaches.Add(measure.LegReach);
        }

        strides.Sort();
        reaches.Sort();

        Assert.Greater(reaches[3] - reaches[0], 0.3f, "the test rig's legs should differ in length");
        Assert.Greater(strides[3] - strides[0], 0.05f,
            "every leg got the same stride; the measurement is still being averaged");
    }

    [Test]
    public void AQuadrupedTrotsOnDiagonalPairs()
    {
        TestMachine m = Build(Leg(-1f, 1f), Leg(1f, 1f), Leg(-1f, -1f), Leg(1f, -1f));
        m.Gait = new TrotGait(0.4f, 0.5f);
        m.Initialise();
        m.SnapToGround();

        // Legs are sorted left-then-rear-to-front at Initialise, so read the layout back rather
        // than assuming an order.
        var home = new Vector3[m.LegCount];
        for (int i = 0; i < m.LegCount; i++)
        {
            Assert.IsTrue(m.TryGetMeasurement(i, out LegMeasurement measure));
            home[i] = measure.HomeLocal;
        }

        int frontLeft = Find(home, -1, 1), frontRight = Find(home, 1, 1);
        int rearLeft = Find(home, -1, -1), rearRight = Find(home, 1, -1);

        var trot = new TrotGait(0.4f, 0.5f);
        var measures = new List<LegMeasurement>();
        for (int i = 0; i < m.LegCount; i++) measures.Add(new LegMeasurement { Index = i, HomeLocal = home[i] });
        trot.Bind(measures);

        Assert.AreEqual(trot.PhaseOffset(frontLeft, 1f), trot.PhaseOffset(rearRight, 1f), 1e-4f);
        Assert.AreEqual(trot.PhaseOffset(frontRight, 1f), trot.PhaseOffset(rearLeft, 1f), 1e-4f);
        Assert.AreNotEqual(trot.PhaseOffset(frontLeft, 1f), trot.PhaseOffset(frontRight, 1f));

        Vector3 start = m.transform.position;
        March(m, m.MaxSpeed * 0.8f, 0f, 400);
        Assert.Greater(Vector2.Distance(new Vector2(start.x, start.z),
                                        new Vector2(m.transform.position.x, m.transform.position.z)),
                       1f, "the trotting quadruped went nowhere");
    }

    private static int Find(Vector3[] home, int signX, int signZ)
    {
        for (int i = 0; i < home.Length; i++)
            if (Mathf.Sign(home[i].x) == signX && Mathf.Sign(home[i].z) == signZ) return i;
        Assert.Fail($"no leg at ({signX}, {signZ})");
        return -1;
    }

    // ─────────── crossing a slope ───────────

    /// The fault this was written for. A body held dead level puts every hip at one height while its
    /// feet are at several, so crossing a hillside asks the downhill legs to span the ride height
    /// PLUS the whole drop. Past their reach they stop arriving and the machine walks along on its
    /// uphill legs with the downhill ones hanging in the air.
    ///
    /// Measured on the six-legged machine before the fix: at a 30-degree cross-slope a level deck
    /// reached 2.1x its own leg length and ended up standing on two feet of six.
    [Test]
    public void ALevelBodyStrandsItsDownhillLegsOnACrossSlope()
    {
        Trace level = CrossSlope(25f, new LevelDeckBody(slopeFollow: 0f));
        Trace following = CrossSlope(25f, new LevelDeckBody(slopeFollow: 0.8f, maxTilt: 30f));

        Assert.Greater(level.WorstReach, 1f,
            "the level-deck case should reproduce the fault; if it no longer does, the test rig " +
            "is too gentle to be guarding anything");

        Assert.Less(following.WorstReach, 1f,
            "following the slope should keep every foot inside its leg's reach, but the worst was " +
            following.WorstReach.ToString("F3"));
        Assert.Less(following.WorstReach, level.WorstReach,
            "following the slope must be an improvement, not merely different");
        Assert.GreaterOrEqual(following.FewestStance, level.FewestStance,
            "following the slope must not cost support");
    }

    [Test]
    public void TheBodyRollsTowardTheSlopeItIsStandingOn()
    {
        Trace t = CrossSlope(25f, new LevelDeckBody(slopeFollow: 0.8f, maxTilt: 30f));

        Assert.Greater(Mathf.Abs(t.Roll), 5f,
            "the body stayed level on a 25-degree cross-slope; it rolled " +
            t.Roll.ToString("F1") + " degrees");
        Assert.Less(Mathf.Abs(t.Roll), 25f, "and it should still be flatter than the ground");
    }

    /// The cap is what keeps a crewed deck walkable on ground it should have gone around.
    [Test]
    public void TheBodysTiltIsCapped()
    {
        Trace t = CrossSlope(35f, new LevelDeckBody(slopeFollow: 1f, maxTilt: 10f));
        Assert.LessOrEqual(Mathf.Abs(t.Roll), 11f);
    }

    private struct Trace
    {
        public float WorstReach;
        public int FewestStance;
        public float Roll;
    }

    private Trace CrossSlope(float degrees, IBodyMotion motion)
    {
        // A slab tilted about the travel axis: the machine walks ALONG the slope, one side high.
        ground.transform.rotation = Quaternion.Euler(0f, 0f, -degrees);
        ground.transform.position = Vector3.zero;
        Physics.SyncTransforms();

        TestMachine m = Build(Leg(-1.2f, 1.2f), Leg(1.2f, 1.2f), Leg(-1.2f, -1.2f), Leg(1.2f, -1.2f));
        machine.transform.position = new Vector3(0f, 8f, -6f);
        m.Motion = motion;
        Physics.SyncTransforms();

        m.Initialise();
        m.SnapToGround();

        var t = new Trace { FewestStance = int.MaxValue };
        for (int i = 0; i < 500; i++)
        {
            m.SetTwist(m.MaxSpeed * 0.5f, 0f);
            m.Step(1f / 60f);
            Physics.SyncTransforms();

            if (i < 90) continue;       // let it settle onto the slope first

            t.WorstReach = Mathf.Max(t.WorstReach, m.LastFrame.WorstReachFraction);
            t.FewestStance = Mathf.Min(t.FewestStance, m.LastFrame.StanceLegs);

            float roll = Mathf.DeltaAngle(0f, machine.transform.eulerAngles.z);
            if (Mathf.Abs(roll) > Mathf.Abs(t.Roll)) t.Roll = roll;
        }

        Object.DestroyImmediate(machine);
        machine = null;
        return t;
    }

    // ─────────── gravity, which every machine now has ───────────

    [Test]
    public void AnySyntheticMachineSpawnedInTheAirComesDown()
    {
        TestMachine m = Build(Leg(-1f, 1f), Leg(1f, 1f), Leg(-1f, -1f), Leg(1f, -1f));
        machine.transform.position = new Vector3(0f, 20f, 0f);
        Physics.SyncTransforms();
        m.Initialise();

        for (int i = 0; i < 400; i++) m.Step(1f / 60f);

        Assert.That(m.transform.position.y, Is.EqualTo(m.RideHeight).Within(0.3f),
            "dropped from 20 m the machine settled at y=" + m.transform.position.y.ToString("F2"));
    }

    [Test]
    public void PlantedFeetDoNotSlideOnASyntheticQuadruped()
    {
        TestMachine m = Build(Leg(-1f, 1f), Leg(1f, 1f), Leg(-1f, -1f), Leg(1f, -1f));
        m.Initialise();
        m.SnapToGround();

        var last = new Vector3[m.LegCount];
        var wasPlanted = new bool[m.LegCount];
        float worst = 0f;

        March(m, m.MaxSpeed * 0.5f, 20f, 500, () =>
        {
            for (int i = 0; i < m.LegCount; i++)
            {
                if (!m.TryGetFoot(i, out Vector3 foot, out bool swinging)) continue;
                if (!swinging && wasPlanted[i])
                    worst = Mathf.Max(worst, Vector3.Distance(foot, last[i]));
                wasPlanted[i] = !swinging;
                last[i] = foot;
            }
        });

        Assert.Less(worst, 0.01f, "a planted foot moved " + worst.ToString("F4") + " m");
    }
}
