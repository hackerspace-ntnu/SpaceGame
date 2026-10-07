using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Ride height and sand drag. The craft's deck flies 13 m up at speed and rests on the sand
    /// when stopped, and that transition is both the foiling behaviour and the way aboard, so it
    /// is worth pinning down.
    /// </summary>
    public class FoilPhysicsTests
    {
        private const float Takeoff = 8f;
        private const float MaxHeight = 13.18f;

        [Test]
        public void AtRest_TheHullSitsOnTheSand()
        {
            Assert.AreEqual(0f, FoilPhysics.RideHeight(0f, Takeoff, MaxHeight), 1e-4f,
                "A stopped craft must be boardable, not hovering.");
        }

        [Test]
        public void BelowTakeoffSpeed_ItStaysDown()
        {
            Assert.AreEqual(0f, FoilPhysics.RideHeight(Takeoff * 0.99f, Takeoff, MaxHeight), 1e-4f);
        }

        [Test]
        public void RideHeight_RisesMonotonicallyWithSpeed()
        {
            float previous = -1f;
            for (float v = 0f; v <= 60f; v += 0.5f)
            {
                float h = FoilPhysics.RideHeight(v, Takeoff, MaxHeight);
                Assert.GreaterOrEqual(h, previous - 1e-5f, $"Ride height fell at {v} m/s.");
                previous = h;
            }
        }

        [Test]
        public void RideHeight_NeverExceedsTheStrut()
        {
            // Past this the foil would be out of the sand making no lift, so it is a real ceiling.
            Assert.LessOrEqual(FoilPhysics.RideHeight(500f, Takeoff, MaxHeight), MaxHeight + 1e-4f);
        }

        [Test]
        public void RideHeight_ApproachesTheStrutAtHighSpeed()
        {
            Assert.Greater(FoilPhysics.RideHeight(40f, Takeoff, MaxHeight), MaxHeight * 0.8f,
                "Well above take-off the craft should be flying near the top of its strut.");
        }

        [Test]
        public void FoilingCostsFarLessDragThanPloughing()
        {
            float ploughing = FoilPhysics.SandDrag(15f, 0f, hullDrag: 0.05f, foilDrag: 0.004f);
            float flying = FoilPhysics.SandDrag(15f, 1f, hullDrag: 0.05f, foilDrag: 0.004f);

            Assert.Greater(ploughing, flying * 5f,
                "Getting up onto the foil must be a real and rewarding transition.");
        }

        [Test]
        public void SandDrag_GrowsWithSpeed()
        {
            float slow = FoilPhysics.SandDrag(5f, 0.5f, 0.05f, 0.004f);
            float fast = FoilPhysics.SandDrag(20f, 0.5f, 0.05f, 0.004f);
            Assert.Greater(fast, slow * 8f, "Drag goes as speed squared.");
        }

        [Test]
        public void ADeeplyBuriedFoil_GripsHarderThanAFlyingOne()
        {
            float submerged = FoilPhysics.LateralGrip(0f, gripSubmerged: 0.98f, gripFlying: 0.55f);
            float flying = FoilPhysics.LateralGrip(1f, gripSubmerged: 0.98f, gripFlying: 0.55f);

            Assert.Greater(submerged, flying,
                "Resisting leeway is what lets the craft sail upwind; a buried foil must grip more.");
        }

        // --- climbing ---------------------------------------------------------

        private const float StallGrade = 0.30f;
        private const float ClimbCost = 14f;

        [Test]
        public void OnTheFlat_ClimbingCostsNothing()
        {
            Assert.AreEqual(0f, FoilPhysics.SlopeDeceleration(0f, ClimbCost, StallGrade), 1e-4f,
                "A level pan must sail exactly as it did before slope was modelled.");
        }

        [Test]
        public void Downhill_CostsNothingEither()
        {
            // Deliberately asymmetric: the craft is slowed by dunes, never launched down them.
            Assert.AreEqual(0f, FoilPhysics.SlopeDeceleration(-0.5f, ClimbCost, StallGrade), 1e-4f,
                "Running down a dune face must not hand the player free speed.");
        }

        [Test]
        public void ClimbCost_GrowsWithTheGrade()
        {
            float gentle = FoilPhysics.SlopeDeceleration(0.05f, ClimbCost, StallGrade);
            float steeper = FoilPhysics.SlopeDeceleration(0.15f, ClimbCost, StallGrade);

            Assert.Greater(gentle, 0f, "Any climb at all should be felt.");
            Assert.Greater(steeper, gentle, "A steeper dune must cost more.");
        }

        [Test]
        public void PastTheStallGrade_TheCostIsOverwhelming()
        {
            // The craft makes about 1.4 m/s^2 flat out, so anything above the stall grade has to
            // dwarf that or a player can simply power up a cliff.
            float atStall = FoilPhysics.SlopeDeceleration(StallGrade, ClimbCost, StallGrade);
            Assert.Greater(atStall, 10f, "At the stall grade the craft must be losing the fight.");

            float beyond = FoilPhysics.SlopeDeceleration(StallGrade * 2f, ClimbCost, StallGrade);
            Assert.Greater(beyond, atStall * 2f, "Past it, it must stop rather than crawl.");
        }

        [Test]
        public void SlopeDeceleration_IsContinuousAcrossTheStallGrade()
        {
            // A step here reads as hitting an invisible wall, which is what "refuse to climb"
            // would have been. The brief is bleed-and-stop, so the curve stays smooth.
            float below = FoilPhysics.SlopeDeceleration(StallGrade - 0.001f, ClimbCost, StallGrade);
            float above = FoilPhysics.SlopeDeceleration(StallGrade + 0.001f, ClimbCost, StallGrade);

            Assert.AreEqual(below, above, 0.5f, "The stall grade must not be a cliff edge.");
        }

        [Test]
        public void ClimbGrade_IsRiseOverRun()
        {
            Assert.AreEqual(0.5f, FoilPhysics.ClimbGrade(rise: 4f, run: 8f), 1e-4f);
            Assert.AreEqual(0f, FoilPhysics.ClimbGrade(rise: 4f, run: 0f), 1e-4f,
                "A zero lookahead must not divide by zero.");
        }

        [Test]
        public void ClimbGrade_IsClampedToSomethingSane()
        {
            // A probe that lands on a wall, or on the roof of a ruin, reports an enormous rise over
            // a short run. Unclamped that is an instant full stop from a metre of scenery.
            Assert.LessOrEqual(FoilPhysics.ClimbGrade(rise: 400f, run: 6f), 4f);
        }

        // --- coming to a stop -------------------------------------------------

        [Test]
        public void RollingResistance_IsHeavyOnTheHullAndLightOnTheFoil()
        {
            float ploughing = FoilPhysics.RollingResistance(0f, 0.9f, 0.12f);
            float flying = FoilPhysics.RollingResistance(1f, 0.9f, 0.12f);

            Assert.Greater(ploughing, flying * 4f, "A foiler must coast; a hull in sand must not.");
        }

        [Test]
        public void AnUndrivenCraft_ActuallyStops()
        {
            // The claim that matters, integrated rather than asserted about a coefficient: with
            // every sail struck, the craft has to reach a genuine standstill in a handful of
            // seconds. Quadratic drag alone never does — it approaches zero and stays there — and
            // a hull creeping across the pan at a few centimetres a second reads as broken.
            const float dt = 0.02f;
            float v = 20f;
            float t = 0f;

            while (t < 60f)
            {
                float decel = FoilPhysics.SandDrag(v, 0f, 0.05f, 0.004f)
                            + FoilPhysics.RollingResistance(0f, 0.9f, 0.12f);
                v = Mathf.Max(0f, v - decel * dt);
                if (v < FoilPhysics.StopThreshold) break;
                t += dt;
            }

            Assert.Less(v, FoilPhysics.StopThreshold,
                $"Still making {v:F2} m/s after {t:F0} s with no drive at all.");
            Assert.Less(t, 30f, $"Took {t:F0} s to stop from 20 m/s; that is a craft adrift.");
        }

        // --- steering ---------------------------------------------------------

        private const float Wheelbase = 17f;
        private const float LateralGripLimit = 8.5f;
        private const float MaxYaw = 26f;

        [Test]
        public void AStoppedCraft_HasNoSteerage()
        {
            Assert.AreEqual(0f, FoilPhysics.SteeredYawRate(0f, 27f, Wheelbase), 1e-4f,
                "A blade needs flow over it to bite. Full lock at a standstill turns nothing.");
        }

        [Test]
        public void TurnRate_GrowsWithSpeedAndWithLock()
        {
            float slow = FoilPhysics.SteeredYawRate(5f, 27f, Wheelbase);
            float fast = FoilPhysics.SteeredYawRate(15f, 27f, Wheelbase);
            float gentle = FoilPhysics.SteeredYawRate(15f, 9f, Wheelbase);

            Assert.Greater(fast, slow, "More way means the blade bites harder.");
            Assert.Greater(fast, gentle, "More lock means a tighter circle.");
        }

        [Test]
        public void TheTurningCircle_IsSetByTheWheel_NotBySpeed()
        {
            // What makes a steered vehicle feel like one: pick a lock, get a radius, and the
            // radius is the same whether you are crawling or flying. Speed changes how fast you
            // go round it, not how big it is.
            float radiusAt8 = TurnRadius(8f, 20f);
            float radiusAt16 = TurnRadius(16f, 20f);

            Assert.AreEqual(radiusAt8, radiusAt16, radiusAt8 * 0.02f,
                $"Radius went from {radiusAt8:F1} m to {radiusAt16:F1} m with speed alone.");
        }

        private static float TurnRadius(float speed, float steer)
        {
            float rate = FoilPhysics.SteeredYawRate(speed, steer, Wheelbase) * Mathf.Deg2Rad;
            return speed / rate;
        }

        [Test]
        public void AtSpeed_TheFoilWashesOutAndTheCraftUndersteers()
        {
            // The blade can only hold so much sideways load, so the same handful of wheel that
            // carves a circle at walking pace opens into a long arc at forty metres a second.
            float asked = FoilPhysics.SteeredYawRate(35f, 27f, Wheelbase);
            float held = FoilPhysics.SteadyYawRate(35f, 27f, Wheelbase, LateralGripLimit, MaxYaw);

            Assert.Less(held, asked, "Full lock at 35 m/s must exceed what the foil can hold.");

            float lateral = held * Mathf.Deg2Rad * 35f;
            Assert.LessOrEqual(lateral, LateralGripLimit + 1e-3f,
                $"The turn pulls {lateral:F1} m/s² sideways against a {LateralGripLimit} limit.");
        }

        [Test]
        public void TurnRate_NeverExceedsTheHullsCeiling()
        {
            for (float v = 0f; v <= 60f; v += 1f)
            {
                float rate = FoilPhysics.SteadyYawRate(v, 27f, Wheelbase, LateralGripLimit, MaxYaw);
                Assert.LessOrEqual(Mathf.Abs(rate), MaxYaw + 1e-3f,
                    $"A seventeen-metre hull spun at {rate:F0} deg/s doing {v:F0} m/s.");
            }
        }

        [Test]
        public void TheHelmIsSymmetric()
        {
            float toStarboard = FoilPhysics.SteadyYawRate(12f, 20f, Wheelbase, LateralGripLimit, MaxYaw);
            float toPort = FoilPhysics.SteadyYawRate(12f, -20f, Wheelbase, LateralGripLimit, MaxYaw);

            Assert.Greater(toStarboard, 0f, "Starboard lock turns to starboard.");
            Assert.AreEqual(-toStarboard, toPort, 1e-3f, "Both tacks must steer the same.");
        }

        [Test]
        public void MakingSternway_ReversesTheHelm()
        {
            // The same as backing anything steered from behind its centre of resistance, and it
            // falls out of the geometry rather than being special-cased.
            float ahead = FoilPhysics.SteeredYawRate(6f, 20f, Wheelbase);
            float astern = FoilPhysics.SteeredYawRate(-6f, 20f, Wheelbase);

            Assert.Less(ahead * astern, 0f, "Backing must turn the craft the other way.");
        }

        [Test]
        public void FlyingHigh_LeavesTheCraftSteerable()
        {
            // Geometrically the strut has almost nothing left in the sand at full ride height. A
            // craft that becomes unsteerable exactly when it is going fastest is a way to lose it,
            // so the authority is floored well above the truth.
            float onTheSand = FoilPhysics.SteeringAuthority(0f, 0.7f);
            float flying = FoilPhysics.SteeringAuthority(1f, 0.7f);

            Assert.AreEqual(1f, onTheSand, 1e-4f);
            Assert.Less(flying, onTheSand, "Less blade in the sand is less steering.");
            Assert.Greater(flying, 0.5f, "But never so little that the craft cannot be pointed.");
        }
    }
}
