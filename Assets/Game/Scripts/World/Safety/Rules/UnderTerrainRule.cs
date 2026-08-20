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

        public UnderTerrainVerdict Evaluate(float bodyY, bool hasTerrain, float terrainY)
        {
            if (hasTerrain)
            {
                return bodyY < terrainY - DepthTolerance
                    ? new UnderTerrainVerdict(UnderTerrainAction.Lift, terrainY + SurfaceClearance)
                    : UnderTerrainVerdict.Fine;
            }

            return bodyY < AbsoluteFloorY
                ? new UnderTerrainVerdict(UnderTerrainAction.Park, bodyY)
                : UnderTerrainVerdict.Fine;
        }
    }
}
