using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The flight model is pure functions, so its behaviour is asserted directly rather than inferred
    /// from watching a prefab fly. These are behaviour tests, not equation tests: they say what a
    /// pilot should experience, which is what has to keep working when the constants are retuned.
    /// </summary>
    public class OrnithopterFlightModelTests
    {
        private const float Dt = 1f / 50f;      // one physics step

        private static OrnithopterFlightConfig Config() => new OrnithopterFlightConfig();

        /// <summary>Run the model forward, returning the final state.</summary>
        private static OrnithopterFlightState Fly(OrnithopterFlightState s, OrnithopterFlightInput input,
                                                  OrnithopterFlightConfig cfg, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
                s = OrnithopterFlightModel.Step(s, input, cfg, Dt);
            return s;
        }

        /// <summary>A craft already in the air with its wings open and trimmed level.</summary>
        private static OrnithopterFlightState Airborne(float speed = 20f)
        {
            OrnithopterFlightState s = OrnithopterFlightState.Launch(speed, 0f);
            s.Deployment = 1f;
            s.WingSpread = 1f;
            return s;
        }

        // ─────────── Gliding ───────────

        [Test]
        public void Gliding_LosesAltitude()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Fly(Airborne(), OrnithopterFlightInput.Neutral, cfg, 4f);

            Assert.Less(s.Gamma, 0f,
                "A glider with no thrust must descend: the flight path angle should settle below " +
                $"level, but it was {s.Gamma:F2} deg.");
        }

        [Test]
        public void Gliding_HoldsSpeedRoughly()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Fly(Airborne(20f), OrnithopterFlightInput.Neutral, cfg, 6f);

            // A glide converges on a trim speed. The exact number is a tuning matter; that it does not
            // run away to zero or to a hundred is the behaviour that must hold.
            Assert.Greater(s.Airspeed, 8f, "A glide bled off all its speed.");
            Assert.Less(s.Airspeed, 45f, "A glide ran away to an absurd speed.");
        }

        [Test]
        public void Gliding_IsShallowerThanADive()
        {
            OrnithopterFlightConfig cfg = Config();
            var noseDown = new OrnithopterFlightInput(-1f, 0f, 0f, 0f);

            float glide = Fly(Airborne(), OrnithopterFlightInput.Neutral, cfg, 3f).Gamma;
            float dive = Fly(Airborne(), noseDown, cfg, 3f).Gamma;

            Assert.Less(dive, glide,
                $"Pushing the nose down must steepen the descent: glide {glide:F1} deg, dive {dive:F1} deg.");
        }

        // ─────────── Energy ───────────

        [Test]
        public void Diving_TradesAltitudeForSpeed()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState start = Airborne(15f);
            var noseDown = new OrnithopterFlightInput(-1f, 0f, 0f, 0f);

            OrnithopterFlightState s = Fly(start, noseDown, cfg, 3f);

            Assert.Greater(s.Airspeed, start.Airspeed,
                $"A dive must build speed: {start.Airspeed:F1} -> {s.Airspeed:F1} m/s.");
            Assert.Less(s.Gamma, 0f, "A dive must point the flight path downward.");
        }

        [Test]
        public void Climbing_CostsSpeed()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState start = Airborne(30f);
            var noseUp = new OrnithopterFlightInput(1f, 0f, 0f, 0f);

            // No flapping: the only place the energy to climb can come from is the airspeed.
            OrnithopterFlightState s = Fly(start, noseUp, cfg, 2f);

            Assert.Less(s.Airspeed, start.Airspeed,
                $"Climbing without thrust must cost speed: {start.Airspeed:F1} -> {s.Airspeed:F1} m/s.");
        }

        [Test]
        public void Flapping_Climbs()
        {
            OrnithopterFlightConfig cfg = Config();
            var flapping = new OrnithopterFlightInput(0.3f, 0f, 1f, 0f);

            float glide = Fly(Airborne(), OrnithopterFlightInput.Neutral, cfg, 3f).Gamma;
            float beat = Fly(Airborne(), flapping, cfg, 3f).Gamma;

            Assert.Greater(beat, glide,
                $"Flapping must buy climb over a glide: glide {glide:F1} deg, flapping {beat:F1} deg.");
        }

        // ─────────── Stalling ───────────

        [Test]
        public void LiftCollapsesPastTheStallAngle()
        {
            OrnithopterFlightConfig cfg = Config();

            float atStall = OrnithopterFlightModel.LiftCoefficient(cfg.StallAngle, cfg);
            float wellPast = OrnithopterFlightModel.LiftCoefficient(cfg.StallAngle + cfg.StallFadeAngle, cfg);

            Assert.Less(wellPast, atStall,
                "Past the stall angle the wing must make LESS lift, not more.");
            Assert.Greater(wellPast, 0f,
                "Post-stall lift must stay above zero — a wing making no lift never lowers its own " +
                "nose, so the stall would never end.");
        }

        [Test]
        public void HaulingTheNoseUpAtLowSpeedStalls()
        {
            OrnithopterFlightConfig cfg = Config();
            var noseUp = new OrnithopterFlightInput(1f, 0f, 0f, 0f);

            OrnithopterFlightState s = Fly(Airborne(12f), noseUp, cfg, 3f);

            Assert.IsTrue(s.Stalled,
                $"Holding full nose-up from low speed must stall: AoA reached {s.AngleOfAttack:F1} deg " +
                $"against a stall angle of {cfg.StallAngle:F1}.");
        }

        [Test]
        public void AStallIsRecoverable()
        {
            OrnithopterFlightConfig cfg = Config();

            // Stall it, then do what a pilot does: let go and push the nose down.
            OrnithopterFlightState s = Fly(Airborne(12f), new OrnithopterFlightInput(1f, 0f, 0f, 0f), cfg, 3f);
            Assert.IsTrue(s.Stalled, "Setup failed: expected to be stalled before recovering.");

            s = Fly(s, new OrnithopterFlightInput(-1f, 0f, 0f, 0f), cfg, 4f);

            Assert.IsFalse(s.Stalled,
                $"Pushing the nose down must break the stall; still stalled at {s.AngleOfAttack:F1} deg AoA.");
            Assert.Greater(s.Airspeed, 10f, "Recovery must restore flying speed.");
        }

        [Test]
        public void StallSpeedSupportsTheCraftInLevelFlight()
        {
            OrnithopterFlightConfig cfg = Config();
            float vStall = OrnithopterFlightModel.StallSpeed(cfg);

            Assert.Greater(vStall, 0f, "Stall speed must be a real number.");

            // At the stall speed and peak lift coefficient, lift should just balance weight. That is
            // the definition, so it is a check that the derivation agrees with the model it describes.
            float clMax = cfg.LiftSlopePerDegree * cfg.StallAngle;
            float lift = 0.5f * cfg.AirDensity * vStall * vStall * cfg.WingArea * clMax;
            Assert.That(lift, Is.EqualTo(cfg.Mass * OrnithopterFlightModel.Gravity).Within(1f),
                "Stall speed must be the speed at which peak lift equals weight.");
        }

        // ─────────── Turning ───────────

        [Test]
        public void BankingTurns()
        {
            OrnithopterFlightConfig cfg = Config();
            var bankRight = new OrnithopterFlightInput(0f, 1f, 0f, 0f);

            OrnithopterFlightState s = Fly(Airborne(), bankRight, cfg, 3f);

            Assert.Greater(s.Roll, 1f, "Right stick must produce right bank.");
            Assert.AreNotEqual(0f, s.TurnRate, "A banked wing must be turning.");
            Assert.Greater(s.TurnRate, 0f, "Banking right must yaw right.");
        }

        [Test]
        public void BankingLeftTurnsTheOtherWay()
        {
            OrnithopterFlightConfig cfg = Config();
            float right = Fly(Airborne(), new OrnithopterFlightInput(0f, 1f, 0f, 0f), cfg, 2f).TurnRate;
            float left = Fly(Airborne(), new OrnithopterFlightInput(0f, -1f, 0f, 0f), cfg, 2f).TurnRate;

            Assert.Greater(right, 0f);
            Assert.Less(left, 0f);
        }

        [Test]
        public void RollSelfCentresWhenTheStickIsReleased()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Fly(Airborne(), new OrnithopterFlightInput(0f, 1f, 0f, 0f), cfg, 2f);
            Assert.Greater(s.Roll, 5f, "Setup failed: expected to be banked.");

            s = Fly(s, OrnithopterFlightInput.Neutral, cfg, 3f);

            Assert.That(s.Roll, Is.EqualTo(0f).Within(1f),
                $"Releasing the stick must return the craft to wings-level; still at {s.Roll:F1} deg.");
        }

        // ─────────── Stamina ───────────

        [Test]
        public void FlappingDrainsStaminaAndGlidingRestoresIt()
        {
            OrnithopterFlightConfig cfg = Config();
            var flapping = new OrnithopterFlightInput(0f, 0f, 1f, 0f);

            OrnithopterFlightState tired = Fly(Airborne(), flapping, cfg, 3f);
            Assert.Less(tired.Stamina, 1f, "Flapping must cost stamina.");

            OrnithopterFlightState rested = Fly(tired, OrnithopterFlightInput.Neutral, cfg, 3f);
            Assert.Greater(rested.Stamina, tired.Stamina, "Gliding must restore stamina.");
        }

        [Test]
        public void ExhaustionWeakensTheBeat()
        {
            OrnithopterFlightConfig cfg = Config();
            var flapping = new OrnithopterFlightInput(0f, 0f, 1f, 0f);

            OrnithopterFlightState fresh = OrnithopterFlightModel.Step(Airborne(), flapping, cfg, Dt);

            OrnithopterFlightState spent = Airborne();
            spent.Stamina = 0f;
            spent = OrnithopterFlightModel.Step(spent, flapping, cfg, Dt);

            Assert.Less(spent.FlapEffort, fresh.FlapEffort,
                "An exhausted pilot must beat more weakly than a fresh one.");
        }

        // ─────────── Wings ───────────

        [Test]
        public void TuckingFoldsTheWings()
        {
            OrnithopterFlightConfig cfg = Config();
            var tucked = new OrnithopterFlightInput(0f, 0f, -1f, 0f);

            OrnithopterFlightState open = OrnithopterFlightModel.Step(Airborne(), OrnithopterFlightInput.Neutral, cfg, Dt);
            OrnithopterFlightState folded = OrnithopterFlightModel.Step(Airborne(), tucked, cfg, Dt);

            Assert.Less(folded.WingSpread, open.WingSpread, "Tucking must fold the wings in.");
        }

        [Test]
        public void FoldedWingsMakeLessLiftThanOpenOnes()
        {
            OrnithopterFlightConfig cfg = Config();

            // Same attitude, same speed, different spread — the descent must be steeper folded.
            OrnithopterFlightState open = Airborne();
            OrnithopterFlightState folded = Airborne();
            folded.Deployment = 0.2f;

            float openGamma = Fly(open, OrnithopterFlightInput.Neutral, cfg, 2f).Gamma;
            float foldedGamma = Fly(folded, OrnithopterFlightInput.Neutral, cfg, 2f).Gamma;

            Assert.Less(foldedGamma, openGamma,
                $"Folded wings must sink faster: open {openGamma:F1} deg, folded {foldedGamma:F1} deg.");
        }

        [Test]
        public void FlapPhaseAdvancesEvenWhileGliding()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Airborne();
            float before = s.FlapPhase;

            s = Fly(s, OrnithopterFlightInput.Neutral, cfg, 0.5f);

            Assert.AreNotEqual(before, s.FlapPhase,
                "The wings must keep breathing while gliding, not freeze mid-beat.");
        }

        // ─────────── Kinematics ───────────

        [Test]
        public void VelocityFollowsHeadingAndFlightPath()
        {
            var s = OrnithopterFlightState.Launch(10f, 90f);   // due +X
            Vector3 v = OrnithopterFlightModel.VelocityOf(s);

            Assert.That(v.x, Is.EqualTo(10f).Within(0.01f), "Heading 90 deg must fly along +X.");
            Assert.That(v.y, Is.EqualTo(0f).Within(0.01f), "Level flight must have no vertical speed.");
            Assert.That(v.z, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void ClimbingFlightPathGivesUpwardVelocity()
        {
            var s = OrnithopterFlightState.Launch(10f, 0f);
            s.Gamma = 30f;

            Vector3 v = OrnithopterFlightModel.VelocityOf(s);

            Assert.That(v.y, Is.EqualTo(5f).Within(0.01f), "30 deg climb at 10 m/s is 5 m/s up.");
            Assert.Greater(v.z, 0f, "Heading 0 must still be flying forward along +Z.");
        }

        [Test]
        public void AZeroTimestepIsANoOp()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Airborne();
            OrnithopterFlightState after = OrnithopterFlightModel.Step(s, OrnithopterFlightInput.Neutral, cfg, 0f);

            Assert.AreEqual(s.Airspeed, after.Airspeed);
            Assert.AreEqual(s.FlapPhase, after.FlapPhase);
        }

        [Test]
        public void TheModelIsStableOverALongFlight()
        {
            OrnithopterFlightConfig cfg = Config();
            OrnithopterFlightState s = Airborne();

            // Two minutes of varied input. Nothing here should ever produce a NaN — a single one
            // propagates into the Rigidbody and Unity removes the object from the simulation.
            for (int i = 0; i < 6000; i++)
            {
                float t = i * Dt;
                var input = new OrnithopterFlightInput(
                    Mathf.Sin(t * 0.7f), Mathf.Sin(t * 0.3f), Mathf.Sin(t * 0.5f), 0f);
                s = OrnithopterFlightModel.Step(s, input, cfg, Dt);
            }

            Assert.IsFalse(float.IsNaN(s.Airspeed) || float.IsInfinity(s.Airspeed), "Airspeed diverged.");
            Assert.IsFalse(float.IsNaN(s.Gamma) || float.IsInfinity(s.Gamma), "Flight path angle diverged.");
            Assert.IsFalse(float.IsNaN(s.Heading) || float.IsInfinity(s.Heading), "Heading diverged.");
            Assert.IsFalse(float.IsNaN(s.Roll) || float.IsInfinity(s.Roll), "Roll diverged.");
            Assert.GreaterOrEqual(s.Airspeed, 0f, "Airspeed must never go negative.");
        }
    }
}
