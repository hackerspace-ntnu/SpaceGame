// The arithmetic that puts an agent's body on the ground, with no scene, no colliders and no
// physics -- the same separation SpiderWalkerGroundingTests relies on at the other end, where the
// whole assembled machine is checked against real geometry.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.Tests
{
    public class AgentGroundingTests
    {
        private static AgentGroundingSettings Settings() => new AgentGroundingSettings
        {
            SoleOffset = 0f,
            MaxCorrection = 1f,
            HeightFollowSpeed = 12f,
            SlopeFollow = 1f,
            MaxTiltDegrees = 30f,
            TiltFollowSpeed = 8f,
        };

        /// The measured case: the NavMesh put the body 0.26 m above the sand.
        [Test]
        public void TheBodyIsPutOnTheGroundTheProbeFound()
        {
            var g = new AgentGrounding(Quaternion.identity);

            g.Step(grounded: true, navSurfaceY: 100.26f, groundY: 100f, localGroundNormal: Vector3.up,
                   currentBodyRotation: Quaternion.identity, Settings(), 1f / 60f);

            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);
        }

        /// <summary>
        /// The first frame snaps. Easing in from zero would drop every agent a quarter of a metre
        /// in front of the player on the frame it spawns, and do it again after every stream-in,
        /// respawn and save restore.
        /// </summary>
        [Test]
        public void TheFirstFrameSnapsRatherThanEasingIn()
        {
            var g = new AgentGrounding(Quaternion.identity);

            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, Settings(), 0.0001f);

            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);
        }

        /// <summary>
        /// A probe that finds something absurd -- the roof of a cave, a collider streaming in
        /// underneath -- must not be able to teleport a body. It is clamped, not trusted.
        /// </summary>
        [Test]
        public void TheCorrectionIsCappedBothWays()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.MaxCorrection = 0.5f;

            g.Step(true, 100f, 90f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(-0.5f, g.HeightOffset, 0.0001f, "a floor 10 m down must not drop the body 10 m");

            var up = new AgentGrounding(Quaternion.identity);
            up.Step(true, 100f, 110f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(0.5f, up.HeightOffset, 0.0001f, "a ceiling read as floor must not launch the body");
        }

        /// <summary>
        /// Off the ground -- mid-leap, over a ledge, a hole in the collision -- holding the last
        /// correction would hang the body at the height of ground it is no longer above. The
        /// honest answer with no probe is "wherever navigation put me".
        /// </summary>
        [Test]
        public void WithNoGroundTheCorrectionDecaysBackToZero()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);

            for (int i = 0; i < 120; i++)
                g.Step(false, 100.26f, 0f, Vector3.up, g.BodyRotation, s, 1f / 60f);

            Assert.AreEqual(0f, g.HeightOffset, 0.001f);
        }

        [Test]
        public void TheTiltFollowsTheSurfaceNormal()
        {
            var g = new AgentGrounding(Quaternion.identity);
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            g.Step(true, 100f, 100f, normal, Quaternion.identity, Settings(), 1f);

            float angle = Quaternion.Angle(Quaternion.identity, g.BodyRotation);
            Assert.AreEqual(20f, angle, 0.5f);
        }

        [Test]
        public void TheTiltIsCappedHoweverSteepTheGroundIs()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.MaxTiltDegrees = 15f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 50f) * Vector3.up;

            g.Step(true, 100f, 100f, normal, Quaternion.identity, s, 1f);

            Assert.AreEqual(15f, Quaternion.Angle(Quaternion.identity, g.BodyRotation), 0.5f);
        }

        /// <summary>
        /// The compounding trap. On the Nomad, PatrolRobot and Vrescal nothing animates the node
        /// the tilt is written to, so the value read back next frame is the tilt itself. Multiply
        /// the tilt in again and the body spins.
        /// </summary>
        [Test]
        public void ANodeNothingElseDrivesDoesNotAccumulateTilt()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.TiltFollowSpeed = 1000f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            // Exactly what the component does: write the result to the transform, and read that
            // same transform back on the next frame because nothing else touched it.
            Quaternion onTheTransform = Quaternion.identity;
            for (int i = 0; i < 100; i++)
            {
                g.Step(true, 100f, 100f, normal, onTheTransform, s, 1f / 60f);
                onTheTransform = g.BodyRotation;
            }

            Assert.AreEqual(20f, Quaternion.Angle(Quaternion.identity, onTheTransform), 0.5f);
        }

        /// <summary>
        /// The opposite case, and it wants the opposite treatment. The Golem's clips carry a
        /// rotation curve for Bone_Root and DuneRat's for Arm_DuneRat, so the Animator rewrites
        /// the node before every LateUpdate. Tilting from the rest pose there would erase the
        /// animation; the tilt has to ride on top of it.
        /// </summary>
        [Test]
        public void AnAnimatedNodeKeepsItsAnimationUnderTheTilt()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.TiltFollowSpeed = 1000f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            Quaternion animated = Quaternion.identity;
            for (int i = 0; i < 100; i++)
            {
                // A clip swinging the root bone 10 degrees back and forth about Y.
                animated = Quaternion.Euler(0f, Mathf.Sin(i * 0.3f) * 10f, 0f);
                g.Step(true, 100f, 100f, normal, animated, s, 1f / 60f);
            }

            Assert.AreEqual(0f, Quaternion.Angle(g.BodyRotation, g.LastTilt * animated), 0.5f,
                            "the tilt must be composed onto the animated pose, not replace it");
        }

        /// <summary>
        /// Reset is what OnEnable calls. A creature comes back from a respawn, a chunk stream and
        /// a save restore, and each time it must snap to the ground it is standing on now rather
        /// than easing over from wherever it last stood.
        /// </summary>
        [Test]
        public void ResetMakesTheNextFrameSnapAgain()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, s, 1f);

            g.Reset();
            Assert.AreEqual(0f, g.HeightOffset, 0.0001f);

            g.Step(true, 50.4f, 50f, Vector3.up, Quaternion.identity, s, 0.0001f);
            Assert.AreEqual(-0.4f, g.HeightOffset, 0.0001f);
        }
    }
}
