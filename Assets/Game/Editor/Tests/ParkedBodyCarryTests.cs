// Why the crash-landing crew stood up able to walk on air.
//
// Two systems suspended the same body's gravity and each remembered the old value privately.
//
//   * `UnderTerrainGuard` parks a body that is inside the streamed grid with no loaded chunk under
//     it — gravity off, position pinned — and remembered `useGravity` in its own array.
//   * `CarriedBody` freezes a body a seat or a mount has taken, and captures `useGravity` on the
//     FIRST hold so the LAST release can hand it back.
//
// The arrival puts those two in exactly the wrong order. `ArrivalDirector` spawns the player at the
// top of the descent — 2200 m up and 900 m out, over chunks the streamer has not reached — and
// seats them one frame later. `IsInsideWorldGrid` is an X/Z test that ignores altitude, so the
// guard reads "inside the world, no ground here" and parks a body two kilometres in the sky. The
// seat then captures `useGravity == false` as this player's normal state. The guard drops its park
// a quarter-second later (the body is carried now), so the whole descent looks right — and thirty
// seconds later the crew stand up and `CarriedBody` hands back the state it banked: a dynamic body
// with no gravity. They walk, they steer, they never come down.
//
// The fix is the one `CarriedBody` was written for and the guard was never routed through: ONE
// record of what a body was before anything picked it up. These tests hold that line.
//
// NOT covered here, and deliberately: `UnderTerrainGuard.OnDisable` giving a park back when the
// guard is switched off mid-hold — which `ArrivalDirector.QuietHull` does to every ship's guard for
// the length of a descent. Unity does not raise OnEnable/OnDisable for a plain MonoBehaviour outside
// play mode, so neither `enabled = false` nor `DestroyImmediate` reaches it from an EditMode test
// (both were tried, and both leave the hook unrun rather than failing loudly). Covering it needs a
// PlayMode suite, which this project does not have. Do not re-add the EditMode version.
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.World.Safety;

namespace SpaceGame.EditorTools
{
    public class ParkedBodyCarryTests
    {
        /// <summary>
        /// Far from any Terrain that might be loaded in the editor, and far below the guard's
        /// absolute floor: the one place a park can be provoked without a WorldStreamer.
        /// </summary>
        private static readonly Vector3 OutsideTheWorld = new(100000f, -1000f, 100000f);

        private GameObject root;
        private Rigidbody body;

        /// <summary>Stands in for the seat or the mount — <c>CarriedBody</c> only needs identity.</summary>
        private readonly object carrier = new();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Player") { transform = { position = OutsideTheWorld } };
            body = root.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        [TearDown]
        public void TearDown()
        {
            CarriedBody.Abandon(carrier);
            if (root != null) Object.DestroyImmediate(root);
        }

        /// <summary>
        /// The bug, end to end, in the order the arrival produces it: parked first, carried second,
        /// un-parked while still carried, released last.
        /// </summary>
        [Test]
        public void ABodyCarriedWhileParked_GetsItsOwnGravityBack()
        {
            UnderTerrainGuard guard = Park();

            CarriedBody.Hold(root, carrier);   // the seat takes a body the guard is already holding
            guard.RunCheckNow();               // the guard sees it is carried and lets go
            CarriedBody.Release(root, carrier); // the player stands up

            Assert.IsTrue(body.useGravity,
                "The seat banked the guard's park as this player's normal state and handed it " +
                "back. A dynamic body with no gravity is a player who walks on air.");
            Assert.IsFalse(body.isKinematic);
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);
        }

        /// <summary>
        /// The same collision the other way round, at the record rather than through the guard: the
        /// guard declines to evaluate a body somebody else has taken, so "parked while carried" is
        /// unreachable from outside — but nothing about the record may depend on that staying true.
        /// </summary>
        [Test]
        public void AWeightClaimTakenAfterAFullOne_StillLeavesTheBodyItsOwnState()
        {
            var park = new object();

            CarriedBody.Hold(root, carrier);
            CarriedBody.SuspendGravity(root, park);
            CarriedBody.Release(root, park);
            CarriedBody.Release(root, carrier);

            Assert.IsTrue(body.useGravity);
            Assert.IsFalse(body.isKinematic);
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);
        }

        /// <summary>
        /// A weight-only claim leaves the body under physics. It is not a freeze, and a park that
        /// froze every vehicle it held would be a very different component.
        /// </summary>
        [Test]
        public void SuspendingWeight_LeavesTheBodyDynamic()
        {
            var park = new object();

            CarriedBody.SuspendGravity(root, park);

            Assert.IsFalse(body.useGravity);
            Assert.IsFalse(body.isKinematic, "A parked body still collides and still has momentum.");
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);
        }

        /// <summary>
        /// The park on its own still works, and still ends: a body nobody else takes gets its
        /// gravity back the moment the guard stops holding it.
        /// </summary>
        [Test]
        public void AParkThatNobodyElseJoins_EndsOnItsOwn()
        {
            UnderTerrainGuard guard = Park();
            Assert.IsFalse(body.useGravity, "A parked body falls while it waits.");

            root.transform.position = Vector3.zero;   // back above the floor: no longer a park
            guard.RunCheckNow();

            Assert.IsTrue(body.useGravity);
        }

        /// <summary>
        /// Parks the body and returns the guard holding it. The park is the loud kind — below the
        /// absolute floor with no terrain — because that is the only verdict reachable without a
        /// WorldStreamer to answer <c>IsInsideStreamedWorld</c>.
        /// </summary>
        private UnderTerrainGuard Park()
        {
            var guard = root.AddComponent<UnderTerrainGuard>();

            LogAssert.Expect(LogType.Error, new Regex("fell below"));
            guard.RunCheckNow();

            Assert.IsFalse(body.useGravity, "Test fixture is wrong: the guard did not park.");
            return guard;
        }
    }
}
