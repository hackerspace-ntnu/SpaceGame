// The one copy of the gauntlet seating arithmetic, shared by the real worn gauntlet and the body
// screen's ghost of it. The model is aligned to the elbow→wrist line, its dorsal face turned to
// the back of the arm, and the LEFT arm gets a negative X scale rather than a mirrored model —
// the base's hinges are on one flank, and a plain rotation would put them on the wrong one.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class ForearmSeatTests
    {
        private GameObject forearm;
        private GameObject hand;
        private GameObject instance;

        // A right forearm laid along world +X with the hand 0.4 m out, thumb side up.
        private static readonly Vector3 Elbow = new(0f, 1.2f, 0f);
        private static readonly Vector3 Wrist = new(0.4f, 1.2f, 0f);
        private static readonly Quaternion Grip = Quaternion.identity;   // grip up = world up = thumb side

        [SetUp]
        public void SetUp()
        {
            forearm = new GameObject("LowerArm");
            forearm.transform.position = Elbow;

            hand = new GameObject("Hand");
            hand.transform.position = Wrist;

            instance = new GameObject("Gauntlet");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(hand);
            Object.DestroyImmediate(forearm);
        }

        private GauntletFit Fit(float cuff, float length, float wristGap, float roll)
        {
            // GauntletFit is [DisallowMultipleComponent] and the two-armed tests below seat the same
            // instance twice, so reuse the component: a second AddComponent logs an error and
            // answers null.
            GauntletFit fit = instance.GetComponent<GauntletFit>();
            if (fit == null) fit = instance.AddComponent<GauntletFit>();

            var so = new SerializedObject(fit);
            so.FindProperty("cuffScale").floatValue = cuff;
            so.FindProperty("lengthScale").floatValue = length;
            so.FindProperty("wristGap").floatValue = wristGap;
            so.FindProperty("rollDegrees").floatValue = roll;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fit;
        }

        [Test]
        public void SitsAWristGapBackFromTheHandAlongTheArm()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            // toHand is world +X, so the origin backs off the wrist toward the elbow by the gap.
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.38f, 1.2f, 0f), instance.transform.position), 1e-4f);
            Assert.AreEqual(forearm.transform, instance.transform.parent);
        }

        [Test]
        public void PointsItsOwnForwardDownTheArm()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, instance.transform.forward), 1e-2f,
                "the model's +Z is the elbow→wrist line");
        }

        [Test]
        public void TheLeftArmIsMirroredOnX()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: true, Fit(1f, 1f, 0.02f, 0f));
            Assert.Less(instance.transform.localScale.x, 0f, "the left arm mirrors rather than rotating");

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));
            Assert.Greater(instance.transform.localScale.x, 0f);
        }

        [Test]
        public void WidthAndLengthScaleSeparately()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1.5f, 2f, 0.02f, 0f));

            Vector3 scale = instance.transform.localScale;
            Assert.AreEqual(1.5f, scale.x, 1e-4f, "across the arm");
            Assert.AreEqual(1.5f, scale.y, 1e-4f, "across the arm");
            Assert.AreEqual(2f, scale.z, 1e-4f, "along it");
        }

        [Test]
        public void TheDorsalFaceIsTurnedOppositeWaysOnTheTwoArms()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));
            Vector3 rightUp = instance.transform.up;

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: true, Fit(1f, 1f, 0.02f, 0f));

            Assert.AreEqual(0f, Vector3.Distance(-rightUp, instance.transform.up), 1e-3f,
                "with the same thumb side, the two arms' backs face opposite ways");
        }

        [Test]
        public void ADegenerateArmStillGetsAUsablePose()
        {
            // Hand exactly on the elbow: toHand is undefined and the cross product collapses.
            hand.transform.position = Elbow;

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            Assert.IsFalse(float.IsNaN(instance.transform.rotation.x), "no NaN pose from a zero-length arm");
        }
    }
}
