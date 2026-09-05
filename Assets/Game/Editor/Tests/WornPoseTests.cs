// A worn item must not pose the arm.
//
// UsableItem.OnEquipped hands every gripped item a HoldAnimator whether or not one was authored
// (HoldPoseTests pins that). A gauntlet goes through the very same OnEquipped — it is the same
// artifact, seated on the forearm instead of in the palm — and the hand it sits beside may be
// holding a bazooka at the same time. So the one thing that changes for a worn instance is that
// the pose is skipped, and this pins that the switch is Worn, set before OnEquipped, and nothing
// else.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class WornPoseTests
    {
        private class PlainItem : UsableItem
        {
            protected override void Use() { }
        }

        private GameObject item;
        private GameObject holder;

        [SetUp]
        public void SetUp()
        {
            holder = new GameObject("Holder");
            item = new GameObject("Item");
        }

        [TearDown]
        public void TearDown()
        {
            if (item != null) Object.DestroyImmediate(item);
            if (holder != null) Object.DestroyImmediate(holder);
        }

        [Test]
        public void AWornItemGetsNoHoldPose()
        {
            var usable = item.AddComponent<PlainItem>();
            usable.Worn = true;

            usable.OnEquipped(holder);

            Assert.IsNull(item.GetComponent<HoldAnimator>(),
                "a gauntlet on the forearm must leave the hand free; it must not add a hold pose");
        }

        [Test]
        public void TheSameItemHeldStillGetsOne()
        {
            var usable = item.AddComponent<PlainItem>();
            usable.Worn = false;

            usable.OnEquipped(holder);

            Assert.IsNotNull(item.GetComponent<HoldAnimator>(),
                "held is the default, and held items pose the hand as before");
        }

        [Test]
        public void WornIsNotTheDefault()
        {
            Assert.IsFalse(item.AddComponent<PlainItem>().Worn,
                "every existing equip path never sets Worn, and must keep posing");
        }
    }
}
