using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;
using UnityEditor;
using UnityEngine;

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
            Assert.AreEqual(Tuning().fallSeconds, KnockdownPolicy.Seconds(fall, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_Blast_GrowsWithLowHealth_AndKnockback()
        {
            KnockdownTuning t = Tuning();
            float moderate = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Blast, 0f, 0.7f, 5f), t);
            float nearlyDead = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Blast, 0f, 0.1f, 5f), t);
            float shoved = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Blast, 0f, 0.7f, 15f), t);

            Assert.GreaterOrEqual(moderate, t.minSeconds);
            Assert.Greater(nearlyDead, moderate, "less health left must keep you down longer");
            Assert.Greater(shoved, moderate, "more knockback must keep you down longer");
        }

        [Test]
        public void Seconds_NeverExceedTheMaximum()
        {
            var brutal = new KnockdownEvent(RagdollCause.Blast, 0.9f, 0.01f, 80f);
            Assert.AreEqual(Tuning().maxSeconds, KnockdownPolicy.Seconds(brutal, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_Blast_AlwaysKnocksDown_EvenWithNoDamage()
        {
            // The gauntlet deals no damage; its whole price is the knockdown.
            KnockdownTuning t = Tuning();
            float puff = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Blast, 0f, 1f, 0f), t);
            Assert.AreEqual(t.minSeconds, puff, 1e-4f);
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

        // A body knocked over from standing takes about 0.4 s just to reach the ground (hips ~1.4 m
        // up on a 3 m body, gravity 18 m/s²) and longer to topple. A grace shorter than that stands
        // every knockdown up mid-fall: it half-collapses and snaps back, which reads as spasming.
        private const float FallFromHipsSeconds = 0.4f;

        [Test]
        public void TheDefaultGrace_LetsAKnockedBodyLandBeforeItStandsUp()
        {
            float grace = Tuning().settleGraceSeconds;
            Assert.IsFalse(KnockdownPolicy.ShouldStandUp(now: 1f + FallFromHipsSeconds, standAt: 1f, atRest: false, graceSeconds: grace),
                           "still falling after its short down-time: it must land and settle first");
            Assert.LessOrEqual(grace, 2f, "the grace is still a ceiling for a body wedged against a rock (GDC-L1-FEEL-0002)");
        }

        [Test]
        public void NoPrefab_OverridesTheGraceWithOneTooShortToLand()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (RagdollController ragdoll in prefab.GetComponentsInChildren<RagdollController>(true))
                    Assert.Greater(ragdoll.Tuning.settleGraceSeconds, FallFromHipsSeconds,
                                   $"{path}: a grace this short stands every knockdown up mid-fall");
            }
        }
    }
}
