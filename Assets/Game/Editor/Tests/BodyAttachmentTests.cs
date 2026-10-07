// What a body is made of, versus what it is carrying.
//
// Worn and held gear is parented onto the wearer's skeleton, so from the hierarchy alone it is
// indistinguishable from a bone — and RagdollRig, which builds a skeleton by taking every node that
// draws nothing but has geometry beneath it, took nine of its fourteen candidates off the gear of a
// player wearing a jetpack with the pack shouldered. This mark is the difference, so these are the
// tests that keep it honest.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BodyAttachmentTests
    {
        private GameObject body;
        private GameObject bone;
        private GameObject item;
        private GameObject itemPart;

        [SetUp]
        public void SetUp()
        {
            body = new GameObject("Body");

            bone = new GameObject("Spine");
            bone.transform.SetParent(body.transform, false);

            item = new GameObject("Jetpack");
            item.transform.SetParent(bone.transform, false);

            itemPart = new GameObject("Mesh_Jetpack_Shell");
            itemPart.transform.SetParent(item.transform, false);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(body);

        [Test]
        public void AnUnmarkedHierarchyIsAllBody()
        {
            Assert.IsFalse(BodyAttachment.Covers(itemPart.transform, body.transform));
            Assert.IsFalse(BodyAttachment.Covers(bone.transform, body.transform));
        }

        [Test]
        public void TheMarkCoversEverythingBeneathIt()
        {
            BodyAttachment.Mark(item);

            Assert.IsTrue(BodyAttachment.Covers(item.transform, body.transform));
            Assert.IsTrue(BodyAttachment.Covers(itemPart.transform, body.transform),
                          "The mark sits on the item's root and the meshes are below it, so the " +
                          "test has to walk up — RagdollRig asks about renderers, not about roots.");
        }

        [Test]
        public void TheBoneTheItemHangsOffIsStillBody()
        {
            BodyAttachment.Mark(item);

            Assert.IsFalse(BodyAttachment.Covers(bone.transform, body.transform),
                           "Marking gear must not disown the bone it is attached to.");
        }

        [Test]
        public void TheWalkStopsAtTheRoot()
        {
            // A rider is parented into their mount while mounted, and the mount may well have marked
            // them. Asked about the rider's OWN parts, with the rider as the root, the answer is no.
            var mount = new GameObject("Mount");
            try
            {
                body.transform.SetParent(mount.transform, false);
                BodyAttachment.Mark(body);

                Assert.IsFalse(BodyAttachment.Covers(bone.transform, body.transform),
                               "A body that is itself an attachment reported its own bones as gear.");
            }
            finally
            {
                body.transform.SetParent(null, false);
                Object.DestroyImmediate(mount);
            }
        }

        [Test]
        public void MarkingTwiceLeavesOneMark()
        {
            BodyAttachment.Mark(item);
            BodyAttachment.Mark(item);

            Assert.AreEqual(1, item.GetComponents<BodyAttachment>().Length);
        }

        // Every equip path goes through Sanitize, so that is where the promise is kept.
        [Test]
        public void SanitizeMarksAnEquippedCopy()
        {
            var held = new GameObject("HeldItem");
            try
            {
                EquipItemSocket.Sanitize(held);
                Assert.IsNotNull(held.GetComponent<BodyAttachment>());
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }
    }
}
