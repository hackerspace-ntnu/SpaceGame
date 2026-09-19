// Rosters spec §5.4: fighting off a war party never costs goodwill for the quarry or anyone on their side.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SelfDefenceRulesTests
    {
        private FactionDefinition crew, redTeam;

        [SetUp]
        public void SetUp()
        {
            crew = ScriptableObject.CreateInstance<FactionDefinition>();
            redTeam = ScriptableObject.CreateInstance<FactionDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(crew);
            Object.DestroyImmediate(redTeam);
        }

        [Test] public void TheQuarry_IsExempt() =>
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", crew, "p1", crew));

        [Test] public void ACrewmate_IsExempt() =>
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", crew, "p2", crew));

        [Test] public void AnotherTeam_IsNot() =>
            Assert.IsFalse(SelfDefenceRules.IsExempt("p1", crew, "p2", redTeam));

        [Test] public void NotAWarParty_NobodyIs() =>
            Assert.IsFalse(SelfDefenceRules.IsExempt("", crew, "p1", crew));

        [Test] public void QuarryOffline_OnlyTheQuarryIs()
        {
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", null, "p1", crew));
            Assert.IsFalse(SelfDefenceRules.IsExempt("p1", null, "p2", null));
        }
    }
}
