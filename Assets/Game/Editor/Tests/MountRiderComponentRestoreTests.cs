// Why a dismount restores the rider's captured component state instead of switching everything on.
//
// MountModule runs on EVERY machine: MountNetworkSync.OnMountedElsewhere/OnDismountedElsewhere call
// TryMount/Dismount locally so a peer's mount is reproduced here. That means a client also runs
// DisableRiderComponentsForMount / RestoreRiderComponentsAfterDismount against SOMEBODY ELSE'S
// player — a body whose PlayerMovement, PlayerLook and Interactor are deliberately off, because
// PlayerController.DisablePlayer switches them off on every machine that does not own it.
//
// Restoring them to `true` therefore switched a remote player's local-input components ON, and both
// of them then misbehave for the whole session:
//
//   • PlayerLook.LateUpdate re-locks and hides the cursor EVERY FRAME while it is enabled, with no
//     ownership test. So a remote player dismounting steals the cursor from whatever this machine
//     was doing — including the death screen, which is why the Respawn button became unclickable.
//   • PlayerMovement.FixedUpdate writes linearVelocity into a body it does not own, which netcode
//     keeps kinematic, and Unity logs "Setting linear velocity of a kinematic body is not
//     supported." on every physics step. EnsureMovableBody cannot rescue it — it bails on exactly
//     this case, by design, since freeing a remote body would be worse.
//
// Both were observed together in a 2-player session: the client died, the host mounted and
// dismounted, and the client's log then carried 1643 kinematic-velocity warnings until it quit.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class MountRiderComponentRestoreTests
    {
        private GameObject mountObject;
        private GameObject riderObject;

        private MountModule mount;
        private PlayerMovement movement;
        private PlayerLook look;
        private Interactor interactor;

        [SetUp]
        public void SetUp()
        {
            mountObject = new GameObject("Mount");
            mount = mountObject.AddComponent<MountModule>();

            // Edit mode never advances Time.time, so the authored 0.25 s cooldown reads as
            // "0 >= 0.25" and IsAvailableForMount refuses every mount — every test here then
            // fails on its first TryMount, before reaching the restore behaviour it is about.
            // Same workaround as MountSeatAddressingTests.Fit.
            var cooldown = new UnityEditor.SerializedObject(mount);
            cooldown.FindProperty("mountCooldown").floatValue = 0f;
            cooldown.ApplyModifiedPropertiesWithoutUndo();

            riderObject = new GameObject("Rider");
            movement = riderObject.AddComponent<PlayerMovement>();
            look = riderObject.AddComponent<PlayerLook>();
            interactor = riderObject.AddComponent<Interactor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (mountObject != null) Object.DestroyImmediate(mountObject);
            if (riderObject != null) Object.DestroyImmediate(riderObject);
        }

        private void SetRiderComponents(bool enabled)
        {
            movement.enabled = enabled;
            look.enabled = enabled;
            interactor.enabled = enabled;
        }

        [Test]
        public void ARemoteRider_ComesOutOfADismountStillDisabled()
        {
            // What a peer's player looks like on this machine: present, replicated, and switched off
            // by PlayerController.DisablePlayer because we do not own it.
            SetRiderComponents(false);

            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");
            mount.Dismount();

            Assert.IsFalse(movement.enabled,
                "A remote player's PlayerMovement was switched on by the dismount. It then drives a " +
                "kinematic body it does not own, once per physics step, for the rest of the session.");
            Assert.IsFalse(look.enabled,
                "A remote player's PlayerLook was switched on by the dismount. Its LateUpdate re-locks " +
                "the cursor every frame, so this machine loses the pointer — the death screen included.");
            Assert.IsFalse(interactor.enabled,
                "A remote player's Interactor was switched on by the dismount, letting somebody " +
                "else's body interact with the world on this machine.");
        }

        [Test]
        public void TheOwningRider_StillGetsTheirControlsBack()
        {
            // The case the restore exists for, which capturing must not break.
            SetRiderComponents(true);

            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");

            Assert.IsFalse(movement.enabled, "Mounting is what takes the controls away.");
            Assert.IsFalse(look.enabled, "Mounting is what takes the controls away.");
            Assert.IsFalse(interactor.enabled, "Mounting is what takes the controls away.");

            mount.Dismount();

            Assert.IsTrue(movement.enabled, "Dismounting must hand the rider back their movement.");
            Assert.IsTrue(look.enabled, "Dismounting must hand the rider back their look.");
            Assert.IsTrue(interactor.enabled, "Dismounting must hand the rider back their interactor.");
        }
    }
}
