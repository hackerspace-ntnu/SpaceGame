// The rider's turn channel on a lateral traveller.
//
// A crab spends the stick's X axis on STRAFE, so before this existed a mounted rider could translate
// the machine but never change its heading -- the AI channel turned it, the rider could not. The fix
// is a dedicated Turn axis on RiderInput, fed by an optional SteerModule action, in the same shape
// the optional `verticalAction` already had.
//
// What these tests pin, and why each one is here:
//
//   * strafe and turn are live AT THE SAME TIME on a lateral traveller. That is the whole point; a
//     fix that made turning work by taking strafe away would pass a naive "does it turn" check.
//   * a machine that is NOT a lateral traveller is untouched -- Move.x still turns it and the new
//     axis is ignored. Every machine built before the crab is in this case.
//   * the 3-argument RiderInput still means exactly what it did. It is what every other motor
//     constructs, and a silent change there would move the ostrich, the horse and every vehicle.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RiderTurnChannelTests
    {
        private GameObject go;

        /// LeggedDriver is abstract and its channels are protected; this exposes them without widening
        /// the production surface. `lateralSteering` is a private serialized field, so it is set the
        /// only way a test can set one: reflection.
        private class ProbeDriver : LeggedDriver
        {
            public float TurnChannel => turn;
            public float StrafeChannel => strafe;
            public float ForwardChannel => forward;

            public void SetLateral(bool on)
            {
                typeof(LeggedDriver)
                    .GetField("lateralSteering", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(this, on);
            }
        }

        [SetUp]
        public void SetUp() => go = new GameObject("RiderTurnProbe");

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            go = null;
        }

        private ProbeDriver Driver(bool lateral)
        {
            var d = go.AddComponent<ProbeDriver>();
            d.SetLateral(lateral);
            return d;
        }

        // ─────────── the lateral traveller (the crab) ───────────

        [Test]
        public void ALateralTravellerTurnsFromTheDedicatedAxis()
        {
            ProbeDriver d = Driver(true);

            d.ApplyRiderInput(new RiderInput(Vector2.zero, 0f, false, 1f), 1f / 60f);

            Assert.AreEqual(1f, d.TurnChannel, 1e-5f,
                "the rider's turn axis did not reach the driver's turn channel");
        }

        [Test]
        public void ALateralTravellerStrafesAndTurnsAtTheSameTime()
        {
            ProbeDriver d = Driver(true);

            // Stick hard right (strafe) while turning hard left -- a crab sidling around a target.
            d.ApplyRiderInput(new RiderInput(new Vector2(1f, 0.5f), 0f, false, -1f), 1f / 60f);

            Assert.AreEqual(1f, d.StrafeChannel, 1e-5f, "strafe was lost when a turn was commanded");
            Assert.AreEqual(-1f, d.TurnChannel, 1e-5f, "turn was lost when a strafe was commanded");
            Assert.AreEqual(0.5f, d.ForwardChannel, 1e-5f, "throttle was disturbed");
        }

        [Test]
        public void TheTurnAxisIsClampedLikeEveryOtherChannel()
        {
            ProbeDriver d = Driver(true);

            d.ApplyRiderInput(new RiderInput(Vector2.zero, 0f, false, 4f), 1f / 60f);
            Assert.AreEqual(1f, d.TurnChannel, 1e-5f, "an out-of-range turn axis was not clamped");

            d.ApplyRiderInput(new RiderInput(Vector2.zero, 0f, false, -4f), 1f / 60f);
            Assert.AreEqual(-1f, d.TurnChannel, 1e-5f, "an out-of-range turn axis was not clamped");
        }

        [Test]
        public void ALateralTravellerWithNoTurnAxisBoundStillStrafesExactlyAsBefore()
        {
            ProbeDriver d = Driver(true);

            // What SteerModule sends when `turnActionName` is blank: the 3-argument input, turn 0.
            d.ApplyRiderInput(new RiderInput(new Vector2(1f, 0f), 0f, false), 1f / 60f);

            Assert.AreEqual(1f, d.StrafeChannel, 1e-5f, "strafe stopped working when no turn was bound");
            Assert.AreEqual(0f, d.TurnChannel, 1e-5f, "an unbound turn axis invented a heading change");
        }

        // ─────────── every machine built before the crab ───────────

        [Test]
        public void AHeadingSteeredMachineStillTurnsWithTheStick()
        {
            ProbeDriver d = Driver(false);

            d.ApplyRiderInput(new RiderInput(new Vector2(1f, 0f), 0f, false), 1f / 60f);

            Assert.AreEqual(1f, d.TurnChannel, 1e-5f, "Move.x stopped turning a heading-steered machine");
            Assert.AreEqual(0f, d.StrafeChannel, 1e-5f, "a machine that cannot strafe was given a strafe");
        }

        [Test]
        public void AHeadingSteeredMachineIgnoresTheDedicatedTurnAxis()
        {
            ProbeDriver d = Driver(false);

            // Turn axis pushed hard, stick centred: the ostrich must not creep.
            d.ApplyRiderInput(new RiderInput(Vector2.zero, 0f, false, 1f), 1f / 60f);

            Assert.AreEqual(0f, d.TurnChannel, 1e-5f,
                "the dedicated turn axis leaked into a machine that steers from Move.x");
        }

        // ─────────── the struct every other motor constructs ───────────

        [Test]
        public void TheThreeArgumentRiderInputStillMeansWhatItDid()
        {
            var legacy = new RiderInput(new Vector2(0.25f, -0.5f), 0.75f, true);

            Assert.AreEqual(new Vector2(0.25f, -0.5f), legacy.Move);
            Assert.AreEqual(0.75f, legacy.Vertical, 1e-5f);
            Assert.IsTrue(legacy.IsRunning);
            Assert.AreEqual(0f, legacy.Turn, 1e-5f,
                "the 3-argument constructor must leave Turn at zero or every existing motor changes");
        }

        [Test]
        public void TheFourArgumentRiderInputCarriesTurnWithoutDisturbingTheRest()
        {
            var input = new RiderInput(new Vector2(0.25f, -0.5f), 0.75f, true, -0.6f);

            Assert.AreEqual(new Vector2(0.25f, -0.5f), input.Move);
            Assert.AreEqual(0.75f, input.Vertical, 1e-5f);
            Assert.IsTrue(input.IsRunning);
            Assert.AreEqual(-0.6f, input.Turn, 1e-5f);
        }
    }
}
