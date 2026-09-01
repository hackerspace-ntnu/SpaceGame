using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Where the ship is, and which way it is pointing, at a given point through its descent — and
    /// then how it comes to rest once it has hit.
    ///
    /// <para>
    /// Pure and closed-form — no Unity state, no integration, no time step. That is what lets the
    /// shape be unit-tested at all, and it is also what makes the terminal pose EXACT: the wreck is
    /// persisted wherever the arrival leaves it, so an integrator that landed near the impact point
    /// would bury the hull or hover it, permanently, in the save file.
    /// </para>
    ///
    /// <para>
    /// The shape is a descending spiral. Horizontal radius shrinks linearly from the lateral budget
    /// to zero while the bearing sweeps; altitude falls as one-minus-t-squared. Chosen over the
    /// obvious Bezier because the budget is then respected BY CONSTRUCTION rather than by hoping a
    /// control point stays inside it — and the budget is a world-streaming limit, so exceeding it
    /// is a frame-rate problem on somebody else's machine rather than a visible bug on yours.
    /// </para>
    ///
    /// <para>
    /// The altitude curve is one-minus-t-squared and NOT the more obvious one-minus-t, squared.
    /// They look alike and behave oppositely: this one falls slowly at first and fastest at the
    /// end, which is the ground rush the sequence is built around, while the other dumps all its
    /// speed at the top and drifts in.
    /// </para>
    ///
    /// <para>
    /// The descent is COMMITTED: the nose stays pointed at the ground right into it, because this
    /// is a crash and a ship that levels off in the last second has landed. What used to be a flare
    /// is now <see cref="EvaluateSettle"/>, a separate beat that runs AFTER contact — the hull
    /// pivots off its nose and slams down onto its belly. The two halves exist so that the thing
    /// the player watches (a dive into the sand) and the thing the world keeps (a level wreck they
    /// can walk around in) can both be exactly right, instead of one being traded for the other.
    /// </para>
    /// </summary>
    public static class ArrivalTrajectory
    {
        /// <summary>
        /// The pose at <paramref name="t"/>, which is clamped to the zero-to-one range so a caller
        /// that overshoots its own timer gets the touchdown pose rather than an extrapolated one
        /// somewhere under the terrain.
        ///
        /// <para>
        /// At t=1 this is the moment of CONTACT, not the resting pose: the hull is over the impact
        /// point, still nose-down, held <see cref="ArrivalPath.TouchdownLift"/> above it so the
        /// part of it that reaches the ground is the nose rather than the cockpit the crew are
        /// sitting in. <see cref="EvaluateSettle"/> takes it from there.
        /// </para>
        /// </summary>
        public static void Evaluate(float t, in ArrivalPath path,
                                    out Vector3 position, out Quaternion rotation)
        {
            t = Mathf.Clamp01(t);

            float radius = path.LateralBudget * (1f - t);
            float bearing = (path.StartBearing + path.SweepDegrees * t) * Mathf.Deg2Rad;

            float sin = Mathf.Sin(bearing);
            float cos = Mathf.Cos(bearing);

            // The lift is a constant offset on the whole arc rather than something blended in at
            // the end, because a blend is a second curve that has to be kept continuous and this is
            // a few metres on top of a couple of thousand — invisible at the top, exact at the
            // bottom, which is the only place it means anything.
            position = new Vector3(
                path.ImpactPosition.x + radius * sin,
                path.ImpactPosition.y + path.TouchdownLift + path.StartAltitude * (1f - t * t),
                path.ImpactPosition.z + radius * cos);

            rotation = Quaternion.Euler(
                PitchDegrees(t, path),
                HeadingDegrees(t, path),
                -path.MaxBankDegrees * (1f - t));
        }

        /// <summary>
        /// The crash itself: the hull dropping off the nose it speared in on, down onto the belly it
        /// will rest on. <paramref name="k"/> is normalised over the settle and clamped, and at one
        /// this returns EXACTLY the impact position at a yaw-only rotation.
        ///
        /// <para>
        /// That exactness is the whole contract. This pose is what the wreck is saved as and what
        /// <c>ShipGrounding</c> measures the landing against — and that measurement assumes the
        /// hull differs from its prefab by yaw alone — so a settle that merely ended near level
        /// would leave the ship resting on one wing for the life of the world, with the deck of a
        /// walkable base sloping.
        /// </para>
        ///
        /// <para>
        /// Eased as k-squared rather than smoothed at both ends: a hull toppling off its nose is
        /// falling, so it starts slowly, accelerates, and STOPS DEAD when it meets the ground. That
        /// hard stop is the impact. Smoothing it out reads as the ship being lowered onto the sand
        /// by something rather than hitting it.
        /// </para>
        /// </summary>
        public static void EvaluateSettle(float k, in ArrivalPath path,
                                          out Vector3 position, out Quaternion rotation)
        {
            k = Mathf.Clamp01(k);

            float eased = k * k;

            Evaluate(1f, path, out Vector3 touchdown, out Quaternion touchdownRotation);

            position = Vector3.Lerp(touchdown, path.ImpactPosition, eased);
            rotation = Quaternion.Slerp(touchdownRotation, RestRotation(path), eased);
        }

        /// <summary>
        /// The attitude the wreck rests in: the heading the descent arrived on, and nothing else.
        /// Shared by the settle and by anything that has to know where the hull will be pointing
        /// before it gets there, so the two cannot disagree about it.
        /// </summary>
        public static Quaternion RestRotation(in ArrivalPath path) =>
            Quaternion.Euler(0f, HeadingDegrees(1f, path), 0f);

        /// <summary>
        /// How far the nose is down: the angle of the path the hull is actually flying, capped.
        ///
        /// <para>
        /// MEASURED rather than authored. A fixed nose-down angle that merely unwinds — which is
        /// what this was first — points the hull somewhere unrelated to where it is going, and at
        /// the top of a descent that starts level it is simply wrong. Deriving it from the vertical
        /// and horizontal rates means the ship aims at its landing point for free, and steepens on
        /// its own as the descent does.
        /// </para>
        /// <para>
        /// The cap is no longer only a sanity limit. The raw dive angle of this arc runs past
        /// seventy-five degrees at the end, so the last part of every descent sits exactly on
        /// <see cref="ArrivalPath.MaxPitchDegrees"/> — which makes that number the attitude the
        /// ship HITS THE GROUND in, and the size of the topple the settle then has to play out.
        /// </para>
        /// </summary>
        private static float PitchDegrees(float t, in ArrivalPath path)
        {
            // Vertical rate of the one-minus-t-squared altitude curve. Negative: it descends.
            float verticalRate = -2f * path.StartAltitude * t;

            HorizontalRate(t, path, out float dx, out float dz);
            float horizontalRate = Mathf.Sqrt(dx * dx + dz * dz);

            float dive = Mathf.Atan2(-verticalRate, horizontalRate) * Mathf.Rad2Deg;

            return Mathf.Min(dive, path.MaxPitchDegrees);
        }

        /// <summary>
        /// The direction of travel, so the hull points where it is going.
        ///
        /// <para>
        /// Differentiated by hand rather than sampled two frames apart, because a finite difference
        /// would make this depend on a step size that the pure form does not have — and because the
        /// obvious sample point, t plus epsilon, does not exist at the end of the descent.
        /// </para>
        /// <para>
        /// The radius reaching zero at impact does NOT produce a singularity: both derivative
        /// components carry the lateral budget as a factor, so the heading there is simply the
        /// bearing the ship came in on, reversed. A zero lateral budget would be a genuine
        /// degenerate case, and is refused by <see cref="ArrivalDirector"/> rather than papered
        /// over here.
        /// </para>
        /// </summary>
        private static float HeadingDegrees(float t, in ArrivalPath path)
        {
            HorizontalRate(t, path, out float dx, out float dz);

            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// d/dt of the spiral in the horizontal plane, with the radius shrinking at
        /// -LateralBudget per unit t. Shared by the heading and the dive angle so the two cannot
        /// disagree about which way the hull is travelling.
        /// </summary>
        private static void HorizontalRate(float t, in ArrivalPath path, out float dx, out float dz)
        {
            float bearing = (path.StartBearing + path.SweepDegrees * t) * Mathf.Deg2Rad;
            float sweepRate = path.SweepDegrees * Mathf.Deg2Rad;
            float radius = path.LateralBudget * (1f - t);

            dx = -path.LateralBudget * Mathf.Sin(bearing) + radius * sweepRate * Mathf.Cos(bearing);
            dz = -path.LateralBudget * Mathf.Cos(bearing) - radius * sweepRate * Mathf.Sin(bearing);
        }
    }
}
