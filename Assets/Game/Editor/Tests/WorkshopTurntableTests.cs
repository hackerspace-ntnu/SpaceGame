using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>A turning room following an angle with no stop in it: what a wheel asks of it.</summary>
    public class WorkshopTurntableTests
    {
        private const float Step = 0.02f;

        private GameObject host;
        private WorkshopTurntable room;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Room under test");
            room = host.AddComponent<WorkshopTurntable>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(host);

        [Test]
        public void ItComesToRestExactlyAtTheAngleItFollows()
        {
            room.Follow(7.3f, instant: false);
            for (int i = 0; i < 200 && room.IsTurning; i++) room.Step(Step);

            Assert.IsFalse(room.IsTurning);
            Assert.AreEqual(7.3f, room.Angle, 1e-4f);
            Assert.AreEqual(7.3f, Quaternion.Angle(Quaternion.identity, host.transform.localRotation), 1e-2f);
        }

        [Test]
        public void ItGoesPastAFullTurnAndBackTheOtherWay()
        {
            room.Follow(400f, instant: true);
            Assert.AreEqual(400f, room.Angle, 1e-4f, "the angle counts turns; it is not wrapped");

            room.Follow(380f, instant: false);
            room.Step(Step);
            Assert.Less(room.Angle, 400f);
            Assert.Greater(room.Angle, 380f, "it swings after the angle, it does not jump to it");
        }

        [Test]
        public void AnInstantFollowMovesItWithoutAStep()
        {
            room.Follow(-123f, instant: true);

            Assert.IsFalse(room.IsTurning);
            Assert.AreEqual(123f, Quaternion.Angle(Quaternion.identity, host.transform.localRotation), 1e-2f);
        }
    }
}
