using System;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Gear.Wingsuit
{
    /// <summary>
    /// What arriving at a surface costs a wingsuit pilot — the ornithopter's closing-speed rule
    /// with a human's thresholds.
    ///
    /// <para>
    /// The rule is the valuable part and it is unchanged: damage comes from how fast the body was
    /// closing on the thing it hit, not from how fast it was going. That is what makes a shallow
    /// glide onto sand free, a level dive into a cliff expensive, and a wingtip scraped along a
    /// wall nothing at all — without any of the three being a special case.
    /// </para>
    /// <para>
    /// The two Recovery fields the base class carries are left at zero and are inert here. They
    /// exist to find somewhere to stand a pilot who has just been lifted out of a wrecked aircraft;
    /// a wingsuit pilot never left their own body, so there is nobody to place and nothing to step
    /// out of.
    /// </para>
    /// </summary>
    [Serializable]
    public class WingsuitLandingConfig : OrnithopterCrashConfig
    {
        public WingsuitLandingConfig()
        {
            // A well-flown arrival closes on flat ground at about a quarter of its airspeed —
            // roughly 6 m/s at the shipped glide — so this is set just above that. Flying it in
            // properly is free; dropping the last bit of the approach is not.
            SafeClosingSpeed = 9f;

            // Held nose-down the suit passes forty. Thirty is where a dive that was never going to
            // end well finishes the job, and it sits far enough above the glide that no ordinary
            // landing can reach it by accident.
            LethalClosingSpeed = 30f;

            // The player prefab carries 100 health.
            MaxDamage = 100;

            // Inert. See the class summary.
            GroundSearchDistance = 0f;
            SurfaceClearance = 0f;
        }
    }
}
