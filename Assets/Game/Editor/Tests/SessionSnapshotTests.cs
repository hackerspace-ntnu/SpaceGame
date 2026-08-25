// What a joining client is owed.
//
// The snapshot exists because a rope and a portal aperture are not spawned NetworkObjects — every
// machine builds its own copy from a message it had to be present for — so a client that joins
// afterwards has neither, and no way to ever learn.
//
// WHAT CANNOT BE TESTED HERE, and why it is not for want of trying. BuildPayload reads Leash.All,
// which is filled by Leash.OnEnable — and Unity does not raise OnEnable for a runtime AddComponent
// outside play mode, so a rope built by an EditMode test never joins the registry and the payload
// is always empty. That was measured, not assumed. Writing a test that asserts a rope travels would
// therefore pin the empty case while claiming to pin the populated one, which is worse than no test
// at all. The populated path is proved by the two-process run instead, which compares
// CLIENT_LEASHES_SEEN against HOST_LEASHES.
//
// What IS worth pinning here is the empty case, because it is the one that runs on every ordinary
// join and it is the one with a real consequence if it regresses: sending a payload for a session
// with nothing in it makes every joiner do work to learn nothing.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class SessionSnapshotTests
    {
        [Test]
        public void AnEmptySnapshotIsNotSent()
        {
            // Sending an empty payload would cost a round trip to say nothing, and would make every
            // joiner run an apply that does nothing.
            Assert.IsNull(SessionSnapshot.BuildPayload());
        }

        [Test]
        public void BuildingASnapshotNeverThrowsWithNoSessionAtAll()
        {
            // It is called from a connection callback, and a callback that throws takes the rest of
            // the join with it. No NetworkManager, no players, no ropes: the answer is "nothing to
            // send", never an exception.
            Assert.DoesNotThrow(() => SessionSnapshot.BuildPayload());
        }
    }
}
