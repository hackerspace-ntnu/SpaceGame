// The one rule Phase 1 of the artifact-multiplayer fixes exists to enforce: a body is moved by the
// machine that OWNS it, never by "the server" as a blanket answer.
//
// Offline — which is what an EditMode test is — every machine owns everything, so what these tests
// can pin is that the code asks the ownership question at all, and that the offline answer stays
// permissive so single-player never stops moving. The half that actually broke in a session, where
// a client owns a body the server does not, is proved by the two-process run; there is no
// substitute for it and these tests do not pretend to be one.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class BodyOwnershipTests
    {
        private GameObject scratch;

        [SetUp]
        public void SetUp() => scratch = new GameObject("ownership-scratch");

        [TearDown]
        public void TearDown()
        {
            if (scratch != null) Object.DestroyImmediate(scratch);
        }

        [Test]
        public void OfflineOwnsEverything()
        {
            Assert.IsTrue(Network.Owns(scratch.transform),
                          "Offline, every machine owns everything — otherwise nothing moves in single-player.");
        }

        [Test]
        public void LeashObjectEndIsResolvedByItsOwner()
        {
            var target = new GameObject("crate");
            target.AddComponent<Rigidbody>().isKinematic = true;

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, target, Vector3.zero);

            Assert.IsTrue(rope.A.ResolvedHere,
                          "An unnetworked object is owned by every machine, so each resolves its own copy.");

            rope.Dispose();
            Object.DestroyImmediate(target);
        }

        [Test]
        public void ADeadEndIsNobodysToResolve()
        {
            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });

            // An untied end has no anchor and no body, so Network.Owns is asked about null. The
            // answer must not be an exception: a rope is built untied and has its ends attached one
            // at a time, so a physics step legitimately lands while one end is still empty.
            Assert.IsFalse(rope.A.CanMove, "An untied end cannot move anything, whoever owns it.");

            rope.Dispose();
        }
    }
}
