// When each leg swings.
//
// WalkerGait.MetachronalOffset spreads legs EVENLY, which is a ripple wave and nothing else. A
// quadruped built on it can only ever ripple, and real quadruped gaits are not even spreads:
//
//   ripple / metachronal   i/n            assigned by leg order around the hull
//   trot                   {0, ½, ½, 0}   by DIAGONAL pairs
//   pace                   {0, ½, 0, ½}   by LATERAL pairs
//   bound                  {0, 0, ½, ½}   front pair, then rear pair
//
// Offsets are a continuous function of runBlend rather than a table swapped at a threshold, so a
// walk-to-trot transition blends instead of teleporting a foot that is mid-swing.
//
// Every pattern's step-early rule is gated on something, because it is the only part of the gait
// that is not on the clock and so the only thing that can pull legs into step with each other.
// Ungated, a leg that lands over-extended re-steps on the very next frame and the machine spends
// its life in the air taking hundreds of tiny steps.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// Legs lift in sequence up one side and then the other, so consecutive steps are never taken
    /// by neighbours. How a real hexapod walks when it is not in a hurry.
    public class RippleGait : IGaitPattern
    {
        private readonly int swingSlots;
        private readonly int minPlanted;
        private int[] order;
        private int legCount;

        /// `swingSlots` is how many legs may be airborne at once -- 1 is a ripple wave with five
        /// feet always down, 3 an alternating tripod, twice as fast and still statically stable.
        /// `minPlanted` is what a step-early request must leave behind.
        public RippleGait(int swingSlots, int minPlanted)
        {
            this.swingSlots = Mathf.Max(1, swingSlots);
            this.minPlanted = minPlanted;
        }

        public void Bind(IReadOnlyList<LegMeasurement> legs)
        {
            legCount = legs.Count;
            order = GaitLayout.RippleOrder(legs);
        }

        public float PhaseOffset(int leg, float runBlend)
            => order == null ? 0f : WalkerGait.MetachronalOffset(order[leg], legCount);

        public float Duty(float runBlend)
        {
            // Invariant I1, defended at the policy rather than only at the call site. A duty of
            // zero makes the derived top speed zero, which makes SetTwist clamp every command to
            // zero, which stops a clock advanced by distance travelled -- and nothing reopens a
            // slice after that. An unbound pattern reports the duty it would have on the smallest
            // machine that could carry its swing slots, never nothing.
            int count = Mathf.Max(legCount, swingSlots + 1);
            return WalkerGait.SwingDuty(swingSlots, count);
        }

        public bool MayStepEarly(in StepEarlyRequest r)
        {
            if (!r.Unreachable) return false;

            // Invariant I1. A gate that can never be satisfied is not a gate, it is a stall: on a
            // three-legged machine asking to keep three planted, every request would be refused and
            // a leg stranded out of reach would stay stranded forever.
            if (minPlanted >= r.LegCount) return true;

            return r.PlantedCount - 1 >= minPlanted;
        }
    }

    /// Two legs, opposite halves of the cycle. A biped is never statically stable, spends half its
    /// cycle on one foot and, at speed, has moments with no feet down at all -- so there is no
    /// minimum-planted rule here; there is no count of planted feet it could promise.
    public class AlternatingGait : IGaitPattern
    {
        /// A foot must have carried its share for this many swings before it may step early,
        /// however badly it is over-extended. Held to an emergency measure for genuinely broken
        /// ground: once the two legs fall into step the machine has both feet in the air half the
        /// time and is hopping rather than walking.
        private const float MinStanceFraction = 1.0f;

        private readonly float walkDuty;
        private readonly float runDuty;
        private int legCount;

        public AlternatingGait(float walkDuty, float runDuty)
        {
            this.walkDuty = walkDuty;
            this.runDuty = runDuty;
        }

        public void Bind(IReadOnlyList<LegMeasurement> legs) => legCount = legs.Count;

        public float PhaseOffset(int leg, float runBlend)
            => WalkerGait.MetachronalOffset(leg, legCount);

        /// Below 0.5 both feet are down for part of the cycle, which is what makes it a walk. Above
        /// it the swings overlap and the machine is briefly airborne with no feet down -- the
        /// flight phase that makes a run read as a run.
        public float Duty(float runBlend) => Mathf.Lerp(walkDuty, runDuty, Mathf.Clamp01(runBlend));

        public bool MayStepEarly(in StepEarlyRequest r)
            => r.Unreachable && r.StanceTime > r.SwingDuration * MinStanceFraction;
    }

    /// Diagonal pairs: front-left with rear-right, front-right with rear-left. The gait a quadruped
    /// moves to when a walk stops being fast enough.
    ///
    /// The pairs are worked out from where the feet actually are, so a rig re-authored with its
    /// legs in a different order still trots rather than doing something that merely has the right
    /// number of feet in the air.
    public class TrotGait : IGaitPattern
    {
        private readonly float walkDuty;
        private readonly float runDuty;
        private float[] ripple;
        private float[] trot;

        public TrotGait(float walkDuty, float runDuty)
        {
            this.walkDuty = walkDuty;
            this.runDuty = runDuty;
        }

        public void Bind(IReadOnlyList<LegMeasurement> legs)
        {
            int count = legs.Count;
            int[] order = GaitLayout.RippleOrder(legs);

            ripple = new float[count];
            trot = new float[count];

            for (int i = 0; i < count; i++)
                ripple[i] = WalkerGait.MetachronalOffset(order[i], count);

            GaitLayout.Centre(legs, out float midX, out float midZ);
            for (int i = 0; i < count; i++)
            {
                bool left = legs[i].HomeLocal.x < midX;
                bool front = legs[i].HomeLocal.z > midZ;

                // One diagonal takes the first half of the cycle, the other the second. Front-left
                // and rear-right agree; front-right and rear-left agree; the two disagree.
                trot[i] = left == front ? 0f : 0.5f;
            }
        }

        public float PhaseOffset(int leg, float runBlend)
        {
            if (ripple == null || leg >= ripple.Length) return 0f;
            return WalkerGait.BlendOffset(ripple[leg], trot[leg], Mathf.Clamp01(runBlend));
        }

        public float Duty(float runBlend) => Mathf.Lerp(walkDuty, runDuty, Mathf.Clamp01(runBlend));

        /// A trotting quadruped has two feet down by construction, so the step-early rule only has
        /// to keep a leg from re-stepping the instant it lands.
        public bool MayStepEarly(in StepEarlyRequest r)
            => r.Unreachable && r.StanceTime > r.SwingDuration;
    }

    /// Working out a machine's shape from where its feet are.
    internal static class GaitLayout
    {
        /// Sequence position for each leg: up one side, then the other, rear to front. Returns the
        /// leg's place in that sequence, indexed by leg.
        public static int[] RippleOrder(IReadOnlyList<LegMeasurement> legs)
        {
            int count = legs.Count;
            Centre(legs, out float midX, out _);

            var sequence = new List<int>(count);
            for (int i = 0; i < count; i++) sequence.Add(i);

            sequence.Sort((a, b) =>
            {
                int sideA = legs[a].HomeLocal.x < midX ? 0 : 1;
                int sideB = legs[b].HomeLocal.x < midX ? 0 : 1;
                if (sideA != sideB) return sideA.CompareTo(sideB);
                return legs[a].HomeLocal.z.CompareTo(legs[b].HomeLocal.z);
            });

            var order = new int[count];
            for (int i = 0; i < count; i++) order[sequence[i]] = i;
            return order;
        }

        /// Mean foothold, so left/right and front/rear are read against the machine's own centre
        /// rather than against the body origin, which on some rigs sits at a corner.
        public static void Centre(IReadOnlyList<LegMeasurement> legs, out float midX, out float midZ)
        {
            midX = 0f;
            midZ = 0f;
            if (legs.Count == 0) return;

            for (int i = 0; i < legs.Count; i++)
            {
                midX += legs[i].HomeLocal.x;
                midZ += legs[i].HomeLocal.z;
            }
            midX /= legs.Count;
            midZ /= legs.Count;
        }
    }
}
