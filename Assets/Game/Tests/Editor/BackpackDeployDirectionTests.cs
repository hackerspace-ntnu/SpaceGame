using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rule the recurring "it deployed behind me" bug broke: whatever the aim source says, the
    /// pack goes down in front of the body.
    /// </summary>
    public class BackpackDeployDirectionTests
    {
        private static void AssertInFront(Vector3 direction, Vector3 bodyForward)
        {
            Assert.Greater(Vector3.Dot(direction, bodyForward), 0f,
                           $"{direction} is not in front of a body facing {bodyForward}");
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        [TestCase(37.5f)]
        public void AgreeingAim_DeploysInFrontAtEveryYaw(float yaw)
        {
            Vector3 body = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // A first-person camera: the body's yaw plus pitch of its own.
            Vector3 aim = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(30f, 0f, 0f) * Vector3.forward;

            Vector3 direction = BackpackController.DeployDirection(aim, body, out bool inverted);

            Assert.IsFalse(inverted, "a pitched first-person camera must not trip the guard");
            AssertInFront(direction, body);
            Assert.AreEqual(0f, direction.y, 1e-4f, "the drop direction is flattened to the ground plane");
            Assert.AreEqual(1f, direction.magnitude, 1e-4f);
        }

        [Test]
        public void InvertedAim_IsOverriddenByTheBody()
        {
            Vector3 body = Vector3.forward;
            Vector3 direction = BackpackController.DeployDirection(-Vector3.forward, body, out bool inverted);

            Assert.IsTrue(inverted, "an aim source pointing backwards must be reported, not obeyed");
            AssertInFront(direction, body);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(214f)]
        public void InvertedAim_IsOverriddenAtEveryYaw(float yaw)
        {
            Vector3 body = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            Vector3 direction = BackpackController.DeployDirection(-body, body, out bool inverted);

            Assert.IsTrue(inverted);
            AssertInFront(direction, body);
        }

        [Test]
        public void LookingStraightDown_FallsBackToTheBody()
        {
            // Straight down has no horizontal component at all, so there is nothing to flatten to.
            Vector3 body = Vector3.right;
            Vector3 direction = BackpackController.DeployDirection(Vector3.down, body, out bool inverted);

            Assert.IsFalse(inverted);
            AssertInFront(direction, body);
        }

        [Test]
        public void SidewaysAim_IsLeftAlone()
        {
            // 90 degrees off is not an inversion. The guard exists to catch a flipped aim source,
            // not to drag the drop point back onto the body's nose.
            Vector3 body = Vector3.forward;
            Vector3 direction = BackpackController.DeployDirection(Vector3.right, body, out bool inverted);

            Assert.IsFalse(inverted, "a large but sane disagreement must not be overridden");
            Assert.AreEqual(Vector3.right, direction);
        }

        [Test]
        public void DegenerateBody_StillProducesAUsableDirection()
        {
            Vector3 direction = BackpackController.DeployDirection(Vector3.zero, Vector3.zero, out _);

            Assert.AreEqual(1f, direction.magnitude, 1e-4f, "never a zero-length drop direction");
        }
    }
}
