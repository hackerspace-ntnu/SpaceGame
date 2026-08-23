// Why a held item gets its hold pose without anyone remembering to add a component.
//
// The hold pose is driven by a `Hold` bool on the player's Animator, and the only thing that
// ever set it was a HoldAnimator component placed by hand on each item prefab. That made the
// pose opt-in, and opt-in silently: an artifact without the component does not fail, warn, or
// look broken in the inspector — it just stands there in the idle tree holding a gun.
//
// Four of eleven equippable artifacts had one. The other seven — AntiGravityPotion, LaserStaff,
// Leash, LightningSpell, RocketArtifact, PortalGun, WingPack — did not, which is the whole of
// the bug. Nothing else in the chain was broken: the parameter exists, the transitions exist
// (idle tree -> Gun_Aim01 and Move Tree -> Gun_Aim01), and every subclass that overrides
// OnEquipped calls base.
//
// So the fix is at the seam every equipped item already passes through, and these tests pin it
// there rather than pinning the seven prefabs, which would leave artifact number twelve broken
// in exactly the same way.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class HoldPoseTests
    {
        /// <summary>A minimal UsableItem. Only Use() is abstract.</summary>
        private class PlainItem : UsableItem
        {
            protected override void Use() { }
        }

        /// <summary>Something worn rather than gripped, which must not pose the body.</summary>
        private class WornItem : UsableItem
        {
            protected override void Use() { }
            protected override bool UsesHoldPose => false;
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
        public void EquippingAnItemWithNoHoldAnimator_AddsOne()
        {
            var usable = item.AddComponent<PlainItem>();
            Assert.IsNull(item.GetComponent<HoldAnimator>(), "precondition: none authored");

            usable.OnEquipped(holder);

            Assert.IsNotNull(item.GetComponent<HoldAnimator>(),
                "an item with no authored HoldAnimator should still get a hold pose");
        }

        [Test]
        public void AnAuthoredHoldAnimator_IsKeptRatherThanReplaced()
        {
            var usable = item.AddComponent<PlainItem>();
            var authored = item.AddComponent<HoldAnimator>();

            usable.OnEquipped(holder);

            var found = item.GetComponents<HoldAnimator>();
            Assert.AreEqual(1, found.Length, "must not stack a second one on top");
            Assert.AreSame(authored, found[0],
                "the authored component carries per-prefab tuning and must survive");
        }

        [Test]
        public void AnItemThatOptsOut_GetsNoHoldAnimator()
        {
            var usable = item.AddComponent<WornItem>();

            usable.OnEquipped(holder);

            Assert.IsNull(item.GetComponent<HoldAnimator>(),
                "a worn item must not pose the body as though it were gripped");
        }

        [Test]
        public void TheAddedAnimator_RequiresStationary()
        {
            // Not a style preference. The controller has ONE layer with no avatar mask, so the
            // hold state replaces the whole body — legs included. Posing while the player walks
            // freezes the walk cycle and the character glides. Until there is an upper-body mask
            // layer, an auto-added pose has to yield to movement.
            var usable = item.AddComponent<PlainItem>();
            usable.OnEquipped(holder);

            var hold = item.GetComponent<HoldAnimator>();
            Assert.IsTrue(hold.RequiresStationary,
                "auto-added hold must yield to movement while the rig has a single unmasked layer");
        }

        [Test]
        public void UnequippingDoesNotThrow_WhenNothingWasAuthored()
        {
            var usable = item.AddComponent<PlainItem>();
            usable.OnEquipped(holder);

            Assert.DoesNotThrow(() => usable.OnUnequipped(holder));
        }
    }
}
