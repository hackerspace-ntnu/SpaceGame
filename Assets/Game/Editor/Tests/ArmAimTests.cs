// A worn device has to end up pointing at what the player is looking at.
//
// The failure this pins is the quiet one: the arm moves, the pose plays, the lamp is lit, and the
// beam lands a few degrees off the crosshair because the device is not AT the elbow the solver
// bent. Nothing about that shows up in a console, so it is measured here instead — in degrees off
// the target, on a chain with the same shape as the real one (shoulder, elbow, a lamp held out
// beyond the wrist).
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ArmAimTests
    {
        private GameObject root;
        private Transform upper;
        private Transform lower;
        private Transform pointer;

        /// <summary>Arm's-length chain: 0.3 m upper, 0.3 m lower, the lamp 0.1 m past the wrist.</summary>
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Shoulder");
            upper = root.transform;
            lower = new GameObject("Elbow").transform;
            pointer = new GameObject("Lamp").transform;

            lower.SetParent(upper, worldPositionStays: false);
            lower.localPosition = new Vector3(0f, -0.3f, 0f);
            pointer.SetParent(lower, worldPositionStays: false);
            pointer.localPosition = new Vector3(0f, -0.3f, 0.1f);

            upper.position = new Vector3(0f, 1.4f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (lower != null) Object.DestroyImmediate(lower.gameObject);
            if (pointer != null) Object.DestroyImmediate(pointer.gameObject);
        }

        private float DegreesOff(Vector3 target) =>
            Vector3.Angle(pointer.forward, target - pointer.position);

        [Test]
        public void AlreadyOnTargetMovesNothing()
        {
            Quaternion before = upper.rotation;

            ArmAim.Point(upper, lower, pointer, pointer.position + pointer.forward * 10f,
                         shoulderShare: 0.55f, maxDegrees: 75f, weight: 1f, elbowPasses: 2);

            Assert.Less(Quaternion.Angle(before, upper.rotation), 0.01f,
                "an arm already pointing at the target must be left where the clip put it");
        }

        [Test]
        public void TheDeviceEndsUpFacingTheTarget()
        {
            // Down and to the side: the case the fixed hold pose could never answer.
            Vector3 target = new(3f, -4f, 8f);

            ArmAim.Point(upper, lower, pointer, target,
                         shoulderShare: 0.55f, maxDegrees: 75f, weight: 1f, elbowPasses: 2);

            Assert.Less(DegreesOff(target), 0.5f,
                "the beam must land on what the player is looking at, not near it");
        }

        [Test]
        public void OneElbowPassLandsShortAndTheSecondClosesIt()
        {
            Vector3 target = new(3f, -4f, 8f);

            ArmAim.Point(upper, lower, pointer, target,
                         shoulderShare: 0.55f, maxDegrees: 75f, weight: 1f, elbowPasses: 1);
            float afterOne = DegreesOff(target);

            ArmAim.Point(upper, lower, pointer, target,
                         shoulderShare: 0.55f, maxDegrees: 75f, weight: 1f, elbowPasses: 1);
            float afterTwo = DegreesOff(target);

            Assert.Less(afterTwo, afterOne,
                "bending the elbow MOVES the device as well as turning it, so a pass always leaves " +
                "something for the next one — if it did not, the extra pass would be dead weight");
        }

        [Test]
        public void TheSwingStopsAtTheShoulderLimit()
        {
            // Straight behind: what "look over your shoulder" asks of an arm that cannot get there.
            Vector3 target = upper.position - Vector3.forward * 5f;

            Quaternion before = pointer.rotation;
            ArmAim.Point(upper, lower, pointer, target,
                         shoulderShare: 0.55f, maxDegrees: 40f, weight: 1f, elbowPasses: 2);

            Assert.Less(Quaternion.Angle(before, pointer.rotation), 41f,
                "past the limit the arm must stop following rather than going through the chest");
            Assert.Greater(DegreesOff(target), 1f,
                "and it must actually fall short of the target — a clamp that still arrives is no clamp");
        }

        [Test]
        public void WeightZeroLeavesTheAnimatedPoseAlone()
        {
            Quaternion before = pointer.rotation;

            ArmAim.Point(upper, lower, pointer, new Vector3(3f, -4f, 8f),
                         shoulderShare: 0.55f, maxDegrees: 75f, weight: 0f, elbowPasses: 2);

            Assert.Less(Quaternion.Angle(before, pointer.rotation), 0.01f,
                "the aim blends in with the pose it is laid on; at weight 0 it must write nothing");
        }

        [Test]
        public void ConvergenceIsAPointAlongTheLookAndNotADirection()
        {
            Vector3 eye = new(0f, 1.6f, 0f);
            Vector3 point = ArmAim.Convergence(eye, Vector3.forward, 25f);

            Assert.AreEqual(25f, Vector3.Distance(eye, point), 1e-3f,
                "the arm converges on the crosshair at a stated range; a parallel beam would sit " +
                "beside it by however far the wrist is from the eye");
        }
    }
}
