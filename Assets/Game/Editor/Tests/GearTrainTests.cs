using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>The flat gear train a GearDoor solves: what meshes, what turns how fast, what locks up.</summary>
    public class GearTrainTests
    {
        private static readonly GearGeometry Geometry = new() { perTooth = 0.03f, addendum = 0.06f, clearance = 0.06f, tolerance = 0.02f };

        private static GearSpec At(float u, float v, int teeth) => new(new Vector2(u, v), teeth);

        [Test]
        public void GearsMeshOnlyAtTheSumOfTheirPitchRadii()
        {
            GearSpec a = At(0f, 0f, 20);
            Assert.AreEqual(GearContact.Mesh, GearTrain.Between(a, At(0.03f * 30f, 0f, 10), Geometry));
            Assert.AreEqual(GearContact.Hit, GearTrain.Between(a, At(0.03f * 30f - 0.1f, 0f, 10), Geometry));
            Assert.AreEqual(GearContact.Free, GearTrain.Between(a, At(2f, 0f, 10), Geometry));
        }

        [Test]
        public void AMeshedGearTurnsTheOtherWayByItsToothRatio()
        {
            var gears = new[] { At(0f, 0f, 20), At(0.03f * 30f, 0f, 10), At(0.03f * 30f, 0.03f * 50f, 40) };
            var speeds = new float[3];

            Assert.IsFalse(GearTrain.Drive(gears, 0, Geometry, speeds));
            Assert.AreEqual(1f, speeds[0], 1e-5f);
            Assert.AreEqual(-2f, speeds[1], 1e-5f);
            Assert.AreEqual(0.5f, speeds[2], 1e-5f);
        }

        [Test]
        public void ThreeGearsInARingLockUpAndStandStill()
        {
            // Three equal gears, every pair meshing: each would have to turn both ways at once.
            float d = 0.03f * 40f;
            var gears = new[] { At(0f, 0f, 20), At(d, 0f, 20), At(d * 0.5f, d * Mathf.Sqrt(3f) * 0.5f, 20) };
            var speeds = new float[3];

            Assert.IsTrue(GearTrain.Drive(gears, 0, Geometry, speeds));
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, speeds);
        }

        [Test]
        public void AMeshedAngleSetsATipAgainstAGapAndStaysThereAsTheyTurn()
        {
            GearSpec a = At(0f, 0f, 20);
            // Off the horizontal, so the contact angle is not a round number.
            GearSpec b = At(Mathf.Sqrt(Mathf.Pow(0.03f * 34f, 2f) - 0.09f), 0.3f, 14);
            const float toothA = 4f, toothB = 7f;

            for (float turned = 0f; turned < 400f; turned += 37f)
            {
                float angleA = 11f + turned;
                float angleB = GearTrain.MeshedAngle(a, 11f, toothA, b, toothB) - turned * 20f / 14f;
                Vector2 towards = b.Centre - a.Centre;
                float contact = Mathf.Atan2(towards.y, towards.x) * Mathf.Rad2Deg;
                float tipA = Mathf.Repeat((contact - angleA - toothA) * 20f / 360f, 1f);
                float tipB = Mathf.Repeat((contact + 180f - angleB - toothB) * 14f / 360f, 1f);
                Assert.AreEqual(0.5f, Mathf.Repeat(tipA + tipB, 1f), 1e-3f, $"after {turned} degrees");
            }
        }
    }
}
