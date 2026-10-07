using System;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Items
{
    /// <summary>
    /// What arriving somewhere on a booster costs — the ornithopter's closing-speed rule with the
    /// booster's thresholds.
    ///
    /// <para>
    /// Reused rather than rewritten, and for the reason the jetpack reuses it: closing speed is the
    /// one quantity that prices every way a ride can end without any of them being a special case.
    /// Flat out a metre above the sand is not a crash; the same speed straight into a rock face is.
    /// A second damage curve here would be a second answer to a question the game has already
    /// answered twice.
    /// </para>
    /// <para>
    /// The two Recovery fields the base class carries are left at zero and inert: they exist to
    /// find somewhere to stand a pilot lifted out of a wrecked aircraft, and a boosted body never
    /// left itself.
    /// </para>
    /// </summary>
    [Serializable]
    public class BoosterImpactConfig : OrnithopterCrashConfig
    {
        public BoosterImpactConfig()
        {
            // Higher than the jetpack's 9 on purpose. A booster does not take the body over — the
            // player keeps walking, falling and taking their own fall damage throughout — so this
            // rule is billed ON TOP of whatever the body already charges itself. Set at a speed
            // nothing but a real collision reaches, so scraping a boosted crate along a dune or
            // touching down off a modest hop costs nothing here.
            SafeClosingSpeed = 12f;

            // A booster launches a player about 25 m, which comes back down at around 30 m/s. A
            // rider who takes that into a cliff instead of into the sky should reach the top of the
            // curve; one who arrives at the end of a long flat slide should not.
            LethalClosingSpeed = 34f;

            // The player prefab carries 100 health.
            MaxDamage = 100;

            // Inert. See the class summary.
            GroundSearchDistance = 0f;
            SurfaceClearance = 0f;
        }
    }
}
