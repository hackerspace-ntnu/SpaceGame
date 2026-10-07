// A powered Item Scanner must raise the arm it is worn on, the way a lit Flashlight Gauntlet does.
//
// Both are forearm devices that are useless with the arm down — the torch lights the wearer's
// boots, the scanner points its screen at the ground — and both ask for it through the one channel
// PlayerAimRig has for a worn gauntlet, PlayerAimRig.SetWornStyle. The failure this guards is the
// silent one: the scanner switching on, the screen lighting, and the arm never moving, with a
// clean console.
//
// Driven through RestoreItemState rather than Present, because that is the public seam: it is the
// same PoseArm call, and it is also the path a reload takes, where a scanner that comes back
// powered used to come back with its arm at its side.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class ScannerPoseTests
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ItemScanner.prefab";

        private GameObject holder;
        private GameObject scanner;
        private PlayerAimRig rig;
        private ItemScannerArtifact artifact;

        [SetUp]
        public void SetUp()
        {
            holder = new GameObject("Holder");
            rig = holder.AddComponent<PlayerAimRig>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "the item scanner prefab moved");

            scanner = Object.Instantiate(prefab);
            artifact = scanner.GetComponent<ItemScannerArtifact>();
            Assert.IsNotNull(artifact, "the scanner prefab lost its artifact component");

            artifact.Worn = true;
            artifact.WornOn = ItemGrip.Hand.Right;
            artifact.OnEquipped(holder);
        }

        [TearDown]
        public void TearDown()
        {
            if (scanner != null) Object.DestroyImmediate(scanner);
            if (holder != null) Object.DestroyImmediate(holder);
        }

        [Test]
        public void AScannerIsEquippedWithItsArmDown()
        {
            Assert.AreEqual(ItemGrip.HoldStyle.None, rig.PoseStyle,
                "an unpowered scanner asks for no pose — it is off until the wearer switches it on");
        }

        [Test]
        public void APoweredScanner_RaisesTheArmItIsWornOn()
        {
            var state = new ItemState();
            state.Set("on", true);

            artifact.RestoreItemState(state);

            Assert.AreNotEqual(ItemGrip.HoldStyle.None, rig.PoseStyle,
                "a powered scanner has to bring the forearm up or its screen faces the ground");
            Assert.IsTrue(rig.Posing,
                "and the layer WEIGHT has to come up too — writing the style alone poses an " +
                "invisible layer (ANIM-01)");
            Assert.IsFalse(rig.PoseMirrored, "worn on the right arm, so the pose plays unmirrored");
        }

        [Test]
        public void ScannerOnTheLeftArm_MirrorsThePose()
        {
            artifact.WornOn = ItemGrip.Hand.Left;

            var state = new ItemState();
            state.Set("on", true);
            artifact.RestoreItemState(state);

            Assert.IsTrue(rig.PoseMirrored,
                "every hold clip is right-handed, so a left-arm device plays the mirror or the " +
                "empty arm comes up");
        }

        [Test]
        public void SwitchingItOff_DropsThePose()
        {
            var on = new ItemState();
            on.Set("on", true);
            artifact.RestoreItemState(on);

            artifact.RestoreItemState(new ItemState());

            Assert.IsFalse(rig.Posing,
                "the arm reads the set's state, so an unpowered scanner must put it back down");
        }

        [Test]
        public void TakingItOff_DropsThePose()
        {
            var on = new ItemState();
            on.Set("on", true);
            artifact.RestoreItemState(on);

            artifact.OnUnequipped(holder);

            Assert.IsFalse(rig.Posing,
                "a set taken off while powered must not leave the body posing for a wrist it is " +
                "no longer on");
        }

        [Test]
        public void AHeldItemStillOutranksIt()
        {
            var on = new ItemState();
            on.Set("on", true);
            artifact.RestoreItemState(on);

            rig.SetHeldStyle(ItemGrip.HoldStyle.TwoHanded);

            Assert.AreEqual(ItemGrip.HoldStyle.TwoHanded, rig.PoseStyle,
                "both hands are on the held item; the wrist device does not get to pose the body");
        }
    }
}
