using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.EditorTools
{
    /// <summary>How long a body stays down, as a function of what put it there.</summary>
    public class KnockdownPolicyTests
    {
        private static KnockdownTuning Tuning() => new KnockdownTuning();

        [Test]
        public void Seconds_Fall_IsFlat()
        {
            var fall = new KnockdownEvent(RagdollCause.Fall, 0.5f, 0.1f, 30f);
            Assert.AreEqual(1f, KnockdownPolicy.Seconds(fall, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_SmallHitAtFullHealth_DoesNotKnockDown()
        {
            // An assault-rifle round: 5% of health, 95% left, no knockback.
            var bullet = new KnockdownEvent(RagdollCause.Hit, 0.05f, 0.95f, 0f);
            Assert.AreEqual(0f, KnockdownPolicy.Seconds(bullet, Tuning()));
        }

        [Test]
        public void Seconds_Hit_GrowsWithDamage_LowHealth_AndKnockback()
        {
            KnockdownTuning t = Tuning();
            float moderate = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.7f, 0f), t);
            float nearlyDead = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.1f, 0f), t);
            float shoved = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.7f, 15f), t);

            Assert.GreaterOrEqual(moderate, t.minSeconds);
            Assert.Greater(nearlyDead, moderate, "less health left must keep you down longer");
            Assert.Greater(shoved, moderate, "more knockback must keep you down longer");
        }

        [Test]
        public void Seconds_NeverExceedTheMaximum()
        {
            var brutal = new KnockdownEvent(RagdollCause.Hit, 0.9f, 0.01f, 80f);
            Assert.AreEqual(Tuning().maxSeconds, KnockdownPolicy.Seconds(brutal, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_Blast_AlwaysKnocksDown_EvenWithNoDamage()
        {
            // The gauntlet deals no damage; its whole price is the knockdown.
            var puff = new KnockdownEvent(RagdollCause.Blast, 0f, 1f, 2f);
            Assert.AreEqual(Tuning().minSeconds, KnockdownPolicy.Seconds(puff, Tuning()), 1e-4f);
        }

        [Test]
        public void Immune_BlocksHitsJustAfterStandingUp_ButNotBlasts()
        {
            KnockdownTuning t = Tuning();
            Assert.IsTrue(KnockdownPolicy.Immune(RagdollCause.Hit, 0.2f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Hit, t.reknockImmunitySeconds + 0.01f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Blast, 0.2f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Fall, 0.2f, t));
        }

        [Test]
        public void ShouldStandUp_WaitsForTheTime_ThenForRest_ThenForTheGrace()
        {
            Assert.IsFalse(KnockdownPolicy.ShouldStandUp(now: 0.9f, standAt: 1f, atRest: true, graceSeconds: 1.5f),
                           "not before the down-time is up, even at rest");
            Assert.IsTrue(KnockdownPolicy.ShouldStandUp(1.0f, 1f, atRest: true, graceSeconds: 1.5f));
            Assert.IsFalse(KnockdownPolicy.ShouldStandUp(2.0f, 1f, atRest: false, graceSeconds: 1.5f),
                           "still tumbling, inside the grace");
            Assert.IsTrue(KnockdownPolicy.ShouldStandUp(2.5f, 1f, atRest: false, graceSeconds: 1.5f),
                          "wedged against a rock: the grace ends it");
        }
    }
}
