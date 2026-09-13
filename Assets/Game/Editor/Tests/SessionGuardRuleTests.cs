using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Safety;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The four decisions the session guards make, tested without any of the world they read.
    /// Same split as UnderTerrainRule against UnderTerrainGuard: a decision inlined in Update() is a
    /// decision nothing can test.
    /// </summary>
    public class SessionGuardRuleTests
    {
        // ── StuckScopeRule ────────────────────────────────────────────────────

        [Test]
        public void ANullOwnerIsAbandoned()
        {
            Assert.IsTrue(StuckScopeRule.IsAbandoned(null));
        }

        [Test]
        public void ADestroyedComponentIsAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();
            Object.DestroyImmediate(go);

            Assert.IsTrue(StuckScopeRule.IsAbandoned(behaviour),
                          "a destroyed Unity object compares equal to null while the C# reference lives");
        }

        [Test]
        public void ADisabledComponentIsAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();
            behaviour.enabled = false;

            Assert.IsTrue(StuckScopeRule.IsAbandoned(behaviour));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AnActiveComponentIsNotAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();

            Assert.IsFalse(StuckScopeRule.IsAbandoned(behaviour));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void APlainObjectIsNeverJudged()
        {
            Assert.IsFalse(StuckScopeRule.IsAbandoned(new object()),
                           "nothing can be read off a non-Unity owner, so it gets the benefit of the doubt");
        }

        // ── InputRestoreRule ──────────────────────────────────────────────────

        [Test]
        public void InputIsNotRestoredWhileItWorks()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(
                inputEnabled: true, menuActive: false, cutsceneRunning: false,
                isDead: false, mounted: false, held: false, stuckSeconds: 60f, timeoutSeconds: 5f));
        }

        [Test]
        public void InputIsNotRestoredWhileSomethingLegitimatelyHoldsIt()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: true,  cutsceneRunning: false, isDead: false, mounted: false, held: false, stuckSeconds: 60f, timeoutSeconds: 5f), "a menu is up");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: true,  isDead: false, mounted: false, held: false, stuckSeconds: 60f, timeoutSeconds: 5f), "a cutscene is playing");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: false, isDead: true,  mounted: false, held: false, stuckSeconds: 60f, timeoutSeconds: 5f), "the player is dead");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: false, isDead: false, mounted: true,  held: false, stuckSeconds: 60f, timeoutSeconds: 5f), "the player is riding");

            // The fifth holder. Frozen and Foamed are ten seconds each and Swallowed is about six,
            // and every one of them takes the input through PlayerRagdoll — so a guard that did not
            // ask this handed the controls back MID-EFFECT and the player walked around invisible.
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: false, isDead: false, mounted: false, held: true, stuckSeconds: 60f, timeoutSeconds: 5f), "the player's body is being held down");
        }

        [Test]
        public void InputIsRestoredOnlyAfterTheTimeout()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, false, false, false, false, false, stuckSeconds: 4.9f, timeoutSeconds: 5f),
                           "a frame of disabled input is normal during a handover");
            Assert.IsTrue(InputRestoreRule.ShouldRestore(false, false, false, false, false, false, stuckSeconds: 5f, timeoutSeconds: 5f));
        }

        // ── MountRecoveryRule ─────────────────────────────────────────────────

        [Test]
        public void NoMountMeansNothingToRecover()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(
                haveMount: false, mountAlive: false, mountClaimsRider: false,
                brokenSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void AHealthyMountIsLeftAlone()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(true, mountAlive: true, mountClaimsRider: true, brokenSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void ADeadMountForcesADismountAfterTheTimeout()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(true, mountAlive: false, mountClaimsRider: false, brokenSeconds: 2f, timeoutSeconds: 3f));
            Assert.IsTrue(MountRecoveryRule.ShouldDismount(true, mountAlive: false, mountClaimsRider: false, brokenSeconds: 3f, timeoutSeconds: 3f));
        }

        [Test]
        public void AMountThatNoLongerClaimsTheRiderAlsoCounts()
        {
            Assert.IsTrue(MountRecoveryRule.ShouldDismount(true, mountAlive: true, mountClaimsRider: false, brokenSeconds: 5f, timeoutSeconds: 3f),
                          "the mount let go without the rider being told, which strands them seated on nothing");
        }

        // ── ViewRecoveryRule ──────────────────────────────────────────────────

        [Test]
        public void AVisibleGameIsNotRecovered()
        {
            Assert.IsFalse(ViewRecoveryRule.ShouldRestoreView(anyEnabledCamera: true, blindSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void ABlindMachineIsRecoveredOnlyAfterTheTimeout()
        {
            Assert.IsFalse(ViewRecoveryRule.ShouldRestoreView(false, blindSeconds: 2.9f, timeoutSeconds: 3f),
                           "a single-scene load legitimately has no camera for a moment");
            Assert.IsTrue(ViewRecoveryRule.ShouldRestoreView(false, blindSeconds: 3f, timeoutSeconds: 3f));
        }
    }
}
