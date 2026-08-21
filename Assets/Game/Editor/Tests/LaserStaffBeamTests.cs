// What the Laser Staff's beam does when nobody is watching it render.
//
// Three properties are worth pinning, and they are the three that would fail silently:
//
//   • The damage accumulator. NetDamage deals WHOLE points and throws away anything that rounds to
//     zero, so at 50 samples a second a naive `damage / rate` per tick floors to 0 and the beam
//     does nothing at all — while the same code at 4 ticks a second works perfectly. That failure
//     depends on a tuning number, not on the code, which is exactly the kind that ships.
//
//   • The hold timeout. It is the only thing standing between a dropped release packet and a beam
//     that burns forever at full damage with no way to stop it.
//
//   • Self-hit rejection. The aim ray starts inside the holder's own collider, so a plain
//     Physics.Raycast finds the person firing first.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class LaserStaffBeamTests
    {
        private GameObject player;
        private GameObject staff;
        private GameObject target;
        private LaserStaffArtifact artifact;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("player", typeof(AimProvider));

            var cam = new GameObject("cam", typeof(Camera));
            cam.transform.SetParent(player.transform, false);
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));

            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player.GetComponent<AimProvider>(), cam.GetComponent<Camera>());

            staff = new GameObject("laser staff");
            artifact = staff.AddComponent<LaserStaffArtifact>();
            artifact.OnEquipped(player);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (target != null) Object.DestroyImmediate(target);
            if (staff != null) Object.DestroyImmediate(staff);
            if (player != null) Object.DestroyImmediate(player);
        }

        // ─────────── Helpers ───────────

        private static FieldInfo Field(string name) =>
            typeof(LaserStaffArtifact).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static T Get<T>(object target, string name) => (T)Field(name).GetValue(target);

        private static void Set(object target, string name, object value) =>
            Field(name).SetValue(target, value);

        private static void Invoke(object target, string name)
        {
            typeof(LaserStaffArtifact)
                .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
        }

        /// <summary>One hold tick, as EquipmentController would send it.</summary>
        private NetArg Hold(bool active = true)
        {
            var arg = new NetArg { A = 0, B = active ? 1 : 0 };
            artifact.OnRequestHold(ref arg, active);
            artifact.PlayHold(player, arg, active);
            artifact.TryHold(player, arg, active);
            return arg;
        }

        private void PlaceTargetAhead(float z = 10f)
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = new Vector3(0f, 0f, z);
            target.AddComponent<HealthComponent>();
            SeedHealth();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Give the dummy far more health than any test spends.
        ///
        /// The default 100 is exactly what one second at the shipped 100 dps deals, which would
        /// put every arithmetic assertion right on the death boundary — and death is not a neutral
        /// event here: it fires OnDeath, drops loot and stops further damage landing. That would
        /// make these tests measure the clamp rather than the accumulator.
        /// </summary>
        private void SeedHealth()
        {
            var health = target.GetComponent<HealthComponent>();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            typeof(HealthComponent).GetField("maxHealth", flags).SetValue(health, 1_000_000);
            typeof(HealthComponent).GetField("currentHealth", flags).SetValue(health, 1_000_000);
        }

        /// <summary>Run the damage loop for <paramref name="seconds"/> of simulated time.</summary>
        private int DamageOver(float seconds, float frame = 1f / 60f)
        {
            var health = target.GetComponent<HealthComponent>();
            int before = health.GetHealth;

            for (float t = 0f; t < seconds; t += frame)
            {
                Set(artifact, "_damageTimer", Get<float>(artifact, "_damageTimer") + frame);

                // Time.deltaTime is not writable from an EditMode test, so the tick loop is driven
                // by pre-loading the accumulator and calling it with a zero delta. That exercises
                // the real spending arithmetic, which is the part under test.
                Invoke(artifact, "TickDamage");
            }

            return before - health.GetHealth;
        }

        // ─────────── The aim on the wire ───────────

        [Test]
        public void HoldTick_CarriesTheAimRay_NotTheEndpoint()
        {
            PlaceTargetAhead();

            NetArg arg = Hold();

            // The ray's ORIGIN, so every machine traces the same line. Sending the point it landed
            // on would let a client name any target it liked and would leave peers drawing to a
            // different place than the server bills.
            Assert.AreEqual(Vector3.zero, arg.P, "P should be the ray origin, not the hit point.");
            Assert.IsTrue(arg.HasOrientation, "R must carry a real rotation or peers fall back to their own camera.");
            Assert.AreEqual(Vector3.forward, arg.R * Vector3.forward);
        }

        [Test]
        public void ReleaseTick_CarriesNoAim()
        {
            NetArg arg = Hold(active: false);
            Assert.AreEqual(Vector3.zero, arg.P);
        }

        // ─────────── Ignition and extinction ───────────

        [Test]
        public void FirstHoldTick_LightsTheBeam()
        {
            Assert.IsFalse(Get<bool>(artifact, "_lit"));
            Hold();
            Assert.IsTrue(Get<bool>(artifact, "_lit"));
        }

        [Test]
        public void ReleaseTick_PutsItOut()
        {
            Hold();
            Hold(active: false);
            Assert.IsFalse(Get<bool>(artifact, "_lit"));
        }

        [Test]
        public void Unequipping_PutsItOut()
        {
            Hold();
            artifact.OnUnequipped(player);
            Assert.IsFalse(Get<bool>(artifact, "_lit"), "A staff put away, dropped or swapped must stop burning.");
        }

        [Test]
        public void StaleHoldStream_ExtinguishesItself()
        {
            Hold();
            Assert.IsTrue(Get<bool>(artifact, "_lit"));

            // The release that never arrived — a dropped packet, or a player who disconnected with
            // the button down. Rewinding the last-seen time is the same thing as time passing.
            float timeout = Get<float>(artifact, "holdTimeout");
            Set(artifact, "_lastHoldTime", Time.time - timeout - 0.1f);

            Invoke(artifact, "Update");

            Assert.IsFalse(Get<bool>(artifact, "_lit"),
                "Without this, one lost message leaves a beam burning at full damage forever.");
        }

        [Test]
        public void HoldTimeout_CannotBeTunedInsideTheSendInterval()
        {
            Set(artifact, "holdTimeout", 0.01f);
            Invoke(artifact, "OnValidate");

            Assert.GreaterOrEqual(Get<float>(artifact, "holdTimeout"), 0.2f,
                "A timeout shorter than the 15 Hz send interval would cut the beam between two ordinary ticks.");
        }

        // ─────────── Damage ───────────

        [Test]
        public void DamageIsSpentInWholePoints_AtTheConfiguredRate()
        {
            PlaceTargetAhead();
            Hold();
            Invoke(artifact, "Trace");

            Set(artifact, "damagePerSecond", 100f);
            Set(artifact, "damageTicksPerSecond", 50f);

            int dealt = DamageOver(1f);

            Assert.AreEqual(100, dealt, 2,
                "One second of beam should deal one second of damage.");
        }

        [Test]
        public void TickRateDoesNotChangeTheDamage()
        {
            PlaceTargetAhead();
            Hold();
            Invoke(artifact, "Trace");

            Set(artifact, "damagePerSecond", 100f);

            Set(artifact, "damageTicksPerSecond", 50f);
            int fast = DamageOver(1f);

            Set(artifact, "_damageCarry", 0f);
            Set(artifact, "_damageTimer", 0f);

            Set(artifact, "damageTicksPerSecond", 5f);
            int slow = DamageOver(1f);

            Assert.AreEqual(fast, slow, 2,
                "The tick rate is a sampling rate. If it changes the damage, the accumulator is broken.");
        }

        [Test]
        public void FractionalDamagePerTick_IsNotLostToRounding()
        {
            PlaceTargetAhead();
            Hold();
            Invoke(artifact, "Trace");

            // 30 dps over 50 ticks is 0.6 per tick — every single one of which floors to zero.
            // Without the carry this deals nothing at all, forever, and looks like a dead weapon.
            Set(artifact, "damagePerSecond", 30f);
            Set(artifact, "damageTicksPerSecond", 50f);

            int dealt = DamageOver(1f);

            Assert.AreEqual(30, dealt, 2,
                "Sub-1.0 per-tick damage must accumulate, not round away.");
        }

        [Test]
        public void BeamOffTarget_DealsNothing()
        {
            PlaceTargetAhead();
            Hold();

            // Aim at open sky. Nothing is hit, so nothing is billed.
            Set(artifact, "_hitObject", null);
            Set(artifact, "damagePerSecond", 100f);

            int dealt = DamageOver(1f);

            Assert.AreEqual(0, dealt);
        }

        // ─────────── Tracing ───────────

        [Test]
        public void Trace_SkipsTheHolder()
        {
            // The holder's own body, in front of the camera the ray starts at.
            //
            // Placed ahead rather than around the origin on purpose: a ray that STARTS inside a
            // convex collider is not reported as hitting it at all, so a collider straddling the
            // camera would make this test pass without the skip ever running. Standing it a couple
            // of metres down the barrel is what actually puts the holder in the ray's path.
            // Two of them, because a body is several colliders and the skip has to keep stepping
            // rather than clear the first one and give up on the second.
            for (int i = 0; i < 2; i++)
            {
                var self = GameObject.CreatePrimitive(PrimitiveType.Cube);
                self.name = $"holder body {i}";
                self.transform.SetParent(player.transform, false);
                self.transform.localPosition = new Vector3(0f, 0f, 2f + i * 2f);
            }

            PlaceTargetAhead();
            Hold();
            Invoke(artifact, "Trace");

            GameObject hit = Get<GameObject>(artifact, "_hitObject");

            Assert.AreEqual(target, hit,
                "The beam must skip its own holder, or firing it is a way to kill yourself.");
        }

        [Test]
        public void Trace_ReachesFullRangeWhenNothingIsThere()
        {
            Hold();
            Invoke(artifact, "Trace");

            Assert.IsNull(Get<GameObject>(artifact, "_hitObject"));
            Assert.AreEqual(
                Get<float>(artifact, "range"),
                Vector3.Distance(Vector3.zero, Get<Vector3>(artifact, "_endPoint")),
                0.01f);
        }

        [Test]
        public void Trace_TakesTheNearestHit()
        {
            PlaceTargetAhead(z: 20f);

            var near = GameObject.CreatePrimitive(PrimitiveType.Cube);
            near.name = "nearer wall";
            near.transform.position = new Vector3(0f, 0f, 5f);
            Physics.SyncTransforms();

            Hold();
            Invoke(artifact, "Trace");

            GameObject hit = Get<GameObject>(artifact, "_hitObject");

            // Read the name BEFORE destroying it. `hit` IS the near cube, and Unity's overloaded
            // == reports a destroyed object as null — so tearing it down first turns a correct hit
            // into "<nothing>" and the test fails describing a bug that is not there.
            string hitName = hit != null ? hit.name : "<nothing>";
            float endZ = Get<Vector3>(artifact, "_endPoint").z;

            Object.DestroyImmediate(near);

            Assert.AreEqual("nearer wall", hitName);
            Assert.AreEqual(4.5f, endZ, 0.6f,
                "The beam must stop at the first thing in its way, not at the furthest one it can see.");
        }

        // ─────────── The shared layer it rides on ───────────

        [Test]
        public void ContinuousItems_AreOptIn()
        {
            Assert.IsTrue(artifact.IsContinuous);

            var lightning = new GameObject("lightning").AddComponent<LightningSpell>();
            Assert.IsFalse(lightning.IsContinuous,
                "Every item written before the staff must be untouched by the held-use path.");

            Object.DestroyImmediate(lightning.gameObject);
        }

        [Test]
        public void HoldMessageIds_AreDistinct()
        {
            var ids = new List<ushort>
            {
                NetMsg.UseItem, NetMsg.ItemUsed, NetMsg.UseItemHold, NetMsg.ItemUseHeld,
            };

            CollectionAssert.AllItemsAreUnique(ids,
                "Message ids travel between builds; a reused one routes to the wrong handler.");
        }

        [Test]
        public void BeamIsServerAuthoritative()
        {
            Assert.AreEqual(UseAuthority.Server, artifact.Authority,
                "Damage is shared world state. An owner-run beam would bill the target once per watching player.");
        }
    }
}
