// Why a loaded world used to hand you a player who could look around but never move.
//
// The bug these pin: every save on disk carried
// `players[0].state.entries.rigidbody.isKinematic = true`, and RigidbodySaveable put it straight
// back on load. A kinematic Rigidbody ignores every `linearVelocity` write, so PlayerMovement
// commanded 6 m/s into a body that could not answer — while `MoveInput` read (0,1), the animator's
// SpeedY read 6, and every control flag said the player was in charge. Only rotation still worked,
// because MoveRotation works on a kinematic body, which is what made it read as "movement is
// broken" rather than "the save is wrong".
//
// The flag got into the file during teardown. `SaveManager.OnApplicationQuit` writes a save labelled
// "Autosave", and by then netcode teardown has already made the body kinematic — NetworkRigidbody
// sits at component index 3 on the player prefab, PlayerSaveSync near the end. Every file labelled
// "Autosave" recorded true; every file written by a live SaveOnExit recorded false.
//
// So the fix is ownership, not ordering: `isKinematic` belongs to whichever live component wants it
// (NetworkRigidbody, MountModule, NetAuthority, WorldItem, GroundAnchorOnLand), and a save has no
// business overriding them. These tests hold that line from both ends — the restore no longer applies the
// flag, and a player who somehow still ends up kinematic frees themselves.
//
// They live in Editor/ rather than the EditMode asmdef because both RigidbodySaveable and
// PlayerMovement are Assembly-CSharp types, which an asmdef cannot reference.
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Characters;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public class KinematicBodyRestoreTests
    {
        private GameObject root;
        private GameObject carrier;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (carrier != null) Object.DestroyImmediate(carrier);
        }

        // ------------------------------------------------------------------ the restore

        /// <summary>
        /// The bug itself, at its source. A record written during teardown says the body was
        /// kinematic; applying that to a body the runtime wants dynamic is what froze the player.
        /// </summary>
        [Test]
        public void AKinematicRecord_DoesNotFreezeADynamicBody()
        {
            Rigidbody body = BuildBody(kinematic: false);
            var saver = root.AddComponent<RigidbodySaveable>();

            saver.RestoreState(Payload(velocity: Vector3.zero, isKinematic: true));

            Assert.IsFalse(body.isKinematic,
                "A save made the body kinematic, so nothing it is told to do will move it. " +
                "isKinematic belongs to the live components that set it, not to the save.");
        }

        /// <summary>
        /// The half worth keeping: a body left rolling comes back rolling. Removing the flag from
        /// the restore must not quietly remove momentum with it.
        /// </summary>
        [Test]
        public void MomentumStillReachesADynamicBody()
        {
            Rigidbody body = BuildBody(kinematic: false);
            var saver = root.AddComponent<RigidbodySaveable>();

            saver.RestoreState(Payload(velocity: new Vector3(1f, 2f, 3f), isKinematic: false));

            Assert.AreEqual(new Vector3(1f, 2f, 3f), body.linearVelocity);
        }

        /// <summary>
        /// The mirror case, and the reason the guard reads the LIVE body rather than the record: a
        /// rider sitting on a mount is kinematic on purpose, and Unity throws when a kinematic body
        /// is assigned a velocity. Whoever holds the body still holds it after a load.
        /// </summary>
        [Test]
        public void ADynamicRecord_DoesNotWakeABodySomethingElseIsHolding()
        {
            Rigidbody body = BuildBody(kinematic: true);
            var saver = root.AddComponent<RigidbodySaveable>();

            saver.RestoreState(Payload(velocity: new Vector3(9f, 9f, 9f), isKinematic: false));

            Assert.IsTrue(body.isKinematic,
                "The save woke a body that MountModule or NetAuthority is deliberately holding.");
        }

        /// <summary>
        /// The other end of the same rule: a body somebody else is posing has no motion of its own
        /// to report, so it reports none. This is what keeps the flag out of new files — the capture
        /// most likely to find a body held is the one taken while netcode tears the session down,
        /// which is where the bad value came from in the first place.
        /// </summary>
        [Test]
        public void AHeldBody_ReportsNoMotionRatherThanZero()
        {
            BuildBody(kinematic: true);
            var saver = root.AddComponent<RigidbodySaveable>();

            Assert.IsNull(saver.CaptureState(),
                "A kinematic body reported motion it does not have. StateBag.Set drops a null " +
                "payload, which is how the entry stays out of the file entirely.");
        }

        /// <summary>A body that is genuinely moving still reports what it is doing.</summary>
        [Test]
        public void AMovingBody_ReportsItsMotion()
        {
            Rigidbody body = BuildBody(kinematic: false);
            body.linearVelocity = new Vector3(4f, 0f, -1f);
            var saver = root.AddComponent<RigidbodySaveable>();

            var captured = (RigidbodySaveable.State)saver.CaptureState();

            Assert.AreEqual(new Vector3(4f, 0f, -1f), captured.velocity);
        }

        /// <summary>
        /// The shipped files all carry the field, and they have to keep loading. It is read past,
        /// not choked on — see SaveSerializer's MissingMemberHandling.
        /// </summary>
        [Test]
        public void AnOldPayloadCarryingTheFlag_StillRestoresItsMotion()
        {
            Rigidbody body = BuildBody(kinematic: false);
            var saver = root.AddComponent<RigidbodySaveable>();

            saver.RestoreState(JObject.Parse(
                @"{""velocity"":{""x"":1.0,""y"":0.0,""z"":2.0},
                    ""angularVelocity"":{""x"":0.0,""y"":0.0,""z"":0.0},
                    ""isKinematic"":false}"));

            Assert.AreEqual(new Vector3(1f, 0f, 2f), body.linearVelocity);
        }

        // ------------------------------------------------------------------ the player's own guard

        /// <summary>
        /// Defence in depth. Even with the restore fixed, a player left kinematic by anything else
        /// — a netcode ownership hiccup, a mount that failed to hand the body back, a future saver —
        /// is a player who cannot play, so the component that needs a dynamic body insists on one.
        /// </summary>
        [Test]
        public void AStrandedKinematicPlayer_FreesTheirOwnBody()
        {
            Rigidbody body = BuildBody(kinematic: true);
            var movement = root.AddComponent<PlayerMovement>();

            LogAssert.Expect(LogType.Warning, new Regex("kinematic"));

            movement.EnsureMovableBody();

            Assert.IsFalse(body.isKinematic,
                "A player who is not being carried must have a body physics can move.");
        }

        /// <summary>
        /// The one legitimate reason a player's body is kinematic: they are being carried. A rider
        /// is parented into their mount's hierarchy, which is the same test UnderTerrainGuard uses
        /// to tell "carried" from "standing on their own".
        /// </summary>
        [Test]
        public void ARiderBeingCarried_KeepsTheirKinematicBody()
        {
            Rigidbody body = BuildBody(kinematic: true);
            var movement = root.AddComponent<PlayerMovement>();

            carrier = new GameObject("Mount");
            root.transform.SetParent(carrier.transform);

            movement.EnsureMovableBody();

            Assert.IsTrue(body.isKinematic,
                "Freeing a mounted rider's body drops them through their own seat.");
        }

        /// <summary>A body that is already fine is left alone, and says nothing about it.</summary>
        [Test]
        public void ADynamicPlayer_IsLeftAlone()
        {
            Rigidbody body = BuildBody(kinematic: false);
            var movement = root.AddComponent<PlayerMovement>();

            movement.EnsureMovableBody();

            Assert.IsFalse(body.isKinematic);
        }

        // ------------------------------------------------------------------ fixtures

        private Rigidbody BuildBody(bool kinematic)
        {
            root = new GameObject("Body");
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = kinematic;
            return body;
        }

        private static JObject Payload(Vector3 velocity, bool isKinematic) => JObject.Parse(
            $@"{{""velocity"":{{""x"":{velocity.x},""y"":{velocity.y},""z"":{velocity.z}}},
                 ""angularVelocity"":{{""x"":0.0,""y"":0.0,""z"":0.0}},
                 ""isKinematic"":{(isKinematic ? "true" : "false")}}}");
    }
}
