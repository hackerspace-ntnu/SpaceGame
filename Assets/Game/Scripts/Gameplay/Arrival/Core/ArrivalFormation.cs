using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// One descent per team, each on its own arc, all landing where the arena says.
    ///
    /// <para>
    /// Pure and closed-form for the reason <see cref="ArrivalTrajectory"/> is: the shape of a
    /// versus start is worth being able to test without a scene, and the two things it has to get
    /// right — every ship lands on its own authored point FACING the authored way, and no two ships
    /// fly the same line — are both invisible failures otherwise. A ship that lands facing the
    /// wrong way still lands, and a formation where every arc is identical still flies; both just
    /// look wrong, forever, because the hull is left wherever the trajectory ended.
    /// </para>
    ///
    /// <para>
    /// The heading is not steered. <see cref="ArrivalTrajectory"/> points the hull along the way it
    /// is actually travelling, so the only way to decide where a ship ends up facing is to choose
    /// the bearing it STARTS from — see <see cref="BearingForLandingYaw"/>. That is why this is
    /// arithmetic rather than a controller.
    /// </para>
    /// </summary>
    public static class ArrivalFormation
    {
        /// <summary>
        /// The widest stagger allowed, as a fraction. Below one by construction: a spread of one
        /// would give the outermost ship a lateral budget of zero, which is the degenerate descent
        /// <see cref="ArrivalDirector"/> refuses to fly — a spiral with no radius has no heading.
        /// </summary>
        public const float MaxSpread = 0.9f;

        /// <summary>
        /// The path one team flies: the authored descent, moved onto that team's landing point,
        /// turned to arrive on its heading, and pulled off every other team's line.
        ///
        /// <para>
        /// Three things vary per team, and they vary together on purpose. The sweep MIRRORS on odd
        /// teams, so neighbours spiral into the arena from opposite directions rather than chasing
        /// each other round the same circle. The lateral budget and the start altitude are then
        /// pulled DOWN across the field in opposite phases, so the ship that starts furthest out
        /// also starts lowest — which makes the arcs differ in shape, not merely in position, and
        /// keeps two ships from sharing an altitude on the frame they pass each other.
        /// </para>
        ///
        /// <para>
        /// Down, never up, and that is the whole reason the stagger is one-sided. Both authored
        /// numbers are CEILINGS rather than middles: the lateral budget is a world-streaming limit
        /// — chunks pin under tracked entities, so a wider arc drags the streamer through more of
        /// them at speed — and the start altitude is the top of the band where the desert skybox
        /// and the volumetric clouds still read correctly. A stagger that spread symmetrically
        /// would put half the formation past both, which is a frame-rate problem on somebody else's
        /// machine and a sky that goes wrong at the top of the arc.
        /// </para>
        ///
        /// <para>
        /// The duration is not varied and must not be: it is the same for every flight so the
        /// ships land together, which is what makes a versus start a start rather than a queue.
        /// </para>
        /// </summary>
        public static ArrivalPath PathFor(in ArrivalPath authored, int team, int teamCount,
                                          Vector3 impact, float landingYaw, float spread)
        {
            ArrivalPath path = authored;

            // Odd teams mirror the authored sweep rather than the sign being chosen outright, so a
            // formation of one — and team zero in every formation — flies exactly the arc that was
            // authored, and a retune of that arc is still visible in the game.
            float mirror = (team & 1) == 0 ? 1f : -1f;

            // A lone flight is a story descent in all but name, so nothing is staggered at all
            // rather than it being handed one arbitrary end of a range that has no other end.
            float amount = teamCount <= 1 ? 0f : Mathf.Clamp(spread, 0f, MaxSpread);
            float across = Fraction(team, teamCount);

            path.ImpactPosition = impact;
            path.SweepDegrees = authored.SweepDegrees * mirror;

            // The bank follows the sweep. A hull that mirrored its turn but not its roll would bank
            // OUT of the corner it is flying, which reads as a ship in trouble rather than a ship
            // arriving.
            path.MaxBankDegrees = authored.MaxBankDegrees * mirror;

            path.LateralBudget = authored.LateralBudget * (1f - amount * across);
            path.StartAltitude = authored.StartAltitude * (1f - amount * (1f - across));

            path.StartBearing = BearingForLandingYaw(landingYaw, path.SweepDegrees);

            return path;
        }

        /// <summary>
        /// The bearing a descent must start from to arrive on <paramref name="landingYaw"/>.
        ///
        /// <para>
        /// Read off <see cref="ArrivalTrajectory"/> rather than guessed. At touchdown the spiral's
        /// radius has reached zero, so both horizontal rate components carry only the
        /// <c>-LateralBudget</c> term: the heading there is <c>atan2(-sin b, -cos b)</c>, which is
        /// the final bearing <c>b</c> turned half a circle. The final bearing is the start plus the
        /// whole sweep, so the start is the wanted yaw less the sweep less half a turn — whatever
        /// sign the sweep has.
        /// </para>
        /// </summary>
        public static float BearingForLandingYaw(float landingYaw, float sweepDegrees) =>
            Mathf.Repeat(landingYaw - sweepDegrees - 180f, 360f);

        /// <summary>
        /// Where a team sits across the field, from zero at the first to one at the last.
        ///
        /// <para>
        /// Folded rather than indexed: a team number can arrive from a peer built with different
        /// rules, and an arc computed from an out-of-range fraction would put a hull somewhere
        /// nothing else in the formation expects. The same courtesy every other value that crosses
        /// the wire gets in this project.
        /// </para>
        /// </summary>
        public static float Fraction(int team, int teamCount)
        {
            if (teamCount <= 1) return 0f;

            return Mathf.Clamp(team, 0, teamCount - 1) / (float)(teamCount - 1);
        }
    }
}
