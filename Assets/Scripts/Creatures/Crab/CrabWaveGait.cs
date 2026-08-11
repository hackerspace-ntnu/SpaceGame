// A wave that runs along the direction the machine TRAVELS, not along its nose.
//
// `RippleGait` splits the legs left/right about the body's centreline and orders each side rear to
// front — which is exactly right for a machine that walks the way it points, and exactly wrong for
// one that does not. A crab holds its carapace square to the direction of travel and scuttles across
// it, so the wave has to run along the machine's X, the axis it is actually covering ground on.
// Ordering by `HomeLocal.z` there gives a sequence that marches ACROSS the travel direction: the
// legs at one end of the machine take the whole first half of the cycle and the other end takes the
// second, so support swings fore and aft while the ground goes past sideways.
//
// So this pattern transposes the ripple. Legs are split into a FORE row and an AFT row about the
// mean foothold, each row is ordered along X, and the two rows run in antiphase.
//
//   * ordering along X          -- the wave marches with the travel, one leg handing off to its
//                                  neighbour downstream, which is the whole read of a crab walking
//   * two rows in antiphase     -- the legs airborne at any moment are diagonally opposite, so the
//                                  support polygon never collapses onto one end of a wide, low hull
//   * offsets stay exactly k/n  -- which is what makes the support count arithmetic rather than
//                                  hopeful: only `swingSlots` of the n slices overlap at once
//
// Everything is derived from `HomeLocal` in `Bind`, never from leg indices. `LeggedLocomotion` sorts
// its legs by (x, z) before measuring them, so an index-keyed table would silently mean something
// different the moment a rig was re-authored with its legs in another order -- and this pattern has
// to survive four to eight of them.
//
// ─────────── invariant I1 ───────────
//
// `Duty()` never returns 0, bound or unbound. The clock advances by DISTANCE TRAVELLED and MaxSpeed
// is derived from the duty at Initialise, so a duty of zero makes MaxSpeed zero, which makes
// `SetTwist` clamp every command to zero, which stops the clock, which reopens no slice, and nothing
// can un-stick the machine. That failure has already cost one session on the crawler.
//
// The min-planted gate has the same escape hatch for the same reason: a gate that can never be
// satisfied is not a gate, it is a stall.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Locomotion;

/// A metachronal wave along the machine's lateral axis, for a body that travels sideways.
public class CrabWaveGait : IGaitPattern
{
    private readonly int swingOverride;
    private readonly int plantedOverride;

    private float[] offsets;
    private int legCount;
    private int swingSlots = 1;
    private int minPlanted;

    /// Both counts derived from the rig by default -- the whole point of the machine is that one
    /// component covers four to eight legs, and a swing count authored for six leaves an
    /// eight-legged crab creeping and a four-legged one standing on two feet.
    ///
    /// Pass positive values to pin them; the tests use that to build a gate that cannot be met, and
    /// a prefab can use it to trade support for speed. `swingSlots` is clamped to leave at least
    /// three feet down whatever it is handed, because a statically stable machine is the point.
    public CrabWaveGait(int swingSlots = 0, int minPlanted = -1)
    {
        swingOverride = swingSlots;
        plantedOverride = minPlanted;
    }

    /// How many feet may be in the air at once, for a machine with this many legs.
    ///
    /// One leg per three is the rule, floored at one and ceilinged at three: it keeps at least two
    /// thirds of the feet down at every count, which is what makes the support polygon survive a
    /// cross-slope on a hull this wide. Four legs get a single swing (three down, a triangle);
    /// six and seven get two; eight gets two, nine would get three.
    public static int SwingSlotsFor(int legCount)
        => Mathf.Clamp(legCount / 3, 1, Mathf.Max(1, legCount - 3));

    /// Feet a step-early request must leave behind: everything the wave is not already lifting.
    /// Equal to the count the clock guarantees anyway, so the adaptive rule can never take the
    /// machine below what the arithmetic promises.
    public static int MinPlantedFor(int legCount)
        => Mathf.Max(0, legCount - SwingSlotsFor(legCount));

    /// What the pattern settled on once it had seen the rig. For diagnostics and tests; a machine
    /// that will not step is almost always one of these two numbers.
    public int SwingSlots => swingSlots;
    public int MinPlanted => minPlanted;

