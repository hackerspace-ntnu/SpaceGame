using System;
using UnityEngine;

namespace SpaceGame.Gear.JumpingRod
{
    /// <summary>
    /// Everything about how the jumping rod bounces, in one serialized block so the whole feel can
    /// be retuned from the Inspector without touching code.
    ///
    /// <para>
    /// Split from the item for the same reason <c>OrnithopterFlightConfig</c> is split from the
    /// component that reads it: the arithmetic in <see cref="JumpingRodHopModel"/> is pure and
    /// testable, and it can only stay that way if the numbers it reads travel as a value rather
    /// than as fields on a MonoBehaviour.
    /// </para>
    /// <para>
    /// Every speed below is metres per second at this project's <b>-18</b> gravity, where hop
    /// height is <c>v² / 36</c>. The defaults are chosen from the heights they produce, not the
    /// other way round.
    /// </para>
    /// </summary>
    [Serializable]
    public class JumpingRodConfig
    {
        [Header("Hop")]
        [Tooltip("Take-off speed a hop can never fall below, m/s. This is the CRUISE hop — what " +
                 "the rod gives you for standing on it and doing nothing — so it is the number to " +
                 "change if the rod does not feel powerful enough. 11 m/s is about 3.4 m of air, " +
                 "roughly two and a half times an ordinary jump.")]
        [SerializeField, Min(0f)] private float minHopSpeed = 11f;

        [Tooltip("Take-off speed a hop can never exceed, m/s. Bouncing off a cliff would otherwise " +
                 "compound without limit and throw the player out of the streamed world. 16 m/s is " +
                 "about 7 m.")]
        [SerializeField, Min(0f)] private float maxHopSpeed = 16f;

        [Tooltip("Fraction of the arrival speed the spring gives back. Below 1, so a fall bigger " +
                 "than the cruise hop is handed back a little smaller each time and settles to the " +
                 "cruise height instead of ringing forever. It can never damp BELOW the cruise " +
                 "hop — that is what the floor above is for.")]
        [SerializeField, Range(0.5f, 1f)] private float energyReturn = 0.9f;

        [Header("Contact")]
        [Tooltip("How close the rod's tip has to come to the ground to bounce, metres. Measured " +
                 "from the player's feet, so it is also how far the tip hangs below them.")]
        [SerializeField, Min(0.01f)] private float contactHeight = 0.12f;

        [Tooltip("Clearance over which the spring visibly squashes, metres. Purely cosmetic: the " +
                 "coil is compressed in proportion to how close the player is to the ground, which " +
                 "every machine can work out for itself from a pose it already has.")]
        [SerializeField, Min(0.05f)] private float compressHeight = 0.5f;

        [Tooltip("Seconds after a bounce during which another one cannot fire. Guards the one " +
                 "case the descending test does not: a bounce that leaves the player still inside " +
                 "the contact band on the next physics step, which would spend the hop before it " +
                 "started and read as the rod sticking to the floor.")]
        [SerializeField, Min(0f)] private float rebounceLockout = 0.15f;

        public float MinHopSpeed => minHopSpeed;
        public float MaxHopSpeed => maxHopSpeed;
        public float EnergyReturn => energyReturn;
        public float ContactHeight => contactHeight;
        public float CompressHeight => compressHeight;
        public float RebounceLockout => rebounceLockout;
    }
}
