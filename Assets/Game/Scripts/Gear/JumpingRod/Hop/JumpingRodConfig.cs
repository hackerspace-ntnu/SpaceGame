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

        [Header("Landing boost")]
        //
        // Press Jump as the rod meets the ground and the hop is multiplied; chain those presses and
        // the multiplier compounds. The window is deliberately asymmetric — see the two fields
        // below — and a landing that is not hit on the beat drops the chain, so the boost is a
        // rhythm the player is playing rather than a bonus they are accumulating.

        [Tooltip("How long BEFORE the rod touches down a Jump press still counts, seconds. This " +
                 "is a buffer, not an assist: human timing scatters by tens of milliseconds and a " +
                 "press the player perceives as on the beat must not be thrown away for landing a " +
                 "frame or two early (GDC-L1-FEEL-0003). Wider than the late half because pressing " +
                 "early is the commoner error — the player is anticipating a landing they can see " +
                 "coming.")]
        [SerializeField, Min(0f)] private float boostWindowEarly = 0.12f;

        [Tooltip("How long AFTER touchdown a Jump press still counts, seconds. Narrower than the " +
                 "early half: this half is paid out by topping the hop up in mid-air, so every " +
                 "millisecond of it is a visible surge after the launch. Keep it under " +
                 "Rebounce Lockout.")]
        [SerializeField, Min(0f)] private float boostWindowLate = 0.08f;

        [Tooltip("Take-off speed multiplier per link of the chain, compounding. Height goes as the " +
                 "SQUARE of this: 1.12 is 12% more speed and 25% more height per link.")]
        [SerializeField, Range(1f, 1.5f)] private float boostPerLink = 1.12f;

        [Tooltip("Longest chain, in links. The cap on the whole mechanic: at 1.12 per link, five " +
                 "links is 1.76x speed and 3.1x height — the 3.4 m cruise hop becomes 10.4 m. It " +
                 "multiplies the take-off CLAMP, floor and ceiling both, so the chain is what " +
                 "makes heights above Max Hop Speed reachable at all.")]
        [SerializeField, Min(0)] private int maxChainLinks = 5;

        [Tooltip("Links lost when a landing is missed — no press, a mistimed one, or two presses " +
                 "in one hop. At or above Max Chain Links this is a full reset, which is what ships: " +
                 "the top of the ladder should be a run the player is currently making, not a total " +
                 "they banked ten hops ago. Set it to 1 for a gentler decay.")]
        [SerializeField, Min(1)] private int linksLostOnMiss = 5;

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

        public float BoostWindowEarly => boostWindowEarly;
        public float BoostWindowLate => boostWindowLate;
        public float BoostPerLink => boostPerLink;
        public int MaxChainLinks => maxChainLinks;
        public int LinksLostOnMiss => linksLostOnMiss;
    }
}
