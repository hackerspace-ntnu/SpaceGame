// Holding storms off, without a world: a suppressor holds off exactly the storms whose sand reaches its
// circle, and stops the moment it is unregistered -- which is what makes picking a storm ward up bring
// the weather back.
using NUnit.Framework;
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class StormSuppressorsTests
    {
        private sealed class Stake : IStormSuppressor
        {
            public Vector3 SuppressionCentre { get; set; }
            public float SuppressionRadius { get; set; }
        }

        // Sand ends at radius + feather = 150 m from the centre.
        private static readonly StormFootprint Storm = new StormFootprint
        {
            Kind = StormShapeKind.Cell,
            Center = Vector2.zero,
            Heading = Vector2.up,
            Radius = 100f,
            EdgeFeather = 50f,
            Height = 200f,
            HeightFeather = 80f,
        };

        private Stake stake;

        [SetUp]
        public void SetUp() => stake = new Stake { SuppressionRadius = 40f };

        [TearDown]
        public void TearDown() => StormSuppressors.Unregister(stake);

        [Test]
        public void NothingIsHeldOffWithNoSuppressor()
        {
            Assert.IsFalse(StormSuppressors.Suppresses(Storm));
        }

        [Test]
        public void AStormReachingTheCircleIsHeldOffAndOneShortOfItIsNot()
        {
            StormSuppressors.Register(stake);

            stake.SuppressionCentre = new Vector3(185f, 0f, 0f);
            Assert.IsTrue(StormSuppressors.Suppresses(Storm));

            stake.SuppressionCentre = new Vector3(195f, 0f, 0f);
            Assert.IsFalse(StormSuppressors.Suppresses(Storm));
        }

        [Test]
        public void TakingTheSuppressorAwayLetsTheStormBack()
        {
            stake.SuppressionCentre = Vector3.zero;
            StormSuppressors.Register(stake);
            Assert.IsTrue(StormSuppressors.Suppresses(Storm));

            StormSuppressors.Unregister(stake);
            Assert.IsFalse(StormSuppressors.Suppresses(Storm));
        }

        [Test]
        public void RegisteringTwiceDoesNotNeedTwoUnregisters()
        {
            stake.SuppressionCentre = Vector3.zero;
            StormSuppressors.Register(stake);
            StormSuppressors.Register(stake);
            StormSuppressors.Unregister(stake);
            Assert.IsFalse(StormSuppressors.Suppresses(Storm));
        }
    }
}
