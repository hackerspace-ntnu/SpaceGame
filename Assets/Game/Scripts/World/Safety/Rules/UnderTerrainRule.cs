// When a body is under the terrain, and what to do about it: pure arithmetic, no components.
//
// Split out from UnderTerrainGuard for the same reason ChunkGrid was split out of WorldStreamer —
// this is the part that decides, and a decision inlined in Update() is a decision nothing can test.
//
// Two things make this more than "is y below the ground".
//
//   * A tolerance, because the interesting comparison is not exact. A capsule standing in a dip,
//     a foot sunk into a mesh seam, or a terrain sample taken a frame before a collider settles
//     all read as fractionally below the surface while nothing is actually wrong. Reacting to
//     those would teleport a player who is simply standing still.
//
//   * A separate answer for "no terrain here", because the failure that strands a player under
//     the world is usually a chunk that has not loaded — and the ground they fell through does
//     not exist to be measured yet. Comparing against a terrain height nobody can sample is not
//     a safer check, it is no check at all, so that case gets its own verdict instead of being
//     folded into "fine".
//
// The absolute floor only applies when terrain is unsampleable. With terrain present, a lift is
// always the better answer, however far down the body has fallen.
namespace SpaceGame.World.Safety
{
    public enum UnderTerrainAction
    {
        /// <summary>Above the surface, or not far enough below it to be worth touching.</summary>
        None,

        /// <summary>Under the terrain with a measured surface to return to.</summary>
        Lift,

        /// <summary>
        /// Below the absolute floor with no terrain to measure. Hold the body still at its own
        /// X/Z rather than moving it sideways or letting it keep falling — a parked body still
        /// pins the chunk under it, so the terrain it needs is on its way and a later evaluation
        /// turns this into a <see cref="Lift"/>.
        /// </summary>
        Park,

        /// <summary>
        /// The hold has run its course and no ground arrived. Put the body back somewhere known
        /// to have been solid instead of going on holding it.
        ///
        /// <para>
        /// This exists because <see cref="Park"/> is an optimistic verdict: it assumes the chunk
        /// under the body is on its way. When the body is somewhere the streamer owes nothing —
        /// off the grid, or over a chunk authored without terrain — nothing is coming, and a hold
        /// with no way out is a player frozen in the void with no recourse but to quit. A body put
        /// back on ground it has actually stood on is recoverable; one held forever is not.
        /// </para>
        /// </summary>
        Recover,
    }

    public readonly struct UnderTerrainVerdict
    {
        public readonly UnderTerrainAction Action;

        /// <summary>Where to put the body. Only meaningful when <see cref="Action"/> is Lift.</summary>
        public readonly float TargetY;

        public UnderTerrainVerdict(UnderTerrainAction action, float targetY)
        {
            Action = action;
            TargetY = targetY;
        }

        public static readonly UnderTerrainVerdict Fine = new(UnderTerrainAction.None, 0f);
    }

    public readonly struct UnderTerrainRule
    {
        /// <summary>How far below the surface counts as under it rather than on it.</summary>
        public readonly float DepthTolerance;

        /// <summary>How far above the surface a lifted body is placed.</summary>
        public readonly float SurfaceClearance;

        /// <summary>Below this, with no terrain to sample, the body has left the world.</summary>
        public readonly float AbsoluteFloorY;

        public UnderTerrainRule(float depthTolerance, float surfaceClearance, float absoluteFloorY)
        {
            // Negative values invert the comparison and turn the guard into the very bug it
            // exists to catch, so they are clamped rather than trusted.
            DepthTolerance = depthTolerance > 0f ? depthTolerance : 0f;
            SurfaceClearance = surfaceClearance > 0f ? surfaceClearance : 0f;
            AbsoluteFloorY = absoluteFloorY;
        }

        /// <summary>
        /// The original three-part question. "No terrain here" is read as "there is no surface to
        /// be under", which is the right answer for an interior and for anywhere off the map.
        /// </summary>
        public UnderTerrainVerdict Evaluate(float bodyY, bool hasTerrain, float terrainY)
            => Evaluate(bodyY, hasTerrain, terrainY, groundExpected: false);

        /// <param name="groundExpected">
        /// Whether the world owes ground at this position and has not delivered it — the body is
        /// inside the streamed grid with nothing at all beneath it. Only the component can answer
        /// this, because it is a question about the streamer and the colliders, not about heights.
        ///
        /// It exists because the absolute floor is far too late a signal for the failure that
        /// actually strands people. A client's player object can arrive before that client's own
        /// copy of the chunk scene does; with no local terrain collider the body simply falls, and
        /// the floor does not notice until six hundred metres later — by which time the chunk has
        /// loaded overhead and the recovery is a burial being undone rather than one being avoided.
        /// </param>
        public UnderTerrainVerdict Evaluate(float bodyY, bool hasTerrain, float terrainY, bool groundExpected)
            => Evaluate(bodyY, hasTerrain, terrainY, groundExpected, parkExpired: false);

        /// <param name="parkExpired">
        /// Whether this body has already been held below the floor for as long as the guard is
        /// willing to hold it. Only the component can answer this — it is a question about how
        /// long the hold has run, not about heights.
        ///
        /// It turns the below-floor park from a wait with no exit into a bounded one. See
        /// <see cref="UnderTerrainAction.Recover"/> for why that matters.
        /// </param>
        public UnderTerrainVerdict Evaluate(float bodyY, bool hasTerrain, float terrainY,
                                            bool groundExpected, bool parkExpired)
        {
            // A measured surface always wins. It answers the question outright, so how far the body
            // has fallen and whether anything is owed here stop mattering.
            if (hasTerrain)
            {
                return bodyY < terrainY - DepthTolerance
                    ? new UnderTerrainVerdict(UnderTerrainAction.Lift, terrainY + SurfaceClearance)
                    : UnderTerrainVerdict.Fine;
            }

            // Ground is coming. Wait for it where we stand rather than falling through the space
            // where it will be — a parked body still pins the chunk it is waiting on.
            if (groundExpected)
                return new UnderTerrainVerdict(UnderTerrainAction.Park, bodyY);

            if (bodyY >= AbsoluteFloorY)
                return UnderTerrainVerdict.Fine;

            // Below the floor with nothing to measure. Wait first — the chunk is usually on its
            // way — but not indefinitely: a hold nothing can end is worse than the fall it
            // prevented, because the fall at least has a bottom.
            return new UnderTerrainVerdict(
                parkExpired ? UnderTerrainAction.Recover : UnderTerrainAction.Park, bodyY);
        }
    }
}
