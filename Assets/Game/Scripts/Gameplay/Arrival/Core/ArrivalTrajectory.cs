using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Where the ship is, and which way it is pointing, at a given point through its descent.
    ///
    /// <para>
    /// Pure and closed-form — no Unity state, no integration, no time step. That is what lets the
    /// shape be unit-tested at all, and it is also what makes the terminal pose EXACT: the wreck is
    /// persisted wherever the descent leaves it, so an integrator that landed near the impact point
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
    /// </summary>
    public static class ArrivalTrajectory
    {
        /// <summary>
        /// The pose at <paramref name="t"/>, which is clamped to the zero-to-one range so a caller
        /// that overshoots its own timer gets the terminal pose rather than an extrapolated one
        /// somewhere under the terrain.
        /// </summary>
        public static void Evaluate(float t, in ArrivalPath path,
                                    out Vector3 position, out Quaternion rotation)
        {
            t = Mathf.Clamp01(t);

            float radius = path.LateralBudget * (1f - t);
            float bearing = (path.StartBearing + path.SweepDegrees * t) * Mathf.Deg2Rad;

            float sin = Mathf.Sin(bearing);
            float cos = Mathf.Cos(bearing);

            position = new Vector3(
                path.ImpactPosition.x + radius * sin,
                path.ImpactPosition.y + path.StartAltitude * (1f - t * t),
                path.ImpactPosition.z + radius * cos);

            rotation = Quaternion.Euler(
                PitchDegrees(t, path),
                HeadingDegrees(t, path),
                -path.MaxBankDegrees * (1f - t));
        }

        /// <summary>
        /// How far the nose is down: the angle of the path the hull is actually flying, capped, and
        /// flared back to level for the landing.
        ///
        /// <para>
        /// MEASURED rather than authored. A fixed nose-down angle that merely unwinds — which is
        /// what this was first — points the hull somewhere unrelated to where it is going, and at
        /// the top of a descent that starts level it is simply wrong. Deriving it from the vertical
        /// and horizontal rates means the ship aims at its landing point for free, and steepens on
        /// its own as the descent does.
        /// </para>
        /// <para>
        /// The flare is not decoration. Uncapped, the last moments of this arc are a near-vertical
        /// dive, and the terminal attitude is the attitude the WRECK is saved in — so without it
        /// the world's first landmark is a ship stood on its nose forever.
        /// </para>
        /// </summary>
        private static float PitchDegrees(float t, in ArrivalPath path)
        {
            // Vertical rate of the one-minus-t-squared altitude curve. Negative: it descends.
            float verticalRate = -2f * path.StartAltitude * t;

            HorizontalRate(t, path, out float dx, out float dz);
            float horizontalRate = Mathf.Sqrt(dx * dx + dz * dz);

            float dive = Mathf.Atan2(-verticalRate, horizontalRate) * Mathf.Rad2Deg;
            dive = Mathf.Min(dive, path.MaxPitchDegrees);

            return dive * FlareFactor(t, path.FlareFraction);
        }

        /// <summary>
        /// One through most of the descent, easing to exactly zero at touchdown.
        ///
        /// <para>
        /// Clamped to a minimum rather than allowing zero, because a zero-length flare is a step
        /// discontinuity: the nose would be 55 degrees down on the last frame and level on the one
        /// after, which reads as the hull snapping rather than landing.
        /// </para>
        /// </summary>
        private static float FlareFactor(float t, float flareFraction)
        {
            float fraction = Mathf.Clamp(flareFraction, 0.01f, 1f);
            float start = 1f - fraction;

            if (t <= start) return 1f;

            return Mathf.SmoothStep(1f, 0f, (t - start) / fraction);
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
