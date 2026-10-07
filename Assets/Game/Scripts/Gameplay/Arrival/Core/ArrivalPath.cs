using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// One crash descent, as numbers.
    ///
    /// <para>
    /// A serializable struct rather than fields spread across <see cref="ArrivalDirector"/> so the
    /// trajectory can be evaluated — and tested — with no Unity object in existence. Everything
    /// here is authored in the Inspector except <see cref="ImpactPosition"/>, which is measured off
    /// the world at runtime.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ArrivalPath
    {
        [Tooltip("Where the hull ends up. Resolved at runtime from the world's spawn anchor, not authored.")]
        public Vector3 ImpactPosition;

        [Tooltip("How far above the impact point the descent stops, so the nose reaches the ground " +
                 "and the rest of the hull does not. Measured at runtime off the hull itself, not " +
                 "authored: it is the difference between how far the pitched hull hangs below its " +
                 "own origin and how far the resting one does. The settle takes it back out.")]
        public float TouchdownLift;

        [Tooltip("Metres above the impact point that the descent begins. Kept inside the band where " +
                 "the volumetric clouds still read correctly (the skybox itself fades to an " +
                 "airborne look with camera altitude) — this is a high-atmosphere entry, not " +
                 "true orbit.")]
        public float StartAltitude;

        [Tooltip("How far from the impact point, horizontally, the descent begins. This is a " +
                 "STREAMING budget as much as a staging one: chunks are 500 m and pin under tracked " +
                 "entities, so a descent that crossed the map would drag the streamer through a " +
                 "dozen chunks at speed. A few hundred metres keeps it to two or three.")]
        public float LateralBudget;

        [Tooltip("Compass bearing, in degrees, of the point the descent starts from, measured around " +
                 "the impact point.")]
        public float StartBearing;

        [Tooltip("How far around the impact point the ship swings on the way down. This is what " +
                 "makes the path an arc rather than a straight line — the 'orbit' in the brief. " +
                 "Zero would be a dead-straight dive.")]
        public float SweepDegrees;

        [Tooltip("Peak bank angle, reached at the top of the descent and unwound to zero by impact " +
                 "so the wreck does not land on its side.")]
        public float MaxBankDegrees;

        [Tooltip("Ceiling on how far the nose may drop. The dive angle itself is MEASURED from the " +
                 "trajectory — the hull points along the way it is actually travelling, which is " +
                 "what makes it aim at its landing point. The descent is committed, so the last " +
                 "third of it sits ON this cap and this is therefore the attitude the ship HITS " +
                 "the ground in: lower it for a hull that ploughs in, raise it for one that spears " +
                 "in nose-first.")]
        public float MaxPitchDegrees;

        [Tooltip("Peak amplitude of the crash tumble, in degrees of ROLL. This is the hull yawing " +
                 "and rolling like something that has lost control, on top of the arc it is still " +
                 "on. Dosed, not free: the crew are sitting inside it and the camera is their " +
                 "head, so violent roll reads as a broken camera rather than as drama " +
                 "(GDC-L1-FEEL-0006). Hard-capped at ArrivalTrajectory.MaxTumbleDegrees whatever " +
                 "is authored here. Zero flies the old dead-steady arc.")]
        public float TumbleDegrees;

        [Tooltip("How fractionally the tumble carries into YAW as well as roll. Kept below one: " +
                 "the hull is aiming at its landing point, and a nose swinging as far sideways as " +
                 "it rolls reads as bad steering rather than as a ship in trouble.")]
        [Range(0f, 1f)] public float TumbleYawShare;

        [Tooltip("How many roll oscillations the tumble makes over the WHOLE descent. Counted in " +
                 "cycles rather than in hertz so retiming the descent stretches the tumble with " +
                 "it instead of cramming the same wobble into less time.")]
        public float TumbleCycles;

        [Tooltip("How late the tumble builds. The envelope is t-to-this-power times one-minus-t " +
                 "squared, so it peaks at buildUp/(buildUp+2) of the way down: 0 starts at full " +
                 "and only decays, 3 peaks at six tenths, higher keeps the hull steady longer and " +
                 "then loses it. It reaches exactly zero — with zero rate of change — at contact " +
                 "whatever this is, which is what the settle depends on.")]
        public float TumbleBuildUp;

        /// <summary>
        /// The values the arrival ships with. Used by <see cref="ArrivalDirector"/>'s serialized
        /// default so a freshly added component is already flyable, and by the tests as a realistic
        /// starting point rather than a hand-made one that drifts from what the game uses.
        /// </summary>
        public static ArrivalPath Default => new()
        {
            ImpactPosition = Vector3.zero,
            TouchdownLift = 0f,
            StartAltitude = 2200f,
            LateralBudget = 900f,
            StartBearing = 35f,
            SweepDegrees = 110f,
            MaxBankDegrees = 22f,
            MaxPitchDegrees = 70f,
            TumbleDegrees = 18f,
            TumbleYawShare = 0.4f,
            TumbleCycles = 2.5f,
            TumbleBuildUp = 3f,
        };
    }
}
