using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// Every tuning number the flight model uses, in one place, so the physics file contains no
    /// magic constants and the whole feel of the machine can be retuned from the inspector.
    ///
    /// The defaults describe the machine as modelled: a 10 m span, roughly 14 m² of wing, and
    /// about 150 kg once a rider is slung under it. They give a stall around 11 m/s, a glide
    /// somewhere near 9:1, and enough beat to climb — check with
    /// <see cref="OrnithopterFlightModel.StallSpeed"/> rather than assuming after an edit, since
    /// mass, area and the lift curve all feed it.
    /// </summary>
    [Serializable]
    public class OrnithopterFlightConfig
    {
        [Header("Airframe")]
        [Tooltip("All-up mass in kg: airframe plus the rider slung in the cradle.")]
        [Min(1f)] public float Mass = 150f;

        [Tooltip("Wing area in m² at full spread. Scales directly with lift, so this and Mass are " +
                 "the two numbers that set the stall speed.")]
        [Min(0.1f)] public float WingArea = 14f;

        [Tooltip("Air density, kg/m³. 1.225 is sea level. Lower it for a thin-atmosphere world " +
                 "and the machine will need to fly faster to stay up.")]
        [Min(0.01f)] public float AirDensity = 1.225f;

        [Header("Lift curve")]
        [Tooltip("How much lift coefficient each degree of angle of attack buys. Thin-aerofoil " +
                 "theory gives about 0.11; a fabric wing with a blunt leading edge does less.")]
        [Min(0.001f)] public float LiftSlopePerDegree = 0.09f;

        [Tooltip("Angle of attack the wing stalls at, degrees.")]
        [Min(1f)] public float StallAngle = 15f;

        [Tooltip("How many degrees past the stall it takes for lift to finish collapsing. A wide " +
                 "band is a gentle, recoverable stall; a narrow one drops the nose sharply.")]
        [Min(0.1f)] public float StallFadeAngle = 12f;

        [Tooltip("Fraction of peak lift still made deep in the stall. Must stay above zero — a " +
                 "wing that makes NO lift never lowers its own nose, so the stall never ends.")]
        [Range(0.05f, 1f)] public float PostStallLiftFraction = 0.45f;

        [Header("Drag")]
        [Tooltip("Drag with the wing making no lift: rigging, fuselage, the rider.")]
        [Min(0f)] public float DragCoefficientZeroLift = 0.05f;

        [Tooltip("Induced drag — the price of making lift, paid as k·Cl². Raising it flattens the " +
                 "glide and punishes flying slow and nose-high.")]
        [Min(0f)] public float InducedDragFactor = 0.06f;

        [Header("Flapping")]
        [Tooltip("Peak thrust acceleration at the bottom of a full-effort downstroke, m/s². " +
                 "Averaged over a beat this delivers about half its value.")]
        [Min(0f)] public float FlapThrust = 9f;

        [Tooltip("Beats per second while gliding. Never zero: the wings should idle, not freeze.")]
        [Min(0.01f)] public float FlapHzIdle = 0.35f;

        [Tooltip("Beats per second at full effort.")]
        [Min(0.01f)] public float FlapHzMax = 1.6f;

        [Tooltip("Stamina spent per second of full-effort flapping. 1/this is how many seconds of " +
                 "hard climbing the pilot gets from full.")]
        [Min(0f)] public float StaminaDrainPerSecond = 0.16f;

        [Tooltip("Stamina regained per second while gliding.")]
        [Min(0f)] public float StaminaRecoverPerSecond = 0.22f;

        [Tooltip("Stamina below which the beat starts visibly weakening, so exhaustion arrives as " +
                 "a fade the pilot can feel rather than a cutoff.")]
        [Range(0.01f, 1f)] public float StaminaFadeBand = 0.25f;

        [Header("Tow")]
        [Tooltip("Pull a taut rope puts on the craft, m/s². Compare FlapThrust: a rope should out-pull " +
                 "the wings or there is no reason to throw one, but a tow that dwarfs them makes the " +
                 "beat pointless. The pull is resolved along the flight path, so a hook set ahead " +
                 "adds speed and one set behind takes it away.")]
        [Min(0f)] public float TowAcceleration = 14f;

        [Tooltip("Stamina spent per second of tow. This is the price that stops a rope being free " +
                 "speed: 1/this is how many seconds of tow a full pilot gets, and it comes out of " +
                 "the same reserve the climb needs afterwards. Zero makes the tow free — which " +
                 "flies, but hooking and holding then beats flying the aircraft.")]
        [Min(0f)] public float TowStaminaDrainPerSecond = 0.22f;

        [Header("Control")]
        [Tooltip("Pitch rate at full stick and full authority, deg/s.")]
        [Min(1f)] public float PitchRate = 45f;

        [Tooltip("Roll rate at full stick, deg/s.")]
        [Min(1f)] public float RollRate = 90f;

        [Tooltip("How fast the craft rolls back to wings-level when the stick is released, deg/s.")]
        [Min(0f)] public float RollCentringRate = 60f;

        [Tooltip("Largest body pitch the pilot can command, degrees either way.")]
        [Range(5f, 89f)] public float MaxPitch = 60f;

        [Tooltip("Largest bank angle, degrees either way.")]
        [Range(5f, 89f)] public float MaxRoll = 60f;

        [Tooltip("Yaw the tail fan adds directly, deg/s. Bank alone has nothing to work with at " +
                 "low speed, so without this the machine stops answering exactly when it matters.")]
        [Min(0f)] public float TailYawRate = 25f;

        [Tooltip("Airspeed at which the controls have their full authority, m/s. Below it they " +
                 "fade off in proportion, because a wing needs air moving over it to push against.")]
        [Min(0.1f)] public float FullAuthoritySpeed = 12f;

        [Tooltip("Fraction of control authority left in a stall.")]
        [Range(0f, 1f)] public float StalledAuthority = 0.3f;

        [Header("Wing folding")]
        [Tooltip("How much of the wing folds away at full tuck, 0..1. Tucking sheds lift AND drag, " +
                 "which is how a diving bird gets fast.")]
        [Range(0f, 0.95f)] public float TuckSpreadLoss = 0.65f;
    }
}
