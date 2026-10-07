// The stick: how a first-person player's mouse becomes a wing's attitude, and what a landing
// costs.
//
// These are the parts with an answer worth checking. "Fly where you look" is a claim about two
// opposite control laws sitting next to each other — the nose is a POSITION the mouse aims, the
// bank is a RATE the mouse pushes — and getting either the wrong way round produces something that
// still flies and feels wrong in a way nobody can name.
using NUnit.Framework;
using SpaceGame.Gear.Wingsuit;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class WingsuitControlTests
    {
        [Test]
        public void TheNoseIsAimedAndStaysWhereItIsAimed()
        {
            // A position control: the mouse moves the commanded angle and nothing brings it back.
            float commanded = WingsuitControl.AimNose(0f, 12f, maxPitch: 70f);
            Assert.AreEqual(12f, commanded, 1e-4f);

            // ...clamped to what the suit will hold, from both ends.
            Assert.AreEqual(70f, WingsuitControl.AimNose(60f, 40f, 70f), 1e-4f);
            Assert.AreEqual(-70f, WingsuitControl.AimNose(-60f, -40f, 70f), 1e-4f);

            // The stick handed to the model is the ERROR, so the model keeps its rate limiting and
            // its stall fade. Hard over well before the nose arrives, and zero once it has.
            Assert.AreEqual(1f, WingsuitControl.NoseStick(20f, 0f, saturationDegrees: 6f), 1e-4f);
            Assert.AreEqual(-1f, WingsuitControl.NoseStick(-20f, 0f, 6f), 1e-4f);
            Assert.AreEqual(0f, WingsuitControl.NoseStick(15f, 15f, 6f), 1e-4f);
        }

        [Test]
        public void TheSwingIsPushedAndFallsBackToCentre()
        {
            // A rate control, the opposite choice from the nose and deliberately: heading has no
            // resting angle to aim at, so a position stick here would wind up without limit.
            float stick = WingsuitControl.Swing(0f, 0.5f, decayPerSecond: 3f, dt: 0.02f);
            Assert.Greater(stick, 0f);

            for (int i = 0; i < 200; i++)
                stick = WingsuitControl.Swing(stick, 0f, 3f, 0.02f);

            Assert.AreEqual(0f, stick, 1e-4f, "A swing nobody is pushing must come back to centre.");

            // And it saturates rather than accumulating without bound.
            float hard = 0f;
            for (int i = 0; i < 50; i++) hard = WingsuitControl.Swing(hard, 1f, 0f, 0.02f);
            Assert.AreEqual(1f, hard, 1e-4f);

            // The mouse leans the wing, and the strafe keys add to it rather than fighting it.
            Assert.AreEqual(0.5f, WingsuitControl.Bank(0.5f, 0f), 1e-4f);
            Assert.AreEqual(1f, WingsuitControl.Bank(0.6f, 0.6f), 1e-4f, "Clamped to one stick.");
            Assert.AreEqual(0f, WingsuitControl.Bank(1f, -1f), 1e-4f, "Opposed inputs cancel.");
        }

        [Test]
        public void SteeringTurnsTheFlightWithoutMintingAnything()
        {
            // The suit turns at the player's own look sensitivity rather than at the ~80°/s a bank
            // is worth, because the mouse going dead the moment the wings came out read as the suit
            // ignoring the player. What that must NOT become is free energy.
            OrnithopterFlightState flying = OrnithopterFlightState.Launch(20f, 0f, -11f);
            OrnithopterFlightState turned = WingsuitControl.Steer(flying, 90f);

            Assert.AreEqual(0f, Mathf.DeltaAngle(turned.Heading, 90f), 1e-3f);
            Assert.AreEqual(flying.Airspeed, turned.Airspeed, 1e-4f, "Steering is a rotation.");
            Assert.AreEqual(flying.Gamma, turned.Gamma, 1e-4f, "Steering must not lift the nose.");

            Vector3 before = OrnithopterFlightModel.VelocityOf(flying);
            Vector3 after = OrnithopterFlightModel.VelocityOf(turned);
            Assert.AreEqual(before.magnitude, after.magnitude, 1e-3f);
            Assert.AreEqual(before.y, after.y, 1e-3f, "The descent is untouched: only the compass moves.");

            // And it wraps rather than winding up, because heading is a bearing.
            Assert.AreEqual(0f, Mathf.DeltaAngle(WingsuitControl.Steer(turned, 300f).Heading, 30f), 1e-3f);
        }

        [Test]
        public void ADeployOpensAlongTheLookDirectionAtTheSpeedYouHad()
        {
            // Falling, looking at the horizon. The wing opens flying at the horizon — the whole
            // point of the fix: starting on the flight path they were actually on snapped the view
            // to the ground and made the first second a recovery from a dive nobody asked for.
            OrnithopterFlightState level = WingsuitControl.Deploy(
                velocity: new Vector3(0f, -20f, 0f), lookDirection: Vector3.forward,
                speedCarry: 1f, minAirspeed: 14f);

            Assert.AreEqual(20f, level.Airspeed, 0.01f, "Speed is carried as a magnitude.");
            Assert.AreEqual(0f, level.Gamma, 0.5f, "Looking level must open the wing level.");
            Assert.AreEqual(0f, Mathf.DeltaAngle(level.Heading, 0f), 0.5f);

            // ...and looking somewhere else opens it there, heading and pitch together.
            OrnithopterFlightState diving = WingsuitControl.Deploy(
                velocity: new Vector3(0f, 0f, 25f),
                lookDirection: new Vector3(1f, -1f, 0f),
                speedCarry: 1f, minAirspeed: 14f);

            Assert.AreEqual(-45f, diving.Gamma, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(diving.Heading, 90f), 0.5f);

            // The floor supplies a flyable speed: a deploy that starts below the stall reads as the
            // suit having dropped the player.
            OrnithopterFlightState slow = WingsuitControl.Deploy(
                velocity: new Vector3(0f, -2f, 0f), lookDirection: Vector3.right,
                speedCarry: 1f, minAirspeed: 14f);

            Assert.AreEqual(14f, slow.Airspeed, 0.01f);

            // Angle of attack zero at the moment the wings open, whichever way the pilot is
            // looking — and it is also what stops the camera jumping, because the view is slaved
            // to the nose.
            Assert.AreEqual(slow.Gamma, slow.Pitch, 1e-3f);
        }

        [Test]
        public void AFlownArrivalIsFreeAndADiveIsNot()
        {
            var landing = new WingsuitLandingConfig();

            // Flown in properly: a 5:1 glide at cruise closes on flat ground at about a fifth of
            // its airspeed. That has to cost nothing, or the suit is a way of dying.
            Vector3 cruise = new Vector3(0f, -3.8f, 19.8f);
            float shallow = OrnithopterCrash.ClosingSpeed(cruise, Vector3.up);
            Assert.AreEqual(0, OrnithopterCrash.ImpactDamage(shallow, landing),
                $"A flown arrival closes at {shallow:F1} m/s and must be free.");

            // The same speed, straight into a cliff face. Closing speed is what separates them —
            // not how fast the body was going, which is identical.
            float head_on = OrnithopterCrash.ClosingSpeed(new Vector3(0f, 0f, 20.1f), Vector3.back);
            Assert.Greater(OrnithopterCrash.ImpactDamage(head_on, landing), 25);

            // Wingtip dragged along a wall: moving along the surface, not into it.
            Assert.AreEqual(0f, OrnithopterCrash.ClosingSpeed(new Vector3(0f, 0f, 30f), Vector3.left));

            // And a held vertical dive kills from full health.
            Assert.AreEqual(landing.MaxDamage,
                OrnithopterCrash.ImpactDamage(
                    OrnithopterCrash.ClosingSpeed(new Vector3(0f, -42f, 0f), Vector3.up), landing));
        }
    }
}
