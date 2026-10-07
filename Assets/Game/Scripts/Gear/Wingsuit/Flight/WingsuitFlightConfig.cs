using System;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Gear.Wingsuit
{
    /// <summary>
    /// The ornithopter's flight model, tuned for a human wearing a membrane instead of a 10 m
    /// airframe. Same equations, same stall, same energy trade — different numbers.
    ///
    /// <para>
    /// <b>Thrust is zero and stays zero.</b> That is the whole difference between this and the
    /// aircraft: a wingsuit has no way to put energy into itself, so every metre of height comes
    /// from a metre of height already had. It is enforced twice — here, and in
    /// <see cref="WingsuitControl"/>, which never asks for a positive flap — because the two
    /// failures are different: a non-zero number here would let the suit climb, and a positive
    /// input there would let it climb even with this at zero.
    /// </para>
    /// <para>
    /// A real wingsuit glides about 2.5:1 at around 180 km/h and would be miserable to fly. These
    /// numbers are tuned for the sensation instead (GDC-L1-FEEL-0007): a long, committed ~5.3:1
    /// glide that stalls if you hold the nose up. Read the two derived numbers back with
    /// <see cref="OrnithopterFlightModel.StallSpeed"/> and <see cref="BestGlideRatio"/> after any
    /// edit rather than assuming — mass, area and the whole lift curve feed both.
    /// </para>
    /// <para>
    /// A subclass with a constructor rather than a factory because Unity serializes this by its
    /// declared field type and runs the parameterless constructor when it first builds one: the
    /// values below become the prefab's authored defaults, and are then tunable per prefab in the
    /// Inspector like any other serialized field.
    /// </para>
    /// </summary>
    [Serializable]
    public class WingsuitFlightConfig : OrnithopterFlightConfig
    {
        public WingsuitFlightConfig()
        {
            // Airframe: an astronaut, their suit and what they are carrying, under the membrane
            // between arm and hip. The area is generous for the span it is drawn on — it is the
            // cheapest lever on wing loading, and wing loading is what sets both the stall and how
            // fast a hands-off glide sinks.
            Mass = 110f;
            WingArea = 5.5f;

            // A fabric wing with a blunt leading edge and a body in the middle of it: less lift
            // per degree than the aircraft's, but it hangs on to a higher angle before letting go.
            LiftSlopePerDegree = 0.075f;
            StallAngle = 18f;
            StallFadeAngle = 14f;
            PostStallLiftFraction = 0.45f;

            // Together these ARE the glide ratio: 1/(2·sqrt(cd0·k)), here about 5.3:1. A human is
            // draggy in a way an airframe is not and an honest suit is draggier still, but a suit
            // tuned honestly sank at 6 m/s and spent every flight arriving — the height a player
            // had was gone before they could aim it anywhere (GDC-L1-FEEL-0007).
            DragCoefficientZeroLift = 0.075f;
            InducedDragFactor = 0.12f;

            // No flapping, ever. The beat frequencies still tick because FlapPhase drives the
            // membrane's idle breath, but with no thrust behind it that is cosmetic.
            FlapThrust = 0f;

            // Nothing spends stamina: there is no beat to spend it on and the suit refuses to
            // deploy on a rope, so the tow term never runs either.
            StaminaDrainPerSecond = 0f;
            TowAcceleration = 0f;
            TowStaminaDrainPerSecond = 0f;

            // A body answers far quicker than a 10 m span, and these are deliberately well past
            // what an aircraft would use. The first tuning pass took the ornithopter's rates as a
            // starting point and the result read as input lag rather than as weight: a wingsuit is
            // a person moving their own arms, and the nose has to arrive about as fast as the
            // player can turn their head or the crosshair stops meaning anything.
            PitchRate = 150f;
            RollRate = 220f;
            RollCentringRate = 150f;
            MaxPitch = 70f;
            MaxRoll = 70f;

            // No aerodynamic rudder. The mouse yaws the flight directly, at the player's own look
            // sensitivity and outside these equations (WingsuitControl.Steer), so a tail fan would
            // be a second, weaker answer to the same input — and this suit has no tail.
            TailYawRate = 0f;

            // Low, because a deploy starts near the stall and controls that fade out exactly when
            // the player is first taking hold of them read as a suit that ignores you.
            FullAuthoritySpeed = 10f;
            StalledAuthority = 0.45f;

            // Arms in. Sheds lift and drag together, which is how the dive gets fast.
            TuckSpreadLoss = 0.6f;
        }

        /// <summary>
        /// The best glide ratio this configuration can achieve — how many metres forward per metre
        /// down, flown at the angle of attack that gets it.
        ///
        /// Derived rather than tuned, for the same reason <see cref="OrnithopterFlightModel.StallSpeed"/>
        /// is: it is the number the feel is actually described in ("about a five to one glide"),
        /// and a separately tuned copy of it would drift out of agreement with the drag curve that
        /// produces it. Standard result for a parabolic drag polar: L/D peaks at 1/(2·sqrt(cd0·k)).
        /// </summary>
        public static float BestGlideRatio(OrnithopterFlightConfig cfg)
        {
            if (cfg == null) return 0f;

            float product = cfg.DragCoefficientZeroLift * cfg.InducedDragFactor;
            if (product <= 0f) return 0f;

            return 1f / (2f * UnityEngine.Mathf.Sqrt(product));
        }
    }
}
