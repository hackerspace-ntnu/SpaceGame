// ANIM-01: a lit Flashlight Gauntlet must strike the held-item pose, on whichever arm it is worn.
//
// Two failures, and the first is the one the report is about. PlayerAimRig wrote the torch's style
// into the animator but eased the Upper Body layer's WEIGHT from `heldStyle` alone, so with empty
// hands the state machine entered the pose on a layer at weight 0 — every parameter right, the
// right state active, and the arm hanging at the player's side. Nothing logs, and the pose "does
// not stick".
//
// The second is what "must work for both the left and right arm" means. Every hold clip is
// right-handed: measured off HumanM@Gun_Aim01, the right hand sits 0.19 up and 0.19 forward of the
// body centre and the left one stays at the hip. So a lamp on the left forearm needs the mirror of
// that pose, and the gauntlet has to say which arm it is on for the rig to know.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Characters;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class TorchPoseTests
    {
        private GameObject holder;
        private PlayerAimRig rig;

        [SetUp]
        public void SetUp()
        {
            holder = new GameObject("Holder");
            rig = holder.AddComponent<PlayerAimRig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (holder != null) Object.DestroyImmediate(holder);
        }

        [Test]
        public void ALitTorchWithEmptyHands_PosesTheBody()
        {
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.OneHanded);

            Assert.AreEqual(ItemGrip.HoldStyle.OneHanded, rig.PoseStyle,
                "the torch's style is the pose when nothing is held");
            Assert.IsTrue(rig.Posing,
                "the layer weight has to come up too — writing the style alone poses an invisible layer");
        }

        [Test]
        public void SwitchingTheTorchOff_DropsThePose()
        {
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.OneHanded);
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.None);

            Assert.IsFalse(rig.Posing, "the arm reads the lamp's state, so off must put it down");
        }

        [Test]
        public void ATorchOnTheLeftArm_MirrorsThePose()
        {
            rig.SetTorchStyle(ItemGrip.Hand.Left, ItemGrip.HoldStyle.OneHanded);

            Assert.IsTrue(rig.Posing);
            Assert.IsTrue(rig.PoseMirrored,
                "the clips raise the RIGHT arm; unmirrored, a left-arm lamp lights the ground");
        }

        [Test]
        public void ATorchOnTheRightArm_DoesNot()
        {
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.OneHanded);

            Assert.IsFalse(rig.PoseMirrored);
        }

        [Test]
        public void EachArmIsReleasedOnItsOwn()
        {
            // A player can wear a lamp on each wrist and switch them off one at a time. A single
            // torch field made the second switch-off drop a pose the first one still wanted.
            rig.SetTorchStyle(ItemGrip.Hand.Left, ItemGrip.HoldStyle.OneHanded);
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.OneHanded);

            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.None);

            Assert.IsTrue(rig.Posing, "the left lamp is still lit");
            Assert.IsTrue(rig.PoseMirrored, "and it is the left arm that now has to come up");
        }

        [Test]
        public void AHeldItemStillWins_AndIsNeverMirrored()
        {
            // The off hand grips items without the body turning round; mirroring for a held item
            // would swap which shoulder every two-handed thing is braced against.
            rig.SetTorchStyle(ItemGrip.Hand.Left, ItemGrip.HoldStyle.OneHanded);
            rig.SetHeldStyle(ItemGrip.HoldStyle.TwoHanded);

            Assert.AreEqual(ItemGrip.HoldStyle.TwoHanded, rig.PoseStyle);
            Assert.IsFalse(rig.PoseMirrored);
        }

        [Test]
        public void TheGearScreenStandsTheBodyDown_TorchOrNot()
        {
            // Relaxed is set while the gear screen is open: the astronaut is the stand the gear
            // sits on. A lit torch must not be the one thing that keeps posing through it.
            rig.SetTorchStyle(ItemGrip.Hand.Right, ItemGrip.HoldStyle.OneHanded);
            rig.Relaxed = true;

            Assert.IsFalse(rig.Posing);
        }
    }
}
