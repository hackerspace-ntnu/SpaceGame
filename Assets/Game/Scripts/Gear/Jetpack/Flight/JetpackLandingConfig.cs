using System;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>
    /// What arriving at a surface costs a jetpack pilot — the ornithopter's closing-speed rule
    /// with a jetpack's thresholds.
    ///
    /// <para>
    /// The rule is reused rather than rewritten because the two ways a jetpack flight can end
    /// must measure the same quantity, or one of them is free: a landing burned down onto the
    /// sand, and falling out of the sky because the motors overheated. Closing speed is the one
    /// number that prices both without either being a special case.
    /// </para>
    /// <para>
    /// <b>This is the whole punishment for an overheat</b>, and it is why the heat rules needed
    /// no damage of their own. Cutting out at height is not a message and a cooldown; it is a
    /// fall, priced by how far it got before the pack relit.
    /// </para>
    /// <para>
    /// The two Recovery fields the base class carries are left at zero and inert. They exist to
    /// find somewhere to stand a pilot lifted out of a wrecked aircraft; a jetpack pilot never
    /// left their own body, so there is nobody to place.
    /// </para>
    /// </summary>
    [Serializable]
    public class JetpackLandingConfig : OrnithopterCrashConfig
    {
        public JetpackLandingConfig()
        {
            // Reached about 2.3 m below wherever the pilot let go of Space, because a release
            // is a real fall — so an ordinary landing is a burn feathered near the ground rather
            // than a free settle, and this is the number to move first if that reads as punishing
            // rather than as demanding. It is set at the wingsuit's figure on purpose: the same
            // body hitting the same sand should cost the same whichever machine dropped it.
            SafeClosingSpeed = 9f;

            // An overheat at height reaches this in about three seconds of free fall at 18 m/s²,
            // minus what the vertical drag takes off. Cutting out low is survivable; cutting out
            // high and failing to relight is not, which is the risk the heat gauge is warning about.
            LethalClosingSpeed = 30f;

            // The player prefab carries 100 health.
            MaxDamage = 100;

            // Inert. See the class summary.
            GroundSearchDistance = 0f;
            SurfaceClearance = 0f;
        }
    }
}
