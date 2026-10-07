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

        /// <summary>
        /// The press, as EquipmentController sends it: request, present, then the authority half.
        ///
        /// This is what LIGHTS the arc. Hold ticks only steer it — a burn is three seconds long
        /// whatever the button does, so a test that lights the staff by holding it is testing a
        /// path that no longer exists.
        /// </summary>
        private NetArg Fire()
        {
            var arg = new NetArg { A = 0 };
            artifact.OnRequestUse(ref arg);
            artifact.PlayUse(player, arg);
            artifact.TryUse(player, arg);
            return arg;
        }

        /// <summary>Forget the recharge, for tests that fire more than once.</summary>
        private void ClearCooldown() => Set(artifact, "_cooldownEndsAt", 0f);

        /// <summary>
        /// Reach a private field on UsableItem itself. GetField on the derived type does not return
        /// the base's private members, so the charge count needs its own accessor.
        /// </summary>
        private static void SetBase(object target, string name, object value) =>
            typeof(UsableItem)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

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
        public void ThePress_LightsTheBeam()
        {
            Assert.IsFalse(Get<bool>(artifact, "_lit"));
            Fire();
            Assert.IsTrue(Get<bool>(artifact, "_lit"));
        }

        [Test]
        public void HoldTicks_DoNotLightAnything()
        {
            Hold();

            Assert.IsFalse(Get<bool>(artifact, "_lit"),
                "The trigger fires the staff. A tick that could light it would let a held button " +
                "restart the burn the moment the cooldown lapsed.");
        }

        [Test]
        public void ReleaseTick_PutsItOut()
        {
            Fire();
            Hold(active: false);
            Assert.IsFalse(Get<bool>(artifact, "_lit"));
        }

        [Test]
        public void Unequipping_PutsItOut()
        {
            Fire();
            artifact.OnUnequipped(player);
            Assert.IsFalse(Get<bool>(artifact, "_lit"), "A staff put away, dropped or swapped must stop burning.");
        }

        [Test]
        public void StaleHoldStream_ExtinguishesItself()
        {
            Fire();
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

        // ─────────── The burst and the recharge ───────────
        //
        // Three seconds on, ten seconds off, and the button gets no say in either. Everything here
        // fails silently if it breaks: a burn that never ends looks like a working weapon until
        // somebody notices it never stopped, and a cooldown you can skip by scrolling hotbar slots
        // looks like a working cooldown right up until the first player tries it.

        [Test]
        public void Firing_StartsAFixedLengthBurn()
        {
            Fire();

            Assert.AreEqual(Time.time + Get<float>(artifact, "burnDuration"),
                            Get<float>(artifact, "_burnEndsAt"), 0.01f);
        }

        [Test]
        public void TheBurn_EndsItself()
        {
            Fire();

            // The three seconds, spent. Rewinding the deadline is the same thing as time passing.
            Set(artifact, "_burnEndsAt", Time.time - 0.01f);
            Invoke(artifact, "Update");

            Assert.IsFalse(Get<bool>(artifact, "_lit"),
                "The staff times its own burn. Nothing else is going to stop it.");
        }

        [Test]
        public void TheBurn_OutlivesTheButton()
        {
            Fire();

            Assert.IsTrue(artifact.WantsHold,
                "EquipmentController streams the aim only while this is true. False here and a " +
                "tapped shot freezes every other machine's aim at the instant of the press.");

            Set(artifact, "_burnEndsAt", Time.time - 0.01f);
            Invoke(artifact, "Update");

            Assert.IsFalse(artifact.WantsHold, "…and the stream has to end when the burn does.");
        }

        [Test]
        public void HoldingTheButton_DoesNotExtendTheBurn()
        {
            Fire();
            float deadline = Get<float>(artifact, "_burnEndsAt");

            Hold();
            Hold();

            Assert.AreEqual(deadline, Get<float>(artifact, "_burnEndsAt"), 0.0001f);
        }

        [Test]
        public void Firing_StartsTheRecharge_CoveringTheBurnToo()
        {
            Fire();

            float expected = Time.time
                           + Get<float>(artifact, "burnDuration")
                           + Get<float>(artifact, "cooldown");

            Assert.AreEqual(expected, Get<float>(artifact, "_cooldownEndsAt"), 0.05f,
                "Stamped at ignition, not when the burn ends. On a host Present runs before the " +
                "server's Use, so a gate that asked \"is it burning?\" would refuse a press the " +
                "arc had already answered — and spend a charge doing it.");
        }

        [Test]
        public void TheRecharge_RefusesASecondShot()
        {
            Fire();
            Set(artifact, "_burnEndsAt", Time.time - 0.01f);
            Invoke(artifact, "Update");

            Fire();

            Assert.IsFalse(Get<bool>(artifact, "_lit"),
                "Ten seconds means ten seconds, on every machine — the press is presented locally " +
                "before the server has said anything, so the owner has to refuse it itself.");
        }

        [Test]
        public void TheRecharge_Lapses()
        {
            Fire();
            Set(artifact, "_burnEndsAt", Time.time - 0.01f);
            Invoke(artifact, "Update");

            ClearCooldown();
            Fire();

            Assert.IsTrue(Get<bool>(artifact, "_lit"));
        }

        [Test]
        public void TheRecharge_SurvivesAHotbarSwap()
        {
            Fire();

            // Exactly the order Unequip uses: the slot is written back BEFORE anything is put out,
            // so a staff swapped away mid-burn has to report the recharge it is about to owe rather
            // than the one it has not started yet.
            var state = new ItemState();
            artifact.CaptureItemState(state);

            Assert.Greater(state.GetFloat("staffCooldown", 0f), 0f,
                "A cooldown living only on the instance is a cooldown you skip by scrolling away " +
                "and back — the held object is destroyed and rebuilt on every hotbar change.");

            var swapped = new GameObject("second staff").AddComponent<LaserStaffArtifact>();
            swapped.RestoreItemState(state);

            bool blocked = Get<float>(swapped, "_cooldownEndsAt") > Time.time;
            Object.DestroyImmediate(swapped.gameObject);

            Assert.IsTrue(blocked);
        }

        [Test]
        public void TheRecharge_IsStoredAsSecondsRemaining_NotAsADeadline()
        {
            Set(artifact, "_cooldownEndsAt", Time.time + 4f);

            var state = new ItemState();
            artifact.CaptureItemState(state);

            Assert.AreEqual(4f, state.GetFloat("staffCooldown", 0f), 0.05f,
                "Time.time restarts at zero every session, so a stored deadline comes back either " +
                "already spent or hours away.");
        }

        [Test]
        public void ADarkStaff_DoesNotKeepRestampingItsRecharge()
        {
            Fire();
            Set(artifact, "_burnEndsAt", Time.time - 0.01f);
            Invoke(artifact, "Update");

            float deadline = Get<float>(artifact, "_cooldownEndsAt");

            // Every one of these calls Extinguish on a staff that is already out.
            Hold(active: false);
            artifact.OnUnequipped(player);
            Invoke(artifact, "Update");

            Assert.AreEqual(deadline, Get<float>(artifact, "_cooldownEndsAt"), 0.0001f,
                "Scrolling past a recharging staff must not push its next shot ten seconds further away.");
        }

        // ─────────── Charges ───────────
        //
        // Fire() runs Present BEFORE the authority half, which is exactly the order a host uses.
        // Every gate the recharge could plausibly have been written as is already true by then, so
        // these three are the ones that catch a staff spending charges it should not — or, worse,
        // never spending them at all on the one machine most people play on.

        [Test]
        public void APress_SpendsItsCharge_EvenThoughPresentRanFirst()
        {
            SetBase(artifact, "maxUses", 1);

            bool depleted = false;
            artifact.OnItemDepleted += _ => depleted = true;

            Fire();

            Assert.IsTrue(depleted,
                "By the time the server's half of a host press is reached the arc is already lit " +
                "and the recharge already stamped. A gate reading either would skip the charge, " +
                "and a limited-use staff would fire forever on a host.");
        }

        [Test]
        public void APressDuringTheRecharge_SpendsNothing()
        {
            SetBase(artifact, "maxUses", 1);
            Set(artifact, "_cooldownEndsAt", Time.time + 5f);

            bool depleted = false;
            artifact.OnItemDepleted += _ => depleted = true;

            Fire();

            Assert.IsFalse(Get<bool>(artifact, "_lit"));
            Assert.IsFalse(depleted, "A press the staff refused must not cost a charge.");
        }

        [Test]
        public void APressDuringTheBurn_SpendsNothing()
        {
            SetBase(artifact, "maxUses", 2);

            bool depleted = false;
            artifact.OnItemDepleted += _ => depleted = true;

            Fire();
            Fire();

            Assert.IsFalse(depleted,
                "Mashing the button through a burn changes nothing, so it must cost nothing.");
        }

        // ─────────── The arc ───────────

        [Test]
        public void TheArc_IsBentGeometry_PinnedAtBothEnds()
        {
            var lineObject = new GameObject("beam", typeof(LineRenderer));
            var line = lineObject.GetComponent<LineRenderer>();
            Set(artifact, "beam", line);

            var start = new Vector3(0f, 1f, 0f);
            var end = new Vector3(0f, 1f, 20f);

            typeof(LaserStaffArtifact)
                .GetMethod("BuildArc", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(artifact, new object[] { start, end });

            int segments = Get<int>(artifact, "arcSegments");
            int count = line.positionCount;

            Vector3 first = line.GetPosition(0);
            Vector3 last = line.GetPosition(count - 1);

            // The widest the arc strays from the straight line between the two ends.
            float widest = 0f;
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 straight = Vector3.Lerp(start, end, (float)i / (count - 1));
                widest = Mathf.Max(widest, Vector3.Distance(line.GetPosition(i), straight));
            }

            Object.DestroyImmediate(lineObject);

            Assert.AreEqual(segments + 1, count,
                "The kinks are real points. Painting a bolt into the UV of a straight ribbon looks " +
                "like a bolt only until the beam sweeps.");
            Assert.AreEqual(start, first, "The arc must start in the muzzle…");
            Assert.AreEqual(end, last, "…and end exactly where Trace found the surface it is billing.");
            Assert.Greater(widest, 0.01f, "A bolt that does not stray from the straight line is a laser.");
        }

        // ─────────── Damage ───────────

        [Test]
        public void DamageIsSpentInWholePoints_AtTheConfiguredRate()
        {
            PlaceTargetAhead();
            Fire();
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
            Fire();
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
            Fire();
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
            Fire();

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
            Fire();
            Hold();
            Invoke(artifact, "Trace");

            GameObject hit = Get<GameObject>(artifact, "_hitObject");

            Assert.AreEqual(target, hit,
                "The beam must skip its own holder, or firing it is a way to kill yourself.");
        }

        [Test]
        public void Trace_ReachesFullRangeWhenNothingIsThere()
        {
            Fire();
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

            Fire();
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