    public void Bind(IReadOnlyList<LegMeasurement> legs)
    {
        legCount = legs.Count;
        swingSlots = swingOverride > 0
            ? Mathf.Clamp(swingOverride, 1, Mathf.Max(1, legCount))
            : SwingSlotsFor(legCount);
        minPlanted = plantedOverride >= 0 ? plantedOverride : MinPlantedFor(legCount);

        offsets = BuildOffsets(legs, legCount);
    }

    public float PhaseOffset(int leg, float runBlend)
        => offsets == null || leg < 0 || leg >= offsets.Length ? 0f : offsets[leg];

    /// Slack left at the end of every swing, as a fraction of the slice it belongs to.
    ///
    /// A swing sized to fill its slice exactly OVERLAPS THE NEXT ONE, and it does so for a reason
    /// nothing in the arithmetic shows: the clock is sampled once a frame. A leg lifts on the first
    /// frame its slice is open, so up to a frame late, and lands on the first frame its swing timer
    /// has run out, so up to a frame late again -- and the leg behind it has already gone. Measured
    /// on the synthetic crab that cost one extra foot in the air at every leg count: four legs held
    /// two down where the clock promised three, and a four-legged machine on two feet is not
    /// statically stable at all.
    ///
    /// Ten percent buys about five frames of gap at a walk and two and a half at top speed, and
    /// costs 13% of the top speed. That is the right trade for a machine whose whole claim is that
    /// it is always standing on something.
    private const float SliceMargin = 0.9f;

    /// Constant across the speed range. A crab has no run: it is statically stable at every pace and
    /// the thing that changes with speed is how fast the clock turns, not how many feet are up.
    ///
    /// Never zero -- see the invariant note at the top of the file. An unbound pattern reports the
    /// duty it would have on the smallest machine that could carry its swing slots.
    public float Duty(float runBlend)
        => WalkerGait.SwingDuty(swingSlots, Mathf.Max(legCount, swingSlots + 1)) * SliceMargin;

    public bool MayStepEarly(in StepEarlyRequest r)
    {
        if (!r.Unreachable) return false;

        // A gate that can never be met is a stall, not a gate: on a machine asked to keep more feet
        // down than it has, every request would be refused and a leg stranded out of reach would
        // stay stranded for good. Degrade to "never block" rather than to "never move" (I1).
        if (minPlanted >= r.LegCount) return true;

        return r.PlantedCount - 1 >= minPlanted;
    }

    /// Each leg's slice of the cycle, worked out from where its foot rests.
    ///
    /// Two rows about the mean foothold, each ordered along X, interleaved so consecutive slices
    /// alternate rows -- and the aft row entered half a row along, so the leg that lifts alongside a
    /// fore-row leg is the aft one FURTHEST from it rather than the one directly behind. On a hull
    /// that is wide across its travel that is the difference between a support triangle spanning the
    /// machine and one collapsed onto a corner.
    ///
    /// The result is a permutation of {0, 1/n, ..., (n-1)/n}, which is what keeps the support count
    /// exact: with a duty of s/n, exactly s slices overlap at any phase.
    private static float[] BuildOffsets(IReadOnlyList<LegMeasurement> legs, int count)
    {
        var result = new float[count];
        if (count == 0) return result;

        float midZ = 0f;
        for (int i = 0; i < count; i++) midZ += legs[i].HomeLocal.z;
        midZ /= count;

        var fore = new List<int>(count);
        var aft = new List<int>(count);
        for (int i = 0; i < count; i++) (legs[i].HomeLocal.z > midZ ? fore : aft).Add(i);

        // Along the travel axis, both rows the same way, so the two waves march together rather
        // than toward each other.
        fore.Sort((a, b) => legs[a].HomeLocal.x.CompareTo(legs[b].HomeLocal.x));
        aft.Sort((a, b) => legs[a].HomeLocal.x.CompareTo(legs[b].HomeLocal.x));

        // Half a row's head start on the aft wave. With one row empty -- every foot at the same
        // depth, which is a rig that has no fore and aft -- this is a plain metachronal wave along
        // X and still correct.
        int skew = aft.Count / 2;
        int f = 0, a = 0, seq = 0;
        bool wantFore = true;

        while (seq < count)
        {
            // When the row it is the other one's turn for has run out, the remaining row takes the
            // slice. Both rows together hold every leg, so one of the two branches can always run
            // and the loop always advances.
            bool useFore = wantFore ? f < fore.Count : a >= aft.Count;
            if (useFore) result[fore[f++]] = seq++ / (float)count;
            else result[aft[(a++ + skew) % aft.Count]] = seq++ / (float)count;
            wantFore = !wantFore;
        }

        return result;
    }
}
