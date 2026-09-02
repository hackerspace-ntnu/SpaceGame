using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where every astronaut in the VS lobby stands, and how far back the camera has to go to see
    /// them all.
    ///
    /// <para>
    /// Positions are in the anchor's local space: +X runs along the line, +Z runs away from the
    /// camera into the second row. The rank is centred on the anchor, so adding a team pushes the
    /// existing ones outwards symmetrically rather than sliding the whole line sideways.
    /// </para>
    ///
    /// <para>
    /// Seats are addressed by index whether or not anyone is standing in them, which is what stops
    /// a figure sliding sideways because somebody else joined — the rule the four fixed slots
    /// already held before there were teams. Empty seats simply draw nothing.
    /// </para>
    ///
    /// <para>
    /// The wrap is what makes 24 possible at all. A team of twelve in one line is eighteen metres
    /// of astronaut, and six of those is a rank no camera pull-back can frame legibly; wrapped four
    /// wide, the same twelve is a block six metres across.
    /// </para>
    /// </summary>
    public static class RankLayout
    {
        /// <summary>
        /// Metres between figures inside a team. Matches the spacing the four-figure rank already
        /// used, where anything tighter had each shoulder occluding the next suit's colour.
        /// </summary>
        public const float SeatSpacing = 1.45f;

        /// <summary>
        /// Metres between a team's front row and the row behind it.
        ///
        /// Has to clear <see cref="SeatSpacing"/> by enough that a back-row figure reads as
        /// standing behind the row in front rather than slotted between two of its shoulders — the
        /// same occlusion problem <see cref="SeatSpacing"/> solves sideways, solved here in depth.
        /// </summary>
        public const float RowSpacing = 1.6f;

        /// <summary>
        /// Metres of empty sand between two teams, measured between their nearest seats.
        ///
        /// Comfortably more than <see cref="SeatSpacing"/>: the gap is the only thing saying these
        /// are two groups rather than one line, and a gap that merely exceeds the spacing reads as
        /// an uneven line rather than as a division.
        /// </summary>
        public const float TeamGap = 3.2f;

        /// <summary>How wide a team gets before it stands in two rows.</summary>
        public const int MaxSeatsPerRow = 4;

        public static int SeatsPerRow(int teamSize) =>
            teamSize < MaxSeatsPerRow ? Mathf.Max(1, teamSize) : MaxSeatsPerRow;

        public static int RowsFor(int teamSize) =>
            Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, teamSize) / (float)MaxSeatsPerRow));

        /// <summary>
        /// How many teams stand side by side before the next one goes behind them.
        ///
        /// The same number as <see cref="MaxSeatsPerRow"/>, and for the same reason one level up: a
        /// line of eight teams is 45 m of astronaut, and no camera pull-back frames that legibly.
        /// Four abreast keeps the widest legal rank at roughly the width of a four-team one, which
        /// is the shape the shot was composed around.
        /// </summary>
        public const int MaxTeamsPerRow = 4;

        /// <summary>
        /// Metres of clear sand between one row of teams and the next, measured from the back of the
        /// front row's own seats to the front of the next row's.
        ///
        /// Generous compared with <see cref="RowSpacing"/> because these are whole groups rather
        /// than two ranks of one team: the gap has to read as "those teams are further away", and it
        /// is also the lever <see cref="EyeHeight"/> divides by — a tighter gap needs a higher eye
        /// to see over the front row.
        /// </summary>
        public const float TeamRowSpacing = 6f;

        /// <summary>How many teams stand in a full row.</summary>
        public static int TeamsPerRow(int teams) =>
            teams < MaxTeamsPerRow ? Mathf.Max(1, teams) : MaxTeamsPerRow;

        /// <summary>How many rows of teams there are.</summary>
        public static int TeamRowsFor(int teams) =>
            Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, teams) / (float)MaxTeamsPerRow));

        /// <summary>How deep one team's own block of seats is, front row to back row.</summary>
        public static float TeamDepth(int teamSize) => (RowsFor(teamSize) - 1) * RowSpacing;

        /// <summary>Centre-to-centre distance from one row of teams to the next.</summary>
        public static float TeamRowPitch(int teamSize) => TeamDepth(teamSize) + TeamRowSpacing;

        /// <summary>How far across one team's block is, from its first seat to its last.</summary>
        public static float TeamWidth(int teamSize) => (SeatsPerRow(teamSize) - 1) * SeatSpacing;

        /// <summary>
        /// The whole rank, from the leftmost seat to the rightmost.
        ///
        /// Measured across the team centres rather than derived from a formula, because the stagger
        /// in <see cref="TeamCenter"/> means the widest row is not always the first one. For a
        /// single row this reproduces the old arithmetic exactly:
        /// <c>count * TeamWidth + (count - 1) * TeamGap</c>.
        /// </summary>
        public static float TotalWidth(int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int team = 0; team < count; team++)
            {
                float x = TeamCenter(team, count, teamSize).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }

            return max - min + TeamWidth(teamSize);
        }

        /// <summary>
        /// The whole rank front to back: every row of teams, each of them as deep as one team's own
        /// block of seats.
        /// </summary>
        public static float TotalDepth(int teams, int teamSize) =>
            (TeamRowsFor(teams) - 1) * TeamRowPitch(teamSize) + TeamDepth(teamSize);

        /// <summary>
        /// The middle of a team's block, which is where its nameplate hangs and what a player
        /// clicks to join.
        ///
        /// <para>
        /// Teams fill a row <see cref="MaxTeamsPerRow"/> wide and then wrap behind, and a partly
        /// filled last row is centred under the ones in front — the same rule
        /// <see cref="SeatPosition"/> already applies to the seats inside a team. Without the wrap
        /// the rank grows without bound and the camera pays for every team in astronaut pixels:
        /// eight teams in one line is 45 m across against 21 m wrapped.
        /// </para>
        ///
        /// <para>
        /// Every team sits on one shared lattice of half-pitch slots, odd rows offset by half a
        /// pitch, and the whole rank is then re-centred on the anchor <b>once</b>. So a back-row
        /// team never stands directly behind a front-row one: its centre always falls exactly
        /// halfway between two of them, for every legal team count.
        /// </para>
        ///
        /// <para>
        /// The single lattice is what makes that true. Centring each row on <i>itself</i> and then
        /// staggering it looks equivalent and is not: with five teams the lone back team is centred
        /// on nothing, and the two corrections cancel to put it exactly behind a front team. The
        /// cost is that a short back row is no longer centred under the row in front — it fills
        /// lattice slots from the left — which is a fair price for never hiding anybody.
        /// </para>
        ///
        /// <para>
        /// Where the teams are narrow enough that <see cref="TeamWidth"/> clears
        /// <see cref="TeamGap"/> — teams of three or fewer — a back team lands cleanly in the gap
        /// with no lateral overlap at all. Wider teams still overlap in silhouette, which is what a
        /// crowd standing behind another crowd is supposed to look like.
        /// </para>
        /// </summary>
        public static Vector3 TeamCenter(int team, int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);
            int perRow = TeamsPerRow(count);
            int row = team / perRow;
            int column = team % perRow;

            // The distance from one team's centre to the next is its own width plus the gap to the
            // next block — NOT plus another seat spacing on top. Adding SeatSpacing here (as an
            // earlier draft did) widens the real gap between the two nearest seats to
            // TeamGap + SeatSpacing, which contradicts the constant's own doc: TeamGap is defined
            // as that seat-to-seat gap, not as a value the layout is free to pad further.
            float pitch = TeamWidth(teamSize) + TeamGap;

            return new Vector3(LatticeX(row, column, pitch) - CenterOffset(count, pitch),
                               0f,
                               row * TeamRowPitch(teamSize));
        }

        /// <summary>
        /// A team's slot on the shared lattice, before the rank is centred. Odd rows are offset half
        /// a pitch; row zero defines the lattice, so a single row is untouched.
        /// </summary>
        private static float LatticeX(int row, int column, float pitch) =>
            (column + 0.5f * (row % 2)) * pitch;

        /// <summary>
        /// The middle of the occupied lattice, which is what gets subtracted to put the rank back on
        /// the anchor. For a single row this is exactly <c>(count - 1) / 2 * pitch</c>, reproducing
        /// the arithmetic a two-to-four-team rank has always used.
        /// </summary>
        private static float CenterOffset(int teams, float pitch)
        {
            int count = Mathf.Max(1, teams);
            int perRow = TeamsPerRow(count);
            int rows = TeamRowsFor(count);

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int row = 0; row < rows; row++)
            {
                int inThisRow = Mathf.Min(perRow, count - row * perRow);

                min = Mathf.Min(min, LatticeX(row, 0, pitch));
                max = Mathf.Max(max, LatticeX(row, inThisRow - 1, pitch));
            }

            return (min + max) * 0.5f;
        }

        /// <summary>
        /// One seat, in the anchor's local space.
        ///
        /// A partly-filled last row is centred under the rows above it, so a team of five reads as
        /// four and one in the middle rather than four and one hanging off the left edge.
        /// </summary>
        public static Vector3 SeatPosition(int team, int seat, int teams, int teamSize)
        {
            int perRow = SeatsPerRow(teamSize);
            int row = seat / perRow;
            int column = seat % perRow;

            int inThisRow = Mathf.Min(perRow, Mathf.Max(1, teamSize) - row * perRow);
            float x = (column - (inThisRow - 1) * 0.5f) * SeatSpacing;

            Vector3 centre = TeamCenter(team, teams, teamSize);
            return new Vector3(centre.x + x, 0f, centre.z + row * RowSpacing);
        }

        /// <summary>
        /// How far the camera has to sit from the rank's centre to hold <paramref name="width"/>
        /// metres across, with <paramref name="margin"/> as headroom: the rank fills
        /// <c>1 / margin</c> of the frame and the rest is air, so 1.2 leaves about a sixth.
        ///
        /// Takes the horizontal field of view, because the rank is a horizontal problem — a camera
        /// fitted on its vertical FOV frames a rank of four and clips a rank of twenty-four.
        /// </summary>
        public static float CameraDistance(float width, float horizontalFovDegrees, float margin)
        {
            float halfAngle = Mathf.Max(1f, horizontalFovDegrees) * 0.5f * Mathf.Deg2Rad;
            return Mathf.Max(0.01f, width * margin * 0.5f / Mathf.Tan(halfAngle));
        }

        /// <summary>
        /// How tall an astronaut is, in metres. Used to decide how high the eye has to sit to see
        /// over a front-row head, not to size anything.
        /// </summary>
        public const float HeadHeight = 1.8f;

        /// <summary>
        /// How steeply the camera looks down on a multi-row rank, in degrees below level.
        ///
        /// Steep enough that the rows separate on screen — from a near-level eye the back row's
        /// plates land in the same band of frame as the front row's and the two smear together —
        /// but shallow enough that the shot still reads as standing among the astronauts rather
        /// than as a map of them.
        /// </summary>
        public const float MultiRowDownAngle = 16f;

        /// <summary>
        /// The distance that holds a rank <paramref name="width"/> metres across and
        /// <paramref name="height"/> metres tall, whichever needs more room.
        ///
        /// Two axes rather than one, because the horizontal answer alone is what makes a short or
        /// narrow window frame the rank badly: a camera fitted on width has not been asked whether
        /// the usable band of screen is tall enough to hold what it just framed.
        /// </summary>
        public static float CameraDistance(float width, float height, float horizontalFovDegrees,
            float verticalFovDegrees, float margin) =>
            Mathf.Max(CameraDistance(width, horizontalFovDegrees, margin),
                      CameraDistance(height, verticalFovDegrees, margin));

        /// <summary>
        /// How high above the front row's ground the eye must sit for the back row to be visible
        /// over it, in metres. Zero when there is only one row and no lift is needed.
        ///
        /// <para>
        /// An eye at <c>HeadHeight + distance * tan(MultiRowDownAngle)</c> looks down on the rank's
        /// heads at <see cref="MultiRowDownAngle"/>, so a camera that has backed off needs a HIGHER
        /// eye, not a lower one — the angle is what is held, not the height.
        /// </para>
        ///
        /// <para>
        /// This is not a nicety. The lobby's authored eye sits 1.389 m above the anchor, below a
        /// 1.8 m head, so a second row of teams is entirely occluded without it.
        /// </para>
        /// </summary>
        public static float EyeHeight(int teams, int teamSize, float distance)
        {
            if (TeamRowsFor(teams) <= 1) return 0f;

            return HeadHeight + Mathf.Max(0f, distance) * Mathf.Tan(MultiRowDownAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// How far above its own ground a team's plate floats, in metres. Rows further from the
        /// camera hang their plates higher, so on screen the back row's plates sit clearly ABOVE
        /// the front row's instead of in the same band — vertical position is what says which row
        /// a plate belongs to when eight of them are up at once.
        /// </summary>
        public const float PlateLiftFront = 2.2f;

        /// <summary>Extra metres of lift per row of teams behind the front one.</summary>
        public const float PlateLiftStep = 1.5f;

        public static float PlateLift(int team, int teams) =>
            PlateLiftFront + team / TeamsPerRow(Mathf.Max(1, teams)) * PlateLiftStep;

        /// <summary>The highest plate in the rank — the last row's — for the camera fit.</summary>
        public static float MaxPlateLift(int teams) =>
            PlateLiftFront + (TeamRowsFor(teams) - 1) * PlateLiftStep;
    }
}
