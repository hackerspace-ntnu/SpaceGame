// Why a teleport has to move the Rigidbody and not just the transform.
//
// Physics.autoSyncTransforms is false in this project (the Unity default since 2018), which means a
// write to transform.position does NOT reach the body PhysX is simulating — measured in this
// editor: after `go.transform.position = (10,20,30)`, `rb.position` was still (0,0,0). The body
// keeps the pose it last simulated and puts the transform back on the next step, and an
// interpolated body does it a frame sooner still, because interpolation drives the transform from
// the body's own poses every frame.
//
// That is what made respawning look broken: PlayerRespawn resolved the spawn point inside the ship
// correctly, SaveTeleport wrote the transform, and the player's Rigidbody — non-kinematic, with
// Interpolate on — dragged them straight back to the spot they had died on. UnderTerrainGuard had
// already learned this on this project ("PhysX keeps each Rigidbody's position independently and
// snaps it back to whatever it last simulated") and resyncs its bodies by hand; the teleport used
// by respawn, save loading and interior transit did not.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public class TeleportPlacementTests
    {
        private GameObject subject;

        private static readonly Vector3 Destination = new(120f, 34f, -56f);

        [SetUp]
        public void SetUp() => subject = new GameObject("TeleportSubject");

        [TearDown]
        public void TearDown()
        {
            if (subject != null) Object.DestroyImmediate(subject);
        }

        private static Rigidbody AddBody(GameObject host, RigidbodyInterpolation interpolation)
        {
            Rigidbody body = host.AddComponent<Rigidbody>();
            body.interpolation = interpolation;
            return body;
        }

        [Test]
        public void Move_PlacesTheBodyItself_NotOnlyTheTransform()
        {
            Rigidbody body = AddBody(subject, RigidbodyInterpolation.Interpolate);

            SaveTeleport.Move(subject, Destination, Quaternion.identity);

            Assert.AreEqual(Destination, body.position,
                "The transform was moved and the body was not, so PhysX still holds the pose it " +
                "last simulated — and puts the object back there on the next step. This is the " +
                "respawn that returned the player to the place they died.");
        }

        [Test]
        public void Move_PlacesChildBodiesToo()
        {
            var child = new GameObject("Part");
            child.transform.SetParent(subject.transform);
            child.transform.localPosition = new Vector3(0f, 2f, 0f);
            Rigidbody childBody = AddBody(child, RigidbodyInterpolation.None);

            SaveTeleport.Move(subject, Destination, Quaternion.identity);

            Assert.AreEqual(Destination + new Vector3(0f, 2f, 0f), childBody.position,
                "An articulated body — a walker's legs, a rover's bogies — is simulated per part. " +
                "Resyncing only the root drags the chassis away from parts that stayed behind.");
        }

        [Test]
        public void Move_LeavesInterpolationAsItFoundIt()
        {
            Rigidbody body = AddBody(subject, RigidbodyInterpolation.Interpolate);

            SaveTeleport.Move(subject, Destination, Quaternion.identity);

            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation,
                "Interpolation is suppressed across the write so the interpolator cannot smear the " +
                "body back toward where it came from. It has to be put back, or one teleport " +
                "makes the player jitter for the rest of the session.");
        }

        [Test]
        public void Move_ClearsMomentumTheDestinationCannotUse()
        {
            Rigidbody body = AddBody(subject, RigidbodyInterpolation.None);
            body.linearVelocity = new Vector3(0f, -40f, 0f);

            SaveTeleport.Move(subject, Destination, Quaternion.identity);

            Assert.AreEqual(Vector3.zero, body.linearVelocity,
                "A body teleported mid-fall keeps the fall, and arrives punching through the floor.");
        }

        [Test]
        public void Move_WithoutZeroVelocity_LeavesMomentumAlone()
        {
            Rigidbody body = AddBody(subject, RigidbodyInterpolation.None);
            var velocity = new Vector3(3f, 0f, 4f);
            body.linearVelocity = velocity;

            SaveTeleport.Move(subject, Destination, Quaternion.identity, zeroVelocity: false);

            Assert.AreEqual(velocity, body.linearVelocity,
                "RigidbodySaveable restores the real velocity right after this call. The two run " +
                "in component order and neither may depend on winning that race.");
        }
    }
}
