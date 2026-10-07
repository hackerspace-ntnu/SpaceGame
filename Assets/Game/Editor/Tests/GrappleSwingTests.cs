// When the hook is allowed to touch the player's body, and what it does to them once it is.
//
// Every test here pins a specific way the grapple used to feel wrong:
//
//   • Control was taken at the PRESS, not at the hit — the hook called DisableGroundSnap(999) in
//     Present, which makes PlayerMovement.FixedUpdate return before it does anything. So the player
//     went limp and started falling the instant they pulled the trigger, while the rope was still
//     in the air and had not caught anything. That is the "I get dragged before it hits" feeling.
//   • The rope length was measured at the press too, from wherever the player happened to be
//     standing, rather than from where they were when it caught.
//   • The rope shortened whenever the player drifted inside it, so a swing quietly ratcheted its
//     way up the anchor for free instead of behaving like a pendulum.
//   • Nothing gave the body back if the item went away mid-swing. That was survivable when the flag
//     was a 999-second timer; it is not survivable now that the tether never expires on its own.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class GrappleSwingTests
    {
        private const int Attach = 1;

        private GameObject player;
        private GameObject hook;
        private GameObject target;
        private GrapplingHookArtifact artifact;
        private PlayerMovement movement;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("player", typeof(Rigidbody), typeof(AimProvider), typeof(PlayerMovement));
            movement = player.GetComponent<PlayerMovement>();

            var cam = new GameObject("cam", typeof(Camera));
            cam.transform.SetParent(player.transform, false);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player.GetComponent<AimProvider>(), cam.GetComponent<Camera>());

            artifact = NewArtifact();

            // Near face at z = 9.5, straight down the camera's forward.
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = new Vector3(0f, 0f, 10f);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// A hook straight out of the prefab, which is what an equip actually hands the player —
        /// <see cref="EquipItemSocket"/> instantiates the item on every equip and destroys it on
        /// every unequip, so nothing survives a hotbar swap except the slot's ItemState bag.
        /// </summary>
        private GrapplingHookArtifact NewArtifact()
        {
            hook = new GameObject("grapple", typeof(LineRenderer));
            var made = hook.AddComponent<GrapplingHookArtifact>();

            // Silence the bite. Sfx routes through FMOD's RuntimeManager, which logs an ERROR the
            // moment it is touched outside play mode, and the test framework fails a test on any
            // unexpected error however unrelated. Every test here lands on the far side of a bite,
            // so the whole file would fail on the audio layer rather than on anything it asserts.
            // Muted at the source rather than with LogAssert, which did not hold: SfxId.None is the
            // one input Sfx.Play returns on before it reaches FMOD at all.
            typeof(GrapplingHookArtifact)
                .GetField("biteSoundId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(made, SfxId.None);

            return made;
        }

        [TearDown]
        public void TearDown()
        {
            if (target != null) Object.DestroyImmediate(target);
            if (hook != null) Object.DestroyImmediate(hook);
            if (player != null) Object.DestroyImmediate(player);
        }

        // ── Driving the item ───────────────────────────────────────────────────

        /// <summary>Equip, aim, press. Leaves the dart in the air, exactly as a real press does.</summary>
        private void Throw()
        {
            artifact.OnEquipped(player);

            var arg = new NetArg();
            artifact.OnRequestUse(ref arg);
            Assert.AreEqual(Attach, arg.B, "Test setup: the press was expected to hit the cube.");

            artifact.PlayUse(player, arg);
        }

        /// <summary>
        /// Land the dart. Time does not advance in edit mode, so the flight is collapsed by zeroing
        /// its duration — Update then reads a progress of 1 and bites on the spot.
        /// </summary>
        private void LandTheDart()
        {
            Set("_flightDuration", 0f);
            Invoke("Update");
        }

        private static void Invoke(GrapplingHookArtifact a, string method) =>
            typeof(GrapplingHookArtifact)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(a, null);

        private void Invoke(string method) => Invoke(artifact, method);

        private void Set(string field, object value) =>
            typeof(GrapplingHookArtifact)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(artifact, value);

        private T Get<T>(string field) => (T)typeof(GrapplingHookArtifact)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(artifact);

        /// <summary>
        /// Whether the winch is running.
        ///
        /// Read off the latch rather than a bare bool, which is what this used to be. Whether a
        /// grapple reels depends on the ORDER the release and the bite arrived in, and that order
        /// differs per machine — a local flight timer against a relayed message — so the answer
        /// moved into GrapplingHookArtifact.WinchLatch where it can be reasoned about on its own.
        /// See HoldLatchTests.
        /// </summary>
        private bool Winching => Get<GrapplingHookArtifact.WinchLatch>("_winch").Winching;

        // ── The hit is what takes control ──────────────────────────────────────

        [Test]
        public void TheThrowLeavesThePlayerInChargeUntilTheDartLands()
        {
            Throw();

            Assert.IsTrue(Get<bool>("_isShooting"), "Test setup: the dart should be in the air.");
            Assert.IsFalse(movement.IsTethered,
                "The rope has not caught anything yet. Anything that takes the player's movement " +
                "here is felt as being dragged by a hook that has not hit — which is exactly what " +
                "the 999-second DisableGroundSnap in Present used to do.");

            LandTheDart();

            Assert.IsTrue(movement.IsTethered,
                "The bite is the moment the player goes on the rope. If nothing claims their " +
                "movement here, the constraint fights the air-control lerp instead of replacing it.");
        }

        [Test]
        public void TheRopeIsAsLongAsTheGapAtTheMomentItCaught()
        {
            Throw();

            // Walk away from the anchor while the dart is still travelling.
            player.transform.position = new Vector3(0f, 0f, -5f);
            player.GetComponent<Rigidbody>().position = player.transform.position;
            Physics.SyncTransforms();

            LandTheDart();

            Assert.AreEqual(14.5f, Get<float>("_ropeLength"), 0.1f,
                "Measured at the bite, not at the press. A length captured when the trigger came " +
                "down is a free teleport toward the anchor for any player who moved while the dart " +
                "was in the air.");
        }

        // ── Catching is what starts the reel ───────────────────────────────────

        [Test]
        public void ATappedGrappleStillReelsIn()
        {
            Throw();

            // The ordinary click. A press and release is over in about a tenth of a second, and
            // the dart still has metres to travel — so this release ALWAYS lands first.
            artifact.PlayHold(player, default, active: false);

            LandTheDart();

            Assert.IsTrue(Winching,
                "The bite starts the winch. A release that arrived while the dart was still in " +
                "the air is not the player declining to be reeled in — it is just a click ending " +
                "sooner than a 50 m throw. Honouring it is what made a tapped grapple catch and " +
                "then hang there doing nothing.");
        }

        // ── The rope is a rope, not a winch that is always on ──────────────────

        [Test]
        public void SwingingDoesNotShortenTheRope()
        {
            Throw();
            LandTheDart();

            float thrown = Get<float>("_ropeLength");

            // Trigger released AFTER the rope caught — the deliberate gesture for trading the
            // climb for a swing, and the one release the item does honour.
            artifact.PlayHold(player, default, active: false);
            Assert.IsFalse(Winching,
                "Letting go once the rope is taut has to stop the winch, or there is no way to " +
                "choose a swing at all.");

            // Drift well inside the rope's own length, which is what the low point of every swing
            // does. Nothing about that is the player taking up line.
            player.transform.position = new Vector3(0f, 0f, 8f);
            player.GetComponent<Rigidbody>().position = player.transform.position;
            Physics.SyncTransforms();

            Invoke("FixedUpdate");

            Assert.AreEqual(thrown, Get<float>("_ropeLength"), 0.001f,
                "A rope that shortens whenever the player passes inside it ratchets its way up the " +
                "anchor for free, and a pendulum whose length quietly shrinks every pass is not a " +
                "pendulum. Only the winch is allowed to take up line.");
        }

        // ── The ground is not a counter to the grapple ─────────────────────────

        [Test]
        public void StandingOnTheGroundDoesNotConfiscateTheWinch()
        {
            // A floor directly under the player, so IsGrounded answers true for real.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -1.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);

            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.4f;

            // FixedUpdate reads inputs.MoveInput on its first line. The component is enough — its
            // Awake never runs in edit mode, so MoveInput stays Vector2.zero, which is precisely
            // the case under test: a player being winched who is pressing nothing.
            var inputs = player.AddComponent<Core.PlayerInputManager>();
            typeof(PlayerMovement)
                .GetField("inputs", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(movement, inputs);

            Physics.SyncTransforms();

            try
            {
                var rb = player.GetComponent<Rigidbody>();
                movement.SetTethered(true);

                // What the winch just put into the body, pulling toward an anchor across the room.
                rb.linearVelocity = new Vector3(0f, 0f, 14f);

                typeof(PlayerMovement)
                    .GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(movement, null);

                Assert.AreEqual(14f, rb.linearVelocity.z, 0.01f,
                    "Standing on the ground must not delete the winch's pull. The grounded branch " +
                    "sets horizontal velocity straight to the input target, and with no input that " +
                    "target is zero — so the pull was erased fifty times a second and the hook's " +
                    "own stall guard then dropped the rope. The ground was a hard counter.");
            }
            finally
            {
                Object.DestroyImmediate(floor);
            }
        }

        // ── How the harpoon ends up sitting in the wall ────────────────────────

        [Test]
        public void ThePlantedHarpoonKeepsTheAngleItWasFiredAt()
        {
            // A head to plant, and a muzzle well below the shot line so the throw arrives
            // clearly angled rather than square to the cube's near face.
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // Parked far away: it is a PREFAB here, and left at the origin its collider sits on
            // top of the camera and the aim ray hits the head instead of the target.
            head.transform.position = new Vector3(1000f, 1000f, 1000f);

            var muzzle = new GameObject("muzzle").transform;
            muzzle.position = new Vector3(0f, -4f, 0f);

            Set("hookHeadPrefab", head);
            Set("muzzle", muzzle);
            Set("hookHeadTipOffset", 0f);   // seat on the hit point, so this measures rotation only
            Set("hookHeadEmbed", 0f);

            try
            {
                Throw();
                LandTheDart();

                var planted = Get<Transform>("_head");
                Assert.IsNotNull(planted, "Test setup: a head should have been spawned.");

                // The cube's near face points back at -Z, so a normal-aligned plant plants at +Z
                // exactly. The throw came from below, so the fired angle must carry a +Y component.
                Vector3 forward = planted.forward;
                Assert.Greater(forward.y, 0.15f,
                    "The harpoon must keep the angle it arrived at. Planting along the surface " +
                    "normal instead snaps every shot square to the wall, so one thrown up at a " +
                    "ledge from below stands out of it like a nail hammered in from above.");
            }
            finally
            {
                // DestroyHead uses Destroy, which does nothing in edit mode, so the spawned
                // instance is cleaned up here rather than left in the scene for the next test.
                var spawned = Get<Transform>("_head");
                if (spawned != null) Object.DestroyImmediate(spawned.gameObject);

                Object.DestroyImmediate(head);
                if (muzzle != null) Object.DestroyImmediate(muzzle.gameObject);
            }
        }

        // ── Giving the body back ───────────────────────────────────────────────

        [Test]
        public void LosingTheItemMidSwingGivesTheBodyBack()
        {
            Throw();
            LandTheDart();
            Assert.IsTrue(movement.IsTethered, "Test setup: the player should be on the rope.");

            // Called directly rather than by deactivating the object: edit mode does not raise the
            // enable/disable callbacks, so the wiring is the one part of this that only play mode
            // can prove. What is worth pinning here is that the teardown path gives the body back
            // at all — a StopGrapple that forgets to is the bug, and it is silent.
            Invoke("OnDisable");

            Assert.IsFalse(movement.IsTethered,
                "The tether never expires by itself, unlike the 999-second timer it replaced. A " +
                "player who swapped weapons mid-swing would keep rope steering and lose fall " +
                "damage for the rest of the session.");
        }

        // ── A rope in the bag is not a rope you are still on ───────────────────

        [Test]
        public void ARopeLeftInTheSlotDoesNotHaulThePlayerBackToItsAnchor()
        {
            Throw();
            LandTheDart();

            // Swapping hotbar slot mid-swing. EquipmentController.Unequip captures the state
            // BEFORE OnUnequipped tears the item down, so the bag records a rope that stops
            // existing a moment later.
            var bag = new ItemState();
            artifact.CaptureItemState(bag);
            Assert.IsTrue(bag.Has("hook"), "Test setup: the swap should have recorded the live rope.");

            Object.DestroyImmediate(hook);
            artifact = NewArtifact();
            artifact.OnEquipped(player);

            // Then the player goes somewhere else entirely and comes back to the hook. A wing-pack
            // flight is what found this; anything that covers ground does it.
            var landed = new Vector3(0f, 0f, -200f);
            player.transform.position = landed;
            player.GetComponent<Rigidbody>().position = landed;
            Physics.SyncTransforms();

            artifact.RestoreItemState(bag);
            Invoke("FixedUpdate");

            Assert.AreEqual(landed, player.GetComponent<Rigidbody>().position,
                "Re-equipping put the player back on a rope whose anchor is 200 m away, and the " +
                "constraint's over-stretch clamp then wrote their Rigidbody onto that rope's " +
                "sphere — a silent teleport back to wherever they last used the hook. A record " +
                "the player cannot possibly still be attached to describes a rope that is gone.");
        }
    }
}
