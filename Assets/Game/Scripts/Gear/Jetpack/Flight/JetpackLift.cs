using UnityEngine;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>
    /// What a load on the end of a rope does to the pack. One number, used twice.
    ///
    /// <para>
    /// <b>The whole of the problem is that a rope shares out an ACCELERATION.</b> The leash
    /// resolves its two ends by mass share, so a pilot of mass m towing a load of mass M rises at
    /// <c>thrust/(1 + M/m) − g</c> rather than at <c>thrust − g</c>. At this pack's 30 against
    /// this world's 18 that is +12 m/s² alone and −3 m/s² with an equal-weight passenger: not a
    /// slow lift, a sink. Beating it with raw thrust alone would need more than twice gravity,
    /// which is a pack that flies at nearly double the speed for every solo pilot as well.
    /// </para>
    /// <para>
    /// So the load re-enters the model here instead, as a factor the pack answers with: it burns
    /// harder for what is hanging off it. <see cref="Factor"/> scales the thrust AND the heat by
    /// the same figure, deliberately — that is the price, and splitting them is how a lift becomes
    /// free (<c>GDC-L1-SYS-0008</c>: a source has to move with its sink).
    /// </para>
    /// <para>
    /// Pure and static, so the arithmetic that decides whether a passenger leaves the ground is
    /// testable without a rope, a second player or a physics scene.
    /// </para>
    /// </summary>
    public static class JetpackLift
    {
        /// <summary>
        /// How much harder the pack works for a load of <paramref name="loadRatio"/> times the
        /// pilot's own mass. 1 for a pilot flying alone, and never less.
        ///
        /// <para>
        /// <see cref="JetpackConfig.MaxLiftRatio"/> clamps the RATIO rather than the result, and
        /// that is the bound that keeps the pack from being a crane: the load's real weight is
        /// always in the physics, so anything heavier than the clamp still drags the pair down at
        /// its full weight while the pack has stopped adding thrust for it. Nothing has to decide
        /// what counts as "a person" — the mass does it (<c>GDC-L1-SYS-0007</c>: the strategy to
        /// bound here is a rope onto something huge as a free power boost).
        /// </para>
        /// </summary>
        public static float Factor(float loadRatio, JetpackConfig cfg)
        {
            if (cfg == null || float.IsNaN(loadRatio) || loadRatio <= 0f) return 1f;

            return 1f + Mathf.Min(loadRatio, cfg.MaxLiftRatio) * cfg.LiftAssist;
        }

        /// <summary>
        /// The climb a pilot and their load actually get at full thrust, m/s², ignoring drag.
        /// Positive is up.
        ///
        /// <para>
        /// The rope's own share arithmetic restated: two ends coupled by mass share move at the
        /// mass-weighted average of their accelerations. Here so the balance question — does a
        /// passenger leave the ground, and how briskly — can be asked of the numbers rather than
        /// flown for (<c>GDC-L1-BAL-0005</c>: tune with math, decide with play).
        /// </para>
        /// </summary>
        public static float PairClimb(float pilotMass, float loadMass, JetpackConfig cfg)
        {
            if (cfg == null || pilotMass <= 0f) return 0f;

            float ratio = Mathf.Max(0f, loadMass) / pilotMass;

            return cfg.ThrustAcceleration * Factor(ratio, cfg) / (1f + ratio) - cfg.Gravity;
        }
    }
}
