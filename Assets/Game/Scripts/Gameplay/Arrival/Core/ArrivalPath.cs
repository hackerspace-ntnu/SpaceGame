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

        [Tooltip("Metres above the impact point that the descent begins. Kept inside the band where " +
                 "the desert skybox and volumetric clouds still read correctly — this is a " +
                 "high-atmosphere entry, not true orbit.")]
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
                 "what makes it aim at its landing point — and this only stops a very steep late " +
                 "descent from looking absurd.")]
        public float MaxPitchDegrees;

        [Tooltip("The last fraction of the descent spent pulling the nose back up to level. " +
                 "Without it the hull arrives pointing 55 degrees into the ground and the wreck is " +
                 "left standing on its nose, permanently, because the wreck is saved wherever the " +
                 "trajectory ends.")]
        public float FlareFraction;

        /// <summary>
        /// The values the arrival ships with. Used by <see cref="ArrivalDirector"/>'s serialized
        /// default so a freshly added component is already flyable, and by the tests as a realistic
        /// starting point rather than a hand-made one that drifts from what the game uses.
        /// </summary>
        public static ArrivalPath Default => new()
        {
            ImpactPosition = Vector3.zero,
            StartAltitude = 2200f,
            LateralBudget = 900f,
            StartBearing = 35f,
            SweepDegrees = 110f,
            MaxBankDegrees = 22f,
            MaxPitchDegrees = 70f,
            FlareFraction = 0.18f,
        };
    }
}
