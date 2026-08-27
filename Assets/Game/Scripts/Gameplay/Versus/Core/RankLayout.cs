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

        /// <summary>How far across one team's block is, from its first seat to its last.</summary>
        public static float TeamWidth(int teamSize) => (SeatsPerRow(teamSize) - 1) * SeatSpacing;

        /// <summary>The whole rank, from the leftmost seat to the rightmost.</summary>
        public static float TotalWidth(int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);
            return count * TeamWidth(teamSize) + (count - 1) * TeamGap;
        }

        /// <summary>
        /// The middle of a team's block, which is where its nameplate hangs and what a player
        /// clicks to join.
        /// </summary>
        public static Vector3 TeamCenter(int team, int teams, int teamSize)
        {
            // The distance from one team's centre to the next is its own width plus the gap to the
            // next block — NOT plus another seat spacing on top. Adding SeatSpacing here (as an
            // earlier draft did) widens the real gap between the two nearest seats to
            // TeamGap + SeatSpacing, which contradicts the constant's own doc: TeamGap is defined
            // as that seat-to-seat gap, not as a value the layout is free to pad further.
            float pitch = TeamWidth(teamSize) + TeamGap;
            float offset = (team - (Mathf.Max(1, teams) - 1) * 0.5f) * pitch;

            return new Vector3(offset, 0f, 0f);
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
            return new Vector3(centre.x + x, 0f, row * RowSpacing);
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
    }
}
