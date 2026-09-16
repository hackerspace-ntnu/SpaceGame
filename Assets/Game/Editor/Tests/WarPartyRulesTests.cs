// The war-party rules from rosters spec §5, without a scene.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class WarPartyRulesTests
    {
        private static readonly WarPartySettings S = WarPartySettings.Default;

        [Test]
        public void Credits_MatchTheSpec()
        {
            Assert.AreEqual(15f, WarPartyRules.CreditFor(Reckoning.Caught, S));
            Assert.AreEqual(4f, WarPartyRules.CreditFor(Reckoning.Defeated, S));
            Assert.AreEqual(0f, WarPartyRules.CreditFor(Reckoning.Abandoned, S));
            Assert.AreEqual(0f, WarPartyRules.CreditFor(Reckoning.None, S));
        }

        [Test]
        public void OnlyDefeat_RaisesTheTier_AndItCaps()
        {
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Defeated, 0, 2));
            Assert.AreEqual(2, WarPartyRules.NextTier(Reckoning.Defeated, 2, 2));
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Caught, 1, 2));
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Abandoned, 1, 2));
        }

        [Test]
        public void Abandons_OnlyBeyondMaxPursuit_OnTheFlat()
        {
            Assert.IsFalse(WarPartyRules.ShouldAbandon(Vector3.zero, new Vector3(1500f, 900f, 0f), 1500f));
            Assert.IsTrue(WarPartyRules.ShouldAbandon(Vector3.zero, new Vector3(1501f, 0f, 0f), 1500f));
        }

        [Test]
        public void CatchUp_JumpsToTheStandoff_AlongThePath()
        {
            var players = new List<Vector3> { Vector3.zero };
            float staging = WarPartyRules.StagingDistance(250f, 50f);

            Assert.IsTrue(WarPartyRules.TryCatchUp(new Vector3(1000f, 5f, 0f), Vector3.zero, staging + 80f,
                                                   players, staging, out Vector3 moved));
            Assert.AreEqual(380f, moved.x, 0.01f);
            Assert.AreEqual(0f, moved.z, 0.01f);
            Assert.AreEqual(5f, moved.y, 0.01f, "height is the record's own");
        }

        [Test]
        public void CatchUp_NeverMovesAPartyAlreadyInside()
        {
            Assert.IsFalse(WarPartyRules.TryCatchUp(new Vector3(350f, 0f, 0f), Vector3.zero, 380f,
                                                    new List<Vector3>(), 300f, out Vector3 moved));
            Assert.AreEqual(new Vector3(350f, 0f, 0f), moved);
        }

        [Test]
        public void CatchUp_NeverLandsWhereAnyPlayerCouldSee()
        {
            // A second player stands right where the jump would land.
            var players = new List<Vector3> { Vector3.zero, new Vector3(400f, 0f, 0f) };

            Assert.IsFalse(WarPartyRules.TryCatchUp(new Vector3(1000f, 0f, 0f), Vector3.zero, 380f,
                                                    players, 300f, out _));
        }

        [Test]
        public void CatchUp_WithAFuzzedLead_StillLandsOutsideTheQuarrysStaging()
        {
            Vector3 quarry = Vector3.zero;
            Vector3 lead = WarPartyRules.TrailFix(quarry, new Vector2(1f, 0f), 80f);  // 80 m toward the party
            var players = new List<Vector3> { quarry };
            const float staging = 300f;

            Assert.IsTrue(WarPartyRules.TryCatchUp(new Vector3(2000f, 0f, 0f), lead, staging + 80f,
                                                   players, staging, out Vector3 moved));
            Assert.GreaterOrEqual(Vector3.Distance(moved, quarry), staging);
        }

        [Test]
        public void TrailFix_StaysWithinFuzz()
        {
            for (int i = 0; i < 200; i++)
            {
                Vector2 disc = Random.insideUnitCircle * 3f;  // deliberately out of range: must be clamped
                Vector3 fix = WarPartyRules.TrailFix(new Vector3(10f, 2f, 10f), disc, 80f);
                Assert.LessOrEqual(Vector3.Distance(new Vector3(10f, 2f, 10f), fix), 80.001f);
            }
        }

        [Test]
        public void FallbackOrigin_IsAtTheDistance_EvenForAZeroDirection()
        {
            Vector3 origin = WarPartyRules.FallbackOrigin(Vector3.zero, Vector2.zero, 400f);
            Assert.AreEqual(400f, new Vector2(origin.x, origin.z).magnitude, 0.01f);
        }

        [Test]
        public void Defeat_NeedsEveryFighterDown_OrAWipe()
        {
            Assert.IsFalse(WarPartyRules.IsDefeated(0, 0, false), "no fighters seated yet is not a defeat");
            Assert.IsFalse(WarPartyRules.IsDefeated(4, 3, false));
            Assert.IsTrue(WarPartyRules.IsDefeated(4, 4, false));
            Assert.IsTrue(WarPartyRules.IsDefeated(0, 0, true));
        }

        [Test]
        public void WipedOut_NeedsNoMemberLeft_AndNoFighterStanding()
        {
            Assert.IsTrue(WarPartyRules.IsWipedOut(0, 0));
            Assert.IsFalse(WarPartyRules.IsWipedOut(0, 1), "a dismounted rider still fighting keeps the party in the field");
            Assert.IsFalse(WarPartyRules.IsWipedOut(2, 0), "a riderless mount is still a spawned member");
        }

        [Test]
        public void Caught_OnlyByThisParty()
        {
            Assert.IsTrue(WarPartyRules.IsCaughtBy("w1", "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy("w2", "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy(null, "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy(null, ""));
        }
    }
}
