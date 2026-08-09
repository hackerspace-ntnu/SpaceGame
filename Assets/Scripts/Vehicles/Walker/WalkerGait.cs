// When each leg steps, and where it puts its foot.
//
// The gait is a clock, not a set of rules. Each leg owns a fixed slice of the cycle and swings
// during that slice and at no other time. There is nothing to negotiate between legs, so there is
// no deadlock to detect and no escape hatch to trip: support is guaranteed by arithmetic, since
// only `SwingSlots` of the `LegCount` slices overlap at once.
//
// The clock is advanced by DISTANCE TRAVELLED, not by time. Three things fall out of that, and
// together they are most of what makes the machine read as a machine:
//
//   * A stationary walker does not step. The phase stops when the hull stops.
//   * A slow walker takes slow steps, because covering a stride takes longer.
//   * Top speed is derived rather than chosen. A foot must return to its start within its
//     stance, so the fastest honest speed is stride / stance-duration -- ask for more and the
//     feet would have to slide. `MaxSpeed` is that number, and the caller clamps to it.
//
// Turning needs no special case. A leg's foot drifts backwards through the body frame at
// -(v + w x r), which already accounts for the hull's rotation, so a leg on the outside of a turn
// is handed a longer stride than one on the inside without anything here knowing about turns.
using UnityEngine;

namespace SpaceGame.Walker
{
    /// The gait clock. A struct with one field, because that is genuinely all the state a
    /// deterministic gait needs -- everything else is a function of it.
    public struct WalkerGait
    {
        /// Position in the cycle, wrapped to [0, 1).
        public float Phase;

        /// Advance by the distance the hull covered. `cycleDistance` is how far the hull travels
        /// per complete cycle -- NOT the same as the stride, because a foot only has to cover its
        /// stride during the part of the cycle it spends on the ground. Confusing the two makes
        /// `stepDuration` quietly mean something other than it says.
        public void Advance(float distance, float cycleDistance)
        {
            if (cycleDistance <= 1e-4f) return;
            Phase = Mathf.Repeat(Phase + distance / cycleDistance, 1f);
        }

        /// Distance the hull covers per cycle, given the stride its legs can reach. The hull keeps
        /// moving while a foot is in the air, so it outruns the stride by exactly the swing share.
        public static float CycleDistance(float strideLength, float duty)
        {
            float stance = 1f - duty;
            return stance <= 1e-4f ? 0f : strideLength / stance;
        }

        /// Fraction of the cycle a given leg spends in the air.
        public static float SwingDuty(int swingSlots, int legCount)
            => legCount <= 0 ? 0f : Mathf.Clamp01(swingSlots / (float)legCount);

        /// Is this leg mid-swing, and how far through? `t` runs 0 to 1 across the swing.
        public static bool IsSwinging(float phase, float offset, float duty, out float t)
        {
            float local = Mathf.Repeat(phase - offset, 1f);
            if (duty > 1e-5f && local < duty)
            {
                t = local / duty;
                return true;
            }
            t = 0f;
            return false;
        }

        /// Fastest the hull may travel without the feet having to slide: a foot must cover the
        /// whole stride within the part of the cycle it spends on the ground.
        public static float MaxSpeed(float strideLength, float stepDuration, float duty)
        {
            float stance = 1f - duty;
            if (stance <= 1e-4f || stepDuration <= 1e-4f) return 0f;
            return strideLength * duty / (stepDuration * stance);
        }

        /// Rate at which a foot is dragged backwards through the body frame by a commanded twist.
        /// This is what a leg must undo during its swing, and the sign of it is what makes the
        /// outside of a turn stride long while the inside strides short or backwards.
        public static Vector3 FootDrift(Vector3 hipOffsetFromPivot, Vector3 linearVelocity,
                                        float yawRateRadians, Vector3 up)
            => linearVelocity + Vector3.Cross(up * yawRateRadians, hipOffsetFromPivot);

        /// Where a leg should aim: half of the drift it is about to experience, ahead of its rest
        /// foothold. Landing half ahead and drifting half back leaves the foot centred on its rest
        /// position over the cycle, which is what stops a leg walking out to the edge of its
        /// travel and staying pinned there.
        ///
        /// The lead is scaled by THIS leg's drift, not by a fixed fraction of the stride. During a
        /// turn the legs on the inside have their translation and rotation partly cancel, so they
        /// barely drift at all; leading those by a full half-stride plants them far from home with
        /// nothing to carry them back, and they spend the whole cycle against their yaw stop.
        ///
        /// `stanceDuration` is how long this foot will be on the ground. The clamp is a backstop
        /// for the case where a caller asks for more than the leg's arc can cover.
        public static Vector3 Foothold(Vector3 restFoothold, Vector3 drift,
                                       float stanceDuration, float swingDuration, float strideLength)
        {
            // Where the foot wants to be the moment it touches down: half a stance's drift ahead
            // of home, so that drifting back through the stance leaves it half a stride behind.
            Vector3 atTouchdown = drift * (stanceDuration * 0.5f);
            float limit = strideLength * 0.5f;
            if (atTouchdown.sqrMagnitude > limit * limit) atTouchdown = atTouchdown.normalized * limit;

            // But the foothold is committed at LIFT-OFF, and home travels with the hull for the
            // whole swing. Aiming at where home WILL be rather than where it is centres the
            // excursion; without this the foot lands a swing's worth of travel short every time,
            // and the leg spends the end of every stance against its yaw stop.
            return restFoothold + atTouchdown + drift * swingDuration;
        }

        /// How long a foot spends in the air, at the pace the hull is currently setting.
        public static float SwingDuration(float pace, float strideLength, float duty)
        {
            float stance = 1f - duty;
            if (stance <= 1e-4f) return 0f;
            return StanceDuration(pace, strideLength) * duty / stance;
        }

        /// How long a foot stays on the ground, at the pace the hull is currently setting. Stance
        /// is by definition the time it takes to drag a foot through exactly one stride, which is
        /// why this reduces to a division and needs no notion of duty at all.
        public static float StanceDuration(float pace, float strideLength)
            => pace <= 1e-4f ? 0f : strideLength / pace;

        /// Position of a swinging foot: a straight line from lift-off to touch-down, lifted by a
        /// sine arc so the foot clears the ground and sets down softly rather than stabbing.
        public static Vector3 SwingPoint(Vector3 from, Vector3 to, float t, Vector3 up, float height)
        {
            // Ease the horizontal travel as well as the lift. A foot that moves at constant speed
            // and stops dead on contact reads as animation; easing reads as mass.
            float eased = t * t * (3f - 2f * t);
            return Vector3.Lerp(from, to, eased) + up * (Mathf.Sin(t * Mathf.PI) * height);
        }

        /// Phase offsets for a metachronal wave: legs lift in sequence along one side and then the
        /// other, which is how a real hexapod walks when it is not in a hurry. `order` is the
        /// leg's place in that sequence.
        public static float MetachronalOffset(int order, int legCount)
            => legCount <= 0 ? 0f : order / (float)legCount;
    }
}
