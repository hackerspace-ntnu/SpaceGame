// The small rules the humanoid animation system hangs its multiplayer behaviour on, each a pure
// function precisely so it can be pinned here without a session: when an NPC's blow lands, how a
// variant and an arm cross the wire, and which idle a body stands in at a given server time.
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class CharacterActionRuleTests
    {
        [Test]
        public void ABlowWaitsForContactLandsInReachAndDropsWhenLateOrDodged()
        {
            const float reachSqr = 9f;
            Assert.AreEqual(CloseCombatModule.BlowOutcome.Wait, CloseCombatModule.Blow(0.9f, 1f, 0.25f, 1f, reachSqr),
                            "before its contact frame the wind-up is only a warning");
            Assert.AreEqual(CloseCombatModule.BlowOutcome.Land, CloseCombatModule.Blow(1.1f, 1f, 0.25f, 1f, reachSqr));
            Assert.AreEqual(CloseCombatModule.BlowOutcome.Drop, CloseCombatModule.Blow(1.1f, 1f, 0.25f, 16f, reachSqr),
                            "a target that stepped out of reach during the wind-up dodged it");
            Assert.AreEqual(CloseCombatModule.BlowOutcome.Drop, CloseCombatModule.Blow(2f, 1f, 0.25f, 1f, reachSqr),
                            "a blow nobody ticked near its contact frame was interrupted — knocked down, killed, " +
                            "out-prioritised — and must not land seconds later");
        }

        [Test]
        public void VariantAndArmSurviveTheWire()
        {
            foreach (ItemGrip.Hand? arm in new ItemGrip.Hand?[] { null, ItemGrip.Hand.Left, ItemGrip.Hand.Right })
            {
                for (int variant = 0; variant < 5; variant++)
                {
                    (int v, ItemGrip.Hand? a) = CharacterActions.UnpackVariant(CharacterActions.PackVariant(variant, arm));
                    Assert.AreEqual(variant, v);
                    Assert.AreEqual(arm, a, "a stab from the left wrist would mirror onto the wrong arm");
                }
            }
        }

        [Test]
        public void IdleIsAFunctionOfSeedAndServerTimeAlone()
        {
            for (int seed = 1; seed < 50; seed++)
            {
                int idle = IdleVariation.IdleAt(seed, 1234.5, 14f, 4);
                Assert.That(idle, Is.InRange(0, 3));
                Assert.AreEqual(idle, IdleVariation.IdleAt(seed, 1234.5, 14f, 4),
                                "two machines asking at the same server time must see the same idle");
            }
        }
    }
}
