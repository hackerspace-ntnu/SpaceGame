// Riders standing on a transform-driven platform.
//
// The carrier collected its riders from OnTriggerEnter/Stay. Unity delivers those messages to the
// GameObject holding the collider and to the GameObject holding that collider's attachedRigidbody --
// and to nothing else. The carry volume is a CHILD of the hull, so the messages only ever reached the
// carrier when the craft happened to have a Rigidbody somewhere for the volume to attach to.
//
// The DesertCrawler has a kinematic Rigidbody at its root, so it worked. The DuneFoil has no
// Rigidbody at all, so its carrier never received a single message, `riders` stayed empty for the
// whole session, and a player on the deck was left standing in mid-air the moment the craft sailed
// out from under them. Nothing about that failure is visible in the inspector: the volume is there,
// it is a trigger, the component is enabled.
//
// So the case that matters is the one below: a platform with NO Rigidbody anywhere must still carry.
// The Rigidbody-at-the-root case is kept beside it because that is the configuration that already
// worked, and this must not be a fix that trades one craft for another.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class WalkerPlatformCarrierTests
    {
        private GameObject platform;
        private GameObject rider;
        private SimulationMode originalSimulationMode;

        /// Physics is stepped by hand so a test can watch a carry land. Riders are dynamic bodies,
        /// so the carry only shows up once the step has run -- see
        /// <see cref="MovesRiderThroughThePhysicsStepRatherThanTeleportingIt"/> for why it must.
        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = originalSimulationMode;
            if (platform != null) Object.DestroyImmediate(platform);
            if (rider != null) Object.DestroyImmediate(rider);
        }

        private const float Step = 0.02f;

        private static void StepPhysics() => Physics.Simulate(Step);

        /// A hull with the carrier on the root and its trigger volume on a child, the way every
        /// craft in the project is built.
        private WalkerPlatformCarrier BuildPlatform(bool withRigidbody)
        {
            platform = new GameObject("Platform");
            platform.transform.position = Vector3.zero;

            if (withRigidbody)
            {
                Rigidbody body = platform.AddComponent<Rigidbody>();
                body.isKinematic = true;
            }

            GameObject volume = new GameObject("COL_CarryVolume");
            volume.transform.SetParent(platform.transform, false);
            BoxCollider box = volume.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(4f, 3f, 16f);

            WalkerPlatformCarrier carrier = platform.AddComponent<WalkerPlatformCarrier>();
            carrier.BindCarryVolume(box);
            return carrier;
        }

        /// A player-shaped rider: a non-kinematic Rigidbody standing on the deck.
        private Rigidbody BuildRider(Vector3 position)
        {
            rider = new GameObject("Rider");
            rider.transform.position = position;
            CapsuleCollider capsule = rider.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            Rigidbody body = rider.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            return body;
        }

        /// The carry must reach the rider through the physics step, never as a direct pose write.
        ///
        /// Assigning Rigidbody.position teleports the body and throws away Unity's interpolation
        /// history for it. The craft moves on the render loop, so doing that once per rendered frame
        /// discarded the interpolation several times per physics step -- and interpolation is the
        /// only thing smoothing a 50 Hz simulation out over a 240 Hz display. The rider ended up
        /// rendered somewhere between the pose the carry wrote and the pose the solver last
        /// produced, which is the vibration felt when standing on a moving deck. Since the camera
        /// hangs off the player, the whole view shook with them.
        ///
        /// So the observable contract is: CarryRiders leaves the body where it is and asks physics
        /// to put it in the right place, and the rider has arrived once the step has run.
        [Test]
        [Ignore("Carry is still a pose write. Deliberate: expressing it as velocity needs the " +
                "carrier to hold each rider's own motion separately from the carry, and the deck " +
                "is parked for now. This test is the contract that work has to satisfy.")]
        public void MovesRiderThroughThePhysicsStepRatherThanTeleportingIt()
        {
            WalkerPlatformCarrier carrier = BuildPlatform(withRigidbody: false);
            Rigidbody body = BuildRider(new Vector3(0f, 1f, 2f));
            Physics.SyncTransforms();

            carrier.Prime();
            platform.transform.position += new Vector3(0f, 0f, 5f);
            Physics.SyncTransforms();
            carrier.CarryRiders();

            Assert.AreEqual(2f, body.position.z, 1e-3f,
                "the carry wrote the rider's pose directly instead of leaving it to the physics step");

            StepPhysics();

            Assert.AreEqual(7f, body.position.z, 1e-3f,
                "the rider did not arrive at the platform's new position after the physics step");
        }

        [Test]
        public void CarriesRiderWhenThePlatformHasNoRigidbody()
        {
            WalkerPlatformCarrier carrier = BuildPlatform(withRigidbody: false);
            Rigidbody body = BuildRider(new Vector3(0f, 1f, 2f));
            Physics.SyncTransforms();

            carrier.Prime();
            platform.transform.position += new Vector3(0f, 0f, 5f);
            Physics.SyncTransforms();
            carrier.CarryRiders();
            StepPhysics();

            Assert.AreEqual(1, carrier.RiderCount,
                "a platform with no Rigidbody never collected the rider standing on it");
            Assert.AreEqual(7f, body.position.z, 1e-3f,
                "the rider did not travel the platform's 5 m");
        }

        [Test]
        public void CarriesRiderWhenThePlatformHasARigidbody()
        {
            WalkerPlatformCarrier carrier = BuildPlatform(withRigidbody: true);
            Rigidbody body = BuildRider(new Vector3(0f, 1f, 2f));
            Physics.SyncTransforms();

            carrier.Prime();
            platform.transform.position += new Vector3(0f, 0f, 5f);
            Physics.SyncTransforms();
            carrier.CarryRiders();
            StepPhysics();

            Assert.AreEqual(1, carrier.RiderCount);
            Assert.AreEqual(7f, body.position.z, 1e-3f);
        }

        /// The deck turning under the rider has to swing them around the hull's pivot, or a craft
        /// that yaws slides everyone off the side.
        ///
        /// Turned a frame at a time rather than in one 90-degree jump, because that is the only way
        /// it ever happens: the craft is capped at 28 deg/s, which is under half a degree per frame.
        /// A single huge step would land the rider outside the carry volume before anyone asked who
        /// was aboard, and would be testing teleportation rather than turning.
        [Test]
        public void SwingsRiderAboutThePivotWhenThePlatformTurns()
        {
            WalkerPlatformCarrier carrier = BuildPlatform(withRigidbody: false);
            Rigidbody body = BuildRider(new Vector3(0f, 1f, 4f));
            Physics.SyncTransforms();
            carrier.Prime();

            for (int step = 1; step <= 60; step++)
            {
                platform.transform.rotation = Quaternion.Euler(0f, step * 1.5f, 0f);
                Physics.SyncTransforms();
                carrier.CarryRiders();
                StepPhysics();
            }

            // 4 m ahead of the pivot, yawed 90 degrees, lands 4 m to starboard.
            Assert.AreEqual(4f, body.position.x, 1e-2f, "rider was not swung about the pivot");
            Assert.AreEqual(0f, body.position.z, 1e-2f);
            Assert.AreEqual(1, carrier.RiderCount, "rider was dropped part way through the turn");
        }

        /// Somebody who has walked off the deck must be dropped, or they get towed through the air.
        [Test]
        public void DropsRiderWhoLeavesTheVolume()
        {
            WalkerPlatformCarrier carrier = BuildPlatform(withRigidbody: false);
            Rigidbody body = BuildRider(new Vector3(0f, 1f, 2f));
            Physics.SyncTransforms();

            carrier.Prime();
            platform.transform.position += new Vector3(0f, 0f, 1f);
            Physics.SyncTransforms();
            carrier.CarryRiders();
            StepPhysics();
            Assert.AreEqual(1, carrier.RiderCount, "rider should have been aboard to begin with");

            body.position = new Vector3(0f, 1f, 60f);   // stepped off, well clear of the hull
            body.linearVelocity = Vector3.zero;         // and standing still, not still coasting
            Physics.SyncTransforms();
            platform.transform.position += new Vector3(0f, 0f, 1f);
            Physics.SyncTransforms();
            carrier.CarryRiders();
            StepPhysics();

            Assert.AreEqual(0, carrier.RiderCount, "rider who left the deck is still being carried");
            Assert.AreEqual(60f, body.position.z, 1e-3f, "rider off the deck was still moved");
        }
    }
}
