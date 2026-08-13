// The rule that decides whether a body has ended up under the world.
//
// Numbers here are the shipped defaults on UnderTerrainGuard: 0.5 m of tolerance before a body
// counts as buried, 1.2 m of clearance when it is put back (matching SpawnPoint.groundClearance,
// which exists because the player capsule's bottom sits ~1 m below the prefab pivot), and a
// -500 m floor.
using NUnit.Framework;
using SpaceGame.World.Safety;

namespace SpaceGame.Tests
{
    public class UnderTerrainRuleTests
    {
        private const float Tolerance = 0.5f;
        private const float Clearance = 1.2f;
        private const float Floor = -500f;

        private static UnderTerrainRule Shipped() => new(Tolerance, Clearance, Floor);

        [Test]
        public void StandingOnTheSurface_IsLeftAlone()
        {
            var verdict = Shipped().Evaluate(bodyY: 100f, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.None, verdict.Action);
        }

        [Test]
        public void WellAboveTheSurface_IsLeftAlone()
        {
            // A jumping player, a flying ornithopter — height is not the guard's business.
            var verdict = Shipped().Evaluate(bodyY: 400f, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.None, verdict.Action);
        }

        [Test]
        public void SunkWithinTolerance_IsLeftAlone()
        {
            // A foot in a mesh seam or a capsule in a dip. Teleporting for this would fight
            // every collider in the world.
            var verdict = Shipped().Evaluate(bodyY: 99.7f, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.None, verdict.Action);
        }

        [Test]
        public void ExactlyAtTheToleranceBoundary_IsLeftAlone()
        {
            // The comparison is strict, so the boundary itself is still "on the ground".
            var verdict = Shipped().Evaluate(bodyY: 100f - Tolerance, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.None, verdict.Action);
        }

        [Test]
        public void BuriedPastTolerance_IsLiftedToTheSurfacePlusClearance()
        {
            var verdict = Shipped().Evaluate(bodyY: 94f, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.Lift, verdict.Action);
            Assert.AreEqual(101.2f, verdict.TargetY, 1e-4f);
        }

        [Test]
        public void FallenFarBelowTerrainThatIsStillSampleable_IsLiftedNotParked()
        {
            // Terrain present always beats the floor: there is a measured surface to return to,
            // so depth is irrelevant.
            var verdict = Shipped().Evaluate(bodyY: -900f, hasTerrain: true, terrainY: 100f);

            Assert.AreEqual(UnderTerrainAction.Lift, verdict.Action);
            Assert.AreEqual(101.2f, verdict.TargetY, 1e-4f);
        }

        [Test]
        public void NoTerrainAboveTheFloor_IsLeftAlone()
        {
            // An interior scene, or the minigame arena. No terrain means nothing to be under.
            var verdict = Shipped().Evaluate(bodyY: 3f, hasTerrain: false, terrainY: 0f);

            Assert.AreEqual(UnderTerrainAction.None, verdict.Action);
        }

        [Test]
        public void NoTerrainBelowTheFloor_IsParked()
        {
            // Fell through an unloaded chunk. Nothing to measure yet, so hold position and let
            // the streamer catch up rather than guessing at a surface.
            var verdict = Shipped().Evaluate(bodyY: -600f, hasTerrain: false, terrainY: 0f);

            Assert.AreEqual(UnderTerrainAction.Park, verdict.Action);
        }

        [Test]
        public void NegativeTuningIsClamped_SoTheGuardCannotInvertIntoTheBugItCatches()
        {
            // A negative tolerance would make "above the surface" read as buried and lift a body
            // that was already fine, forever.
            var rule = new UnderTerrainRule(depthTolerance: -5f, surfaceClearance: -5f, absoluteFloorY: Floor);

            Assert.AreEqual(UnderTerrainAction.None, rule.Evaluate(100f, true, 100f).Action);
            Assert.AreEqual(UnderTerrainAction.Lift, rule.Evaluate(99.9f, true, 100f).Action);
            Assert.AreEqual(100f, rule.Evaluate(99.9f, true, 100f).TargetY, 1e-4f);
        }
    }
}
