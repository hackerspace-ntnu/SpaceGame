// The six-legged desert crawler, as four choices.
//
// A crewed walking station. Everything that makes a legged machine walk lives in LeggedLocomotion;
// what makes this one a station rather than a bird is:
//
//  1. STRIDE COMES FROM THE COXA'S ARC. Its legs stick out sideways on long coxae, so the foot
//     sweeps a wide arc when the coxa turns and the chord of that arc is the stride. This is the
//     opposite choice to the ostrich's, and neither machine can use the other's: a yaw arc would
//     give the bird a three-centimetre stride, and a hip budget would ignore the coxa this machine
//     is built around.
//  2. STATIC STABILITY IS THE POINT. Three feet stay down, so the hull is supported at any moment
//     you stop the clock. That is what makes it safe to stand on.
//  3. THE DECK IS HELD LEVEL. People stand on it. The legs absorb the terrain so riders are not
//     tipped around, which is the whole reason to build a station on legs.
//  4. THE FOOT IS A PAD. Soles are set flat on the ground and left there.
//
// ─────────── what changed when it moved onto the shared base ───────────
//
// This component used to carry verbatim private copies of the ground probing and never called
// WalkerGround at all -- including a sole query that paired the centre ray's x/z with the highest
// neighbour's y, producing a "contact point" lying on no surface. Those copies are gone. It also
// inherits three fixes that were made on the ostrich and never travelled back: the foothold clamp
// is horizontal and taken against the hip's position AT TOUCHDOWN, the body is posed BEFORE the
// gait picks footholds rather than after, and there is gravity, which this machine never had.
//
// The fields below are the only ones this component adds. Everything else is on the base.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Vehicles.Crawler
{
    public class DesertCrawlerLocomotion : LeggedLocomotion
    {
        /// Feet that must stay down for the hull to be statically supported.
        private const int MinPlantedLegs = 3;

        [Header("Crawler — gait")]
        [Tooltip("Legs allowed in the air at once. 1 is a ripple wave (five feet always down); " +
                 "3 is an alternating tripod, twice as fast and still statically stable.")]
        [Range(1, 3)]
        [SerializeField] private int swingLegs = 1;

        [Header("Crawler — deck")]
        [Tooltip("How much of the ground's tilt the deck takes on.\n\n" +
                 "0 holds it rigidly level, which is what it did originally and what strands the " +
                 "downhill legs on any real slope: every hip sits at one height while the feet do not, " +
                 "so the low side has to span the ride height plus the whole drop and simply runs out " +
                 "of leg. 1 lies the deck flat on the hillside and tips the crew off. This is the " +
                 "compromise, and it is a suspension rather than a pose.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFollow = 0.6f;

        [Tooltip("Hard cap on the deck's tilt, however steep the ground under it gets. This is what " +
                 "keeps the deck walkable when the machine crosses something it should have gone " +
                 "around; the legs take whatever the cap refuses.")]
        [Range(0f, 45f)]
        [SerializeField] private float maxDeckTilt = 12f;

        protected override IStrideModel CreateStride() => new YawArcStride(YawRange);
        protected override IGaitPattern CreateGait() => new RippleGait(swingLegs, MinPlantedLegs);
        protected override IBodyMotion CreateBody() => new LevelDeckBody(slopeFollow, maxDeckTilt);
        protected override IFootStyle CreateFeet() => new FlatSole();

        /// The hull creeps while a foothold is committed, so a step does not arrive at full stretch the
        /// way a bird's does and this can sit closer to the limit than the ostrich's 0.72.
        protected override float FootholdReachFraction => 0.85f;
    }
}
