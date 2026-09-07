// The site score is what decides where the Clanker town goes, and it is the one part of the
// builder that can be wrong without anything looking wrong -- a town on a slope is still a town.
// So it is checked against ground whose shape is known exactly.
//
// The recipe test reads the ASSET, not the class: a serialized slot keeps whatever was last saved
// into it, and the builder's failure mode is an empty slot the generator silently skips.
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class ClankerSettlementTests
    {
        private static readonly Vector2 Centre = new Vector2(1000f, 1000f);
        private const float Radius = 150f;

        [Test]
        public void FlatGroundScoresZero()
        {
            bool ok = SettlementSiteScore.TryEvaluate((x, z) => 40f, Centre, Radius,
                                                      out float range, out float centreHeight);
            Assert.IsTrue(ok);
            Assert.AreEqual(0f, range, 1e-4f);
            Assert.AreEqual(40f, centreHeight, 1e-4f);
        }

        [Test]
        public void UniformSlopeScoresItsRise()
        {
            // 10 % grade along X: the rim samples at ±radius differ by 2 × radius × 0.1.
            bool ok = SettlementSiteScore.TryEvaluate((x, z) => x * 0.1f, Centre, Radius,
                                                      out float range, out _);
            Assert.IsTrue(ok);
            Assert.AreEqual(2f * Radius * 0.1f, range, 1e-3f);
        }

        [Test]
        public void ABumpInsideTheDiscIsSeen()
        {
            // A 5 m knoll on the middle ring, away from the axes so only a ring sample lands on it.
            float middleRing = Radius * 2f / SettlementSiteScore.Rings;
            float a = Mathf.PI * 2f / SettlementSiteScore.SamplesPerRing;   // the second sample's angle
            var knoll = Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * middleRing;

            bool ok = SettlementSiteScore.TryEvaluate(
                (x, z) => Vector2.Distance(new Vector2(x, z), knoll) < 1f ? 5f : 0f,
                Centre, Radius, out float range, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(5f, range, 1e-4f);
        }

        [Test]
        public void ADiscOffTheEdgeOfTheGroundIsNotASite()
        {
            // Ground ends 100 m east of the centre; the rim at +radius falls off it.
            bool ok = SettlementSiteScore.TryEvaluate(
                (x, z) => x > Centre.x + 100f ? (float?)null : 0f,
                Centre, Radius, out _, out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void TheFlattestOfTwoCandidatesWins()
        {
            Func<float, float, float?> ground = (x, z) => x < 1500f ? Mathf.Sin(x * 0.05f) * 3f : 0.2f;

            SettlementSiteScore.TryEvaluate(ground, new Vector2(1000f, 1000f), Radius, out float rough, out _);
            SettlementSiteScore.TryEvaluate(ground, new Vector2(2000f, 1000f), Radius, out float level, out _);

            Assert.Less(level, rough);
        }

        [Test]
        public void RecipeAssetHasEveryBuildingSlotFilled()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RobotSettlementRecipe>(ClankerSettlementBuilder.RecipePath);
            if (recipe == null)
                Assert.Ignore($"{ClankerSettlementBuilder.RecipePath} has not been built yet " +
                              "(Tools > SpaceGame > Settlements > Build Mock Clanker Settlement).");

            Assert.IsNotNull(recipe.shieldGeneratorPrefab, "centre building");
            Assert.IsNotNull(recipe.barracksPrefab, "barracks");
            Assert.IsNotNull(recipe.ecoHubPrefab, "eco hub");
            Assert.IsNotNull(recipe.satelliteDishPrefab, "mid ring");
            // No rocks until the rock prefabs are rebuilt with colliders and a sane scale (see the
            // builder and DEFECTS.md). A rock slot here means someone put them back without that.
            Assert.IsEmpty(recipe.rockPrefabs, "rocks are excluded on purpose");
            Assert.IsNotEmpty(recipe.robotPrefabs, "garrison");
            foreach (GameObject robot in recipe.robotPrefabs)
                Assert.IsNotNull(robot, "a garrison slot is empty");

            // The mounted Clankers ride on their own slots, so the count is a promise rather
            // than a roll of the patrol-group dice.
            Assert.GreaterOrEqual(recipe.outriderTotal, ClankerSettlementBuilder.OutriderCount, "outriders");
            Assert.IsNotEmpty(recipe.outriderPrefabs, "outrider prefab");
            foreach (GameObject outrider in recipe.outriderPrefabs)
                Assert.IsNotNull(outrider, "an outrider slot is empty");

            // The inner ring must clear the centre building: (87 m refinery + padding) / 2 plus a
            // relay outpost's own clearance is ~61 m. Below that the ring's slots are rejected one
            // by one and the town comes out as a tower with nothing round it.
            Assert.Greater(recipe.innerRadius, 61f);
            Assert.Less(recipe.innerRadius, recipe.midRadius);
            Assert.Less(recipe.midRadius, recipe.outerRadius);
        }
    }
}
