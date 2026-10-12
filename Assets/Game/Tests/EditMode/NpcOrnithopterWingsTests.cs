using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The NPC wing presenter measures motion off the transform, which a dynamic body moves only on
    /// physics steps: at a frame rate above the physics rate some frames see no movement at all.
    /// </summary>
    public class NpcOrnithopterWingsTests
    {
        private const float Dt = 1f / 60f;
        private GameObject craft;
        private NpcOrnithopterWings wings;

        [SetUp]
        public void SetUp()
        {
            craft = new GameObject("NpcCraft");
            wings = craft.AddComponent<NpcOrnithopterWings>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(craft);

        // The body moves by two frames' worth on every other frame: physics at half the frame rate.
        private void FlySteppedEveryOtherFrame(Vector3 velocity, int frames, System.Action<int> afterFrame = null)
        {
            for (int i = 0; i < frames; i++)
            {
                if (i % 2 == 1) craft.transform.position += velocity * (2f * Dt);
                wings.Tick(Dt);
                afterFrame?.Invoke(i);
            }
        }

        [Test]
        public void ACruiseSteppedEveryOtherFrame_ReadsASteadySpeed()
        {
            float min = float.MaxValue, max = float.MinValue;
            FlySteppedEveryOtherFrame(new Vector3(0f, 0f, 25f), 180, i =>
            {
                if (i < 90) return;
                min = Mathf.Min(min, wings.Airspeed);
                max = Mathf.Max(max, wings.Airspeed);
            });

            Assert.AreEqual(25f, min, 1f, "a frame without a physics step read as slowing down");
            Assert.AreEqual(25f, max, 1f, "a frame after a physics step read as speeding up");
        }

        [Test]
        public void ANormalDescentSteppedEveryOtherFrame_NeverReadsAsAWreck()
        {
            float minSpread = float.MaxValue;
            FlySteppedEveryOtherFrame(new Vector3(0f, -8f, 20f), 240, i =>
            {
                if (i >= 120) minSpread = Mathf.Min(minSpread, wings.WingSpread);
            });

            Assert.AreEqual(1f, minSpread, 1e-3f, "an 8 m/s descent folded the wings like a wreck");
        }

        [Test]
        public void ACraftThatStops_ReadsAsParked()
        {
            FlySteppedEveryOtherFrame(new Vector3(0f, 0f, 25f), 60);
            FlySteppedEveryOtherFrame(Vector3.zero, 120);

            Assert.AreEqual(0f, wings.Airspeed, 0.1f);
        }
    }
}
