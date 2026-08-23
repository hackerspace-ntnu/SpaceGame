using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What a touchdown costs. These are pilot-facing claims, not equation checks: a wing flown onto
    /// the sand has to stay free and a nose-in has to stay fatal however the constants are retuned,
    /// because those two are the whole difference between landing and crashing.
    /// </summary>
    public class OrnithopterCrashTests
    {
        private const int PlayerHealth = 100;    // the player prefab's maxHealth

        private static OrnithopterCrashConfig Config() => new OrnithopterCrashConfig();

        /// <summary>Velocity of a craft moving north at <paramref name="speed"/> on a given flight path.</summary>
        private static Vector3 Flying(float speed, float gammaDegrees)
        {
            float g = gammaDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(g), Mathf.Cos(g)) * speed;
        }

        [Test]
        public void GlidingOntoFlatGround_CostsNothing()
        {
            // A trimmed glide: fast, but descending only a couple of degrees. This is a landing.
            Vector3 velocity = Flying(speed: 20f, gammaDegrees: -6f);
            float closing = OrnithopterCrash.ClosingSpeed(velocity, Vector3.up);

            Assert.AreEqual(0, OrnithopterCrash.ImpactDamage(closing, Config()),
                $"a {closing:0.0} m/s sink rate is a landing, not a crash");
        }

        [Test]
        public void SameSpeedIntoACliff_Hurts()
        {
            // Identical airspeed to the landing above, pointed at a wall instead of skimming sand.
            // The whole point of pricing on closing speed rather than airspeed is that these two
            // come out differently.
            Vector3 velocity = Flying(speed: 20f, gammaDegrees: 0f);
            float closing = OrnithopterCrash.ClosingSpeed(velocity, Vector3.back);

            Assert.Greater(OrnithopterCrash.ImpactDamage(closing, Config()), 0);
        }

        [Test]
        public void DivingIntoTheGround_IsFatalFromFullHealth()
        {
            // Wings tucked, nose down, held all the way in.
            Vector3 velocity = Flying(speed: 42f, gammaDegrees: -60f);
            float closing = OrnithopterCrash.ClosingSpeed(velocity, Vector3.up);

            Assert.GreaterOrEqual(OrnithopterCrash.ImpactDamage(closing, Config()), PlayerHealth);
        }

        [Test]
        public void ScrapingAlongASurface_IsNotAnImpact()
        {
            // A wingtip dragged down a rock face is moving fast, but not towards the rock. Charging
            // it as a head-on hit would make every brush against terrain lethal.
            Vector3 velocity = Flying(speed: 30f, gammaDegrees: -90f);

            Assert.AreEqual(0f, OrnithopterCrash.ClosingSpeed(velocity, Vector3.back), 1e-4f);
        }
    }
}
