// The rotation rule (settlement expeditions spec §3.1, §3.3): a settlement raises its next band when none
// is out, or when the only one out has turned for home; never while two are out. Pure, no Editor needed.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents.Expeditions;

namespace SpaceGame.EditorTools
{
    public class RotationTests
    {
        private static ExpeditionRecord Band(ExpeditionPhase phase, int stageIndex = ExpeditionRecord.NotStarted) => new ExpeditionRecord
        {
            phase = phase,
            stageIndex = stageIndex,
            stages = new[]
            {
                new StageRecord { kind = StageKind.Travel, minutes = StageRecord.NoLimit },
                new StageRecord { kind = StageKind.ReturnHome, minutes = StageRecord.NoLimit },
            },
        };

        [Test]
        public void NextBandDue_WhenNoneOut()
        {
            Assert.IsTrue(ExpeditionRules.NextBandDue(new List<ExpeditionRecord>()), "a fresh settlement");
            Assert.IsTrue(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Home), Band(ExpeditionPhase.Lost) }),
                "bands that are home or lost are not out");
            Assert.IsTrue(ExpeditionRules.NextBandDue(0, false));
        }

        [Test]
        public void NextBandDue_WhenOnlyBandEntersReturnHome()
        {
            Assert.IsFalse(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Out, 0) }), "still travelling out");
            Assert.IsFalse(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Departing) }), "just leaving");
            Assert.IsTrue(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Out, 1) }), "turned for home");
            Assert.IsTrue(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Returning, 2) }), "walking in");
            Assert.IsTrue(ExpeditionRules.NextBandDue(1, true));
            Assert.IsFalse(ExpeditionRules.NextBandDue(1, false));
        }

        [Test]
        public void NextBandDue_NeverWithTwoOut()
        {
            Assert.IsFalse(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Out, 1), Band(ExpeditionPhase.Announced) }),
                "the next band is already chosen");
            Assert.IsFalse(ExpeditionRules.NextBandDue(new[] { Band(ExpeditionPhase.Out, 1), Band(ExpeditionPhase.Returning, 2) }),
                "two homebound bands are still two out");
            Assert.IsFalse(ExpeditionRules.NextBandDue(2, true));
        }
    }
}
