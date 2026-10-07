// Travel that is not along the machine's nose.
//
// `SetTwist` used to be a speed and a yaw rate, and every one of the six places that needed to know
// where the machine was going derived it from `currentYaw`. That is fine for a bird and for a
// walking station, both of which point at what they are walking toward -- and it cannot express a
// crab, which holds its body square to the direction it is travelling.
//
// The twist's linear half is now a PLANAR VELOCITY IN BODY SPACE: x to the right, y along the nose.
// The tests below are the ones that would have caught each way of getting that half-done:
//
//   * a lateral channel that the path integrates but the gait does not -- the body slides sideways
//     while every foothold is aimed down the nose, so the feet spend their whole stance being
//     dragged out from under the machine;
//   * a lateral channel that `Pace` does not count -- pace 0 stops a clock advanced by DISTANCE, no
//     phase slice ever reopens, and the machine stands there for good (invariant I1);
//   * a clamp applied per component -- which turns a diagonal command into a different direction and
//     lets a machine outrun its own legs on the diagonal;
//   * and the one that matters most to the two machines already shipping: a forward command that no
//     longer means what it did.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    /// A machine whose policies the test sets, and which lets the test read back the twist as the base
    /// actually stored it. Separate from `TestMachine` so the two files cannot fight over one class.
    public class LateralTestMachine : LeggedLocomotion
    {
        public IStrideModel Stride = new YawArcStride(40f);
        public IGaitPattern Gait = new RippleGait(1, 3);
        public IBodyMotion Motion = new LevelDeckBody();
        public IFootStyle Feet = new FlatSole();

        protected override IStrideModel CreateStride() => Stride;
        protected override IGaitPattern CreateGait() => Gait;
        protected override IBodyMotion CreateBody() => Motion;
        protected override IFootStyle CreateFeet() => Feet;

        /// The commanded twist as stored, so a clamp that quietly zeroed a channel is visible.
        public Vector2 CommandedTwist => CommandedVelocity;

        /// What the cadence is actually being paced by. Zero here stops the machine for good.
        public float PacePublic => Pace;
    }

    public class LateralTravelTests
    {
        private const float Dt = 1f / 60f;
        private const int Frames = 500;
        private const int Settle = 90;

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

        // ─────────── the twist itself ───────────

        /// The float overload is the whole reason the shipping machines did not have to change: it means
        /// exactly what it used to, which is a velocity straight down the nose and nothing sideways.
        [Test]
        public void TheForwardOverloadIsPurelyForward()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();

            m.SetTwist(m.MaxSpeed * 0.5f, 0f);

            Assert.AreEqual(0f, m.CommandedTwist.x, 1e-6f, "the forward overload put something sideways");
            Assert.AreEqual(m.MaxSpeed * 0.5f, m.CommandedTwist.y, 1e-4f);
        }

        /// Invariant I1, at the clamp. A machine asked to go sideways faster than its legs can carry it
        /// must come back slower and still sideways -- never stopped. The clock advances by distance
        /// travelled, so a lateral channel clamped to zero is a machine that never steps again.
        [Test]
        public void AnOverSpeedSidewaysCommandIsSlowedAndNeverZeroed()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();

            m.SetTwist(new Vector2(m.MaxSpeed * 4f, 0f), 0f);

            Assert.AreEqual(m.MaxSpeed, m.CommandedTwist.magnitude, 1e-3f,
                "an over-speed lateral command was not brought back to MaxSpeed");
            Assert.Greater(m.CommandedTwist.x, 0f, "the lateral channel was clamped to zero (I1)");
            Assert.Greater(m.PacePublic, 0f,
                "pace is zero on a purely lateral command; the gait clock will never turn again (I1)");
        }

        /// Clamping x and y separately would leave a diagonal command pointing somewhere else, and would
        /// let the machine travel MaxSpeed*sqrt(2) on the diagonal. The magnitude is what is bounded.
        [Test]
        public void TheClampScalesTheVelocityRatherThanItsComponents()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();

            m.SetTwist(new Vector2(m.MaxSpeed * 3f, m.MaxSpeed * 3f), 0f);
            Vector2 got = m.CommandedTwist;

            Assert.AreEqual(m.MaxSpeed, got.magnitude, 1e-3f, "the diagonal outran the legs");
            Assert.AreEqual(got.x, got.y, 1e-4f, "the clamp turned the machine off its commanded heading");
        }

        /// Standing still still means standing still. A pace of zero here is correct and is the one case
        /// where a stopped clock is the answer rather than the bug.
        [Test]
        public void AZeroTwistIsStillAZeroTwist()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            Vector3 start = m.transform.position;
            March(m, Vector2.zero, 0f, 300);

            Assert.Less(Flat(m.transform.position - start), 0.05f,
                "a machine commanded nothing walked off on its own");
        }

        // ─────────── travelling sideways ───────────

        [Test]
        public void ASidewaysCommandTravelsSidewaysAtTheCommandedSpeed()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            float speed = m.MaxSpeed * 0.5f;
            Vector3 start = m.transform.position;
            Trace t = March(m, new Vector2(speed, 0f), 0f, Frames);
            Vector3 moved = m.transform.position - start;

            // The rig starts with its nose down +z, so the body's right is world +x throughout: the yaw
            // rate is zero and nothing else may turn the machine.
            Assert.AreEqual(speed * Frames * Dt, moved.x, speed * Frames * Dt * 0.02f,
                "the machine did not cover the commanded lateral distance");
            Assert.Less(Mathf.Abs(moved.z), 0.25f,
                "the machine drifted " + moved.z.ToString("F3") + " m along its nose while going sideways");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, m.transform.eulerAngles.y)), 1f,
                "the body turned to face where it was going; a crab does not");

            Assert.Less(t.WorstSlip, 0.01f,
                "a planted foot moved " + t.WorstSlip.ToString("F5") + " m travelling sideways");
            Assert.LessOrEqual(t.WorstReach, 1f,
                "worst reach fraction travelling sideways was " + t.WorstReach.ToString("F4"));
        }

        /// The other way, because a gait sequence ordered along one axis is not automatically symmetric.
        [Test]
        public void TheOtherSidewaysDirectionWorksToo()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            float speed = m.MaxSpeed * 0.5f;
            Vector3 start = m.transform.position;
            Trace t = March(m, new Vector2(-speed, 0f), 0f, Frames);
            Vector3 moved = m.transform.position - start;

            Assert.AreEqual(-speed * Frames * Dt, moved.x, speed * Frames * Dt * 0.02f);
            Assert.Less(t.WorstSlip, 0.01f,
                "a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
            Assert.LessOrEqual(t.WorstReach, 1f, "worst reach was " + t.WorstReach.ToString("F4"));
        }

        [Test]
        public void ADiagonalCommandTravelsOnTheDiagonal()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            float speed = m.MaxSpeed * 0.5f;
            var cmd = new Vector2(speed * Mathf.Sqrt(0.5f), speed * Mathf.Sqrt(0.5f));

            Vector3 start = m.transform.position;
            Trace t = March(m, cmd, 0f, Frames);
            Vector3 moved = m.transform.position - start;
            moved.y = 0f;

            Assert.AreEqual(speed * Frames * Dt, moved.magnitude, speed * Frames * Dt * 0.02f,
                "the diagonal did not cover the commanded distance");
            Assert.AreEqual(moved.x, moved.z, moved.magnitude * 0.02f,
                "the diagonal came out at " + Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg + " degrees");
            Assert.Less(t.WorstSlip, 0.01f, "a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
        }

        /// Invariant I1 on the machine rather than on the clamp. The clock advances by distance, so if
        /// anything in the chain read the forward component alone the machine would stand there with a
        /// commanded speed it never spent.
        [Test]
        public void TheGaitStepsWhileTravellingSideways()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            var steps = new int[m.LegCount];
            var wasSwinging = new bool[m.LegCount];
            March(m, new Vector2(m.MaxSpeed * 0.5f, 0f), 0f, Frames, () =>
            {
                for (int i = 0; i < m.LegCount; i++)
                {
                    if (!m.TryGetFoot(i, out _, out bool sw)) continue;
                    if (sw && !wasSwinging[i]) steps[i]++;
                    wasSwinging[i] = sw;
                }
            });

            // Every leg, not merely a total: a clock that turned would eventually reach every slice, and
            // a machine that only ever stepped its leading legs is a different fault worth separating.
            for (int i = 0; i < steps.Length; i++)
                Assert.Greater(steps[i], 0,
                    "leg " + i + " never left the ground over " + Frames + " frames of lateral travel; " +
                    "the gait clock is not being advanced by sideways motion");

            Assert.Greater(m.LastFrame.Phase + m.LastFrame.Duty, 0f, "the gait clock reports nothing");
        }

        /// Sideways AND turning at once. The foot drift is -(v + w x r) and the lateral half has to be in
        /// the v, or the two channels disagree about where the foot is going and the slip comes back.
        [Test]
        public void SidewaysTravelSurvivesATurn()
        {
            LateralTestMachine m = Hexapod();
            m.Initialise();
            m.SnapToGround();

            Trace t = March(m, new Vector2(m.MaxSpeed * 0.4f, 0f), 15f, Frames);

            Assert.Less(t.WorstSlip, 0.01f,
                "a planted foot moved " + t.WorstSlip.ToString("F5") + " m crabbing through a turn");
        }

        // ─────────── the shipping machines must not notice ───────────

        /// Two identical machines, one driven through each overload. Any difference at all here is a
        /// change to what the ostrich and the crawler do, which is the thing F2 is not allowed to be.
        [Test]
        public void BothOverloadsDriveTheMachineIdenticallyGoingForward()
        {
            LateralTestMachine a = Hexapod();
            a.Initialise();
            a.SnapToGround();
            for (int i = 0; i < 240; i++) { a.SetTwist(a.MaxSpeed * 0.5f, 12f); a.Step(Dt); Physics.SyncTransforms(); }
            Vector3 endA = a.transform.position;
            float yawA = a.transform.eulerAngles.y;
            Object.DestroyImmediate(machine);
            machine = null;

            LateralTestMachine b = Hexapod();
            b.Initialise();
            b.SnapToGround();
            for (int i = 0; i < 240; i++) { b.SetTwist(new Vector2(0f, b.MaxSpeed * 0.5f), 12f); b.Step(Dt); Physics.SyncTransforms(); }

            Assert.AreEqual(endA.x, b.transform.position.x, 1e-4f);
            Assert.AreEqual(endA.y, b.transform.position.y, 1e-4f);
            Assert.AreEqual(endA.z, b.transform.position.z, 1e-4f);
            Assert.AreEqual(yawA, b.transform.eulerAngles.y, 1e-3f);
        }

        // ─────────── the rig ───────────

        private struct Trace
        {
            public float WorstSlip;
            public float WorstReach;
        }

        private Trace March(LateralTestMachine m, Vector2 velocityLocal, float yawRate, int frames,
                            System.Action perFrame = null)
        {
            var t = new Trace();
            var last = new Vector3[m.LegCount];
            var wasPlanted = new bool[m.LegCount];

            for (int i = 0; i < frames; i++)
            {
                m.SetTwist(velocityLocal, yawRate);
                m.Step(Dt);
                Physics.SyncTransforms();

                for (int leg = 0; leg < m.LegCount; leg++)
                {
                    if (!m.TryGetFoot(leg, out Vector3 foot, out bool swinging)) continue;
                    if (!swinging && wasPlanted[leg])
                        t.WorstSlip = Mathf.Max(t.WorstSlip, Vector3.Distance(foot, last[leg]));
                    wasPlanted[leg] = !swinging;
                    last[leg] = foot;
                }

                if (i > Settle) t.WorstReach = Mathf.Max(t.WorstReach, m.LastFrame.WorstReachFraction);
                perFrame?.Invoke();
            }

            return t;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// Six splayed legs on long coxae -- the shape a yaw-arc stride is for, and the shape a crab
        /// has. Built procedurally so no art is needed and the geometry is stated rather than authored.
        private LateralTestMachine Hexapod()
        {
            return Build(
                Leg(-1.2f, 1.2f), Leg(-1.45f, 0f), Leg(-1.2f, -1.2f),
                Leg(1.2f, 1.2f), Leg(1.45f, 0f), Leg(1.2f, -1.2f));
        }

        private static (Vector3, float, float) Leg(float x, float z, float upper = 1f, float lower = 1.4f)
            => (new Vector3(x, 0f, z), upper, lower);

        private LateralTestMachine Build(params (Vector3 attach, float upper, float lower)[] legs)
        {
            machine = new GameObject("LateralSynthetic");
            machine.transform.position = new Vector3(0f, 2f, 0f);

            for (int i = 0; i < legs.Length; i++)
            {
                (Vector3 attach, float upper, float lower) = legs[i];
                Vector3 outward = new Vector3(attach.x, 0f, attach.z).normalized;

                Transform coxa = Joint("Coxa_" + i, machine.transform, attach, Vector3.up);
                Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

                Transform hip = Joint("Hip_" + i, coxa, Vector3.zero, pitch);
                Transform knee = Joint("Knee_" + i, hip, outward * upper * 0.7f - Vector3.up * upper * 0.3f, pitch);
                Transform ankle = Joint("Ankle_" + i, knee, outward * lower * 0.2f - Vector3.up * lower, pitch);
                Sole(ankle, -Vector3.up * 0.15f);
            }

            Physics.SyncTransforms();
            return machine.AddComponent<LateralTestMachine>();
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
    }
}
