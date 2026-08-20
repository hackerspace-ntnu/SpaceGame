// Does the wing actually move?
//
// The rig-wiring tests prove the bones RESOLVE. These prove the animator USES them, which is a
// different failure: a rig can bind perfectly and still be posed with a sign that cancels, an axis
// that does nothing, or an amplitude of zero — and all three look like "the wings are stiff".
//
// Driven by a stub flight state rather than by the motor, so the articulation is exercised without
// a Rigidbody, a physics step or a rider. That is the entire reason IOrnithopterFlightState exists.
using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class OrnithopterWingAnimatorTests
    {
        private const string CraftPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";

        /// A hand-set flight state. Every field is writable so a test can put the craft in one exact
        /// condition and look at the wings.
        private class StubFlight : IOrnithopterFlightState
        {
            public float Airspeed { get; set; } = 20f;
            public float FlapPhase { get; set; }
            public float FlapEffort { get; set; } = 1f;
            public float WingSpread { get; set; } = 1f;
            public float BankAngle { get; set; }
            public float PitchInput { get; set; }
            public float TurnInput { get; set; }
            public bool IsStalled { get; set; }
        }

        private GameObject craft;
        private OrnithopterWingAnimator animator;
        private StubFlight stub;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CraftPath);
            Assert.IsNotNull(prefab, $"Ornithopter prefab missing at {CraftPath} — run " +
                                     "Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab.");

            craft = Object.Instantiate(prefab);
            animator = craft.GetComponent<OrnithopterWingAnimator>();
            Assert.IsNotNull(animator, "No wing animator on the prefab.");

            stub = new StubFlight();
            animator.Initialise(stub);
        }

        [TearDown]
        public void TearDown()
        {
            if (craft != null) Object.DestroyImmediate(craft);
            craft = null;

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go != null && go.transform.parent == null && go.name.StartsWith("DuneOrnithopter"))
                    Object.DestroyImmediate(go);
            }
        }

        /// LateUpdate does not run on an editor-instantiated object, so the animator is ticked
        /// directly with an explicit timestep. Several frames by default: the damped channels — bank,
        /// pitch trim, tail fan — approach their target rather than snapping, so asserting after a
        /// single frame would be reading a transient.
        private void Pose(int frames = 60)
        {
            for (int i = 0; i < frames; i++)
                animator.Tick(1f / 60f);
        }

        /// The delta a bone has been rotated by, in its own local space.
        ///
        /// Order matters and getting it backwards is a trap: the animator writes
        /// <c>local = rest * delta</c>, so the delta is <c>inverse(rest) * local</c>. The other order
        /// conjugates the delta by the rest pose, and because the two wings have DIFFERENT rest poses
        /// that makes identical deltas read as tens of degrees apart.
        private static Quaternion DeltaFromRest(Quaternion local, Quaternion rest)
        {
            return Quaternion.Inverse(rest) * local;
        }

        // ─────────── The beat ───────────

        [Test]
        public void TheShouldersMoveThroughTheBeat()
        {
            stub.FlapPhase = 0.25f;                       // top of the downstroke
            Pose(1);
            Quaternion down = animator.Rig.Right.Shoulder.localRotation;

            stub.FlapPhase = 0.75f;                       // top of the upstroke
            Pose(1);
            Quaternion up = animator.Rig.Right.Shoulder.localRotation;

            Assert.Greater(Quaternion.Angle(down, up), 10f,
                "The shoulder barely moved between the two extremes of the beat — the wings are not " +
                "flapping.");
        }

        [Test]
        public void TheBeatIsSymmetricAcrossTheTwoWings()
        {
            stub.FlapPhase = 0.25f;
            Pose(1);

            // The wing bones point outboard in OPPOSITE directions, so their local X is already
            // mirrored: the SAME local delta on both sides is what produces a symmetric flap. If a
            // per-side sign ever creeps onto the beat, this is what catches it.
            float delta = Quaternion.Angle(
                DeltaFromRest(animator.Rig.Left.Shoulder.localRotation, animator.Rig.Left.ShoulderRest),
                DeltaFromRest(animator.Rig.Right.Shoulder.localRotation, animator.Rig.Right.ShoulderRest));

            Assert.Less(delta, 0.5f,
                "The two shoulders are being flapped by different amounts. The beat must NOT carry a " +
                "per-side sign — the mirrored bone axes already do that.");
        }

        [Test]
        public void TheGearsTurnWithTheBeat()
        {
            stub.FlapPhase = 0f;
            Pose(1);
            Quaternion a = animator.Rig.Right.Gear.localRotation;

            stub.FlapPhase = 0.5f;
            Pose(1);

            Assert.Greater(Quaternion.Angle(a, animator.Rig.Right.Gear.localRotation), 10f,
                "The drive wheels are not turning with the wings — the mechanism reads as decoration.");
        }

        // ─────────── Spread ───────────

        [Test]
        public void SpreadingTheWingsOpensTheDigits()
        {
            stub.WingSpread = 0f;
            Pose(1);
            Quaternion folded = animator.Rig.Right.Digits[0].localRotation;

            stub.WingSpread = 1f;
            Pose(1);
            Quaternion open = animator.Rig.Right.Digits[0].localRotation;

            Assert.Greater(Quaternion.Angle(folded, open), 15f,
                "The digits do not open as the wing spreads — there is no deploy animation.");
        }

        [Test]
        public void SpreadingSweepsTheArmsForward()
        {
            stub.WingSpread = 0f;
            Pose(1);
            Quaternion folded = animator.Rig.Right.Arm.localRotation;

            stub.WingSpread = 1f;
            Pose(1);

            Assert.Greater(Quaternion.Angle(folded, animator.Rig.Right.Arm.localRotation), 15f,
                "The arms do not sweep as the wing spreads.");
        }

        [Test]
        public void TheDigitsOpenTheSameAmountOnBothWings()
        {
            stub.WingSpread = 1f;
            stub.FlapEffort = 0f;      // isolate splay from the twist the beat adds
            stub.FlapPhase = 0f;
            Pose(1);

            // Splay is about local Z, which is NOT mirrored between the wings, so it carries an
            // explicit per-side sign. Get that wrong and one wing opens while the other closes — the
            // exact bug the model's build record warns about twice.
            Vector3 leftTip = animator.Rig.Left.Digits[0].position - animator.Rig.Left.Arm.position;
            Vector3 rightTip = animator.Rig.Right.Digits[0].position - animator.Rig.Right.Arm.position;

            Assert.That(leftTip.y, Is.EqualTo(rightTip.y).Within(0.05f),
                $"The wings are not opening symmetrically: left tip is at y={leftTip.y:F3}, right at " +
                $"y={rightTip.y:F3}. One wing is opening while the other closes — check the per-side " +
                "sign on the Z-axis splay.");
        }

        // ─────────── Control surfaces ───────────

        [Test]
        public void PitchInputMovesTheTailBoom()
        {
            stub.PitchInput = -1f;
            Pose();
            Quaternion down = animator.Rig.Boom1.localRotation;

            stub.PitchInput = 1f;
            Pose();

            Assert.Greater(Quaternion.Angle(down, animator.Rig.Boom1.localRotation), 10f,
                "The tail boom does not respond to pitch — the back wing is doing nothing.");
        }

        [Test]
        public void TurnInputSplaysTheTailFanAsymmetrically()
        {
            stub.TurnInput = 0f;
            Pose();
            Quaternion straight = animator.Rig.TailDigits[0].localRotation;

            stub.TurnInput = 1f;
            Pose();
            Quaternion turning = animator.Rig.TailDigits[0].localRotation;

            Assert.Greater(Quaternion.Angle(straight, turning), 5f,
                "The tail fan does not open in a turn — this is the surface that steers the machine.");

            // Outer feathers on opposite sides of the fan must not move together, or the fan is just
            // opening rather than yawing.
            Quaternion other = animator.Rig.TailDigits[OrnithopterWingRig.DigitCount - 1].localRotation;
            Assert.Greater(Quaternion.Angle(turning, other), 5f,
                "Both ends of the tail fan moved identically — the splay is symmetric, so it produces " +
                "no yaw.");
        }

        [Test]
        public void BankingTwistsTheWingsAgainstEachOther()
        {
            stub.BankAngle = 0f;
            stub.FlapEffort = 0f;
            Pose();
            Quaternion leftLevel = animator.Rig.Left.Digits[2].localRotation;
            Quaternion rightLevel = animator.Rig.Right.Digits[2].localRotation;

            stub.BankAngle = 40f;
            Pose();
            Quaternion leftBanked = animator.Rig.Left.Digits[2].localRotation;
            Quaternion rightBanked = animator.Rig.Right.Digits[2].localRotation;

            float leftChange = Quaternion.Angle(leftLevel, leftBanked);
            float rightChange = Quaternion.Angle(rightLevel, rightBanked);

            Assert.Greater(leftChange, 1f, "The left wing does not twist when banking.");
            Assert.Greater(rightChange, 1f, "The right wing does not twist when banking.");

            // Both twist by the same amount in OPPOSITE directions. Same magnitude, and the two must
            // not end up at the same local rotation.
            Assert.That(leftChange, Is.EqualTo(rightChange).Within(0.5f),
                "The roll differential is lopsided.");
            Assert.Greater(Quaternion.Angle(leftBanked, rightBanked), 1f,
                "Both wings twisted the same way — that is not a roll differential, it is just twist.");
        }

        [Test]
        public void SnapToFoldedReturnsEveryBoneToRest()
        {
            stub.FlapPhase = 0.3f;
            stub.BankAngle = 30f;
            Pose();

            animator.SnapToFolded();

            Assert.AreEqual(0f, Quaternion.Angle(animator.Rig.Right.Shoulder.localRotation,
                                                 animator.Rig.Right.ShoulderRest), 0.01f);
            Assert.AreEqual(0f, Quaternion.Angle(animator.Rig.Boom1.localRotation,
                                                 animator.Rig.Boom1Rest), 0.01f);
        }
    }
}
