// Builds a planned settlement's streets and terraces out of prefab pieces, in this order: stairs
// wherever a street's profile changes level, terrace walls along each step between two buildings on
// different levels, then the street surfaces and the paths out from each front door. A piece that
// would overlap anything already laid is left out -- that is what keeps crossings, stair feet and wall
// decks clean.
//
// Two kits. The street kit stretches its pieces to the ground: one flight per level change, scaled
// until its foot meets the ground; one wall block per stop, scaled in Y to the drop; slabs in rows.
// The terrace kit never stretches anything: a chain of flights and landings climbs several levels at
// once (SettlementStairChain), a wall is a top piece over stacked courses with pillars at its bends and
// ends (SettlementWallStack), and each stretch of street gets the surface SettlementStreetSurfaces ranked
// it for: a road of continuous tiles with a node at its junctions (SettlementRoadTiles), the street kit's
// slab rows, or a stone path of scattered stepping stones (SettlementStonePath), which never has stairs.
// Each switches on when the style has its pieces.
//
// Piece origins: a slab, road tile, node or stepping stone at its ground centre (tiles laid end to end
// along local Z); a road end on the edge that joins the last tile, reaching out along +Z; stairs at the
// top of the flight where it meets the wall, descending along +Z; a landing at its back edge; a terrace
// wall, course or pillar at the top of its face, +Z pointing down the slope, its deck reaching back along -Z.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementStreetPaver
    {
        // Consecutive street-kit wall blocks are laid this much further apart than they are long.
        private const float WallJoint = 0.02f;
        // How far two pieces may overlap before the later one is left out.
        private const float AllowedOverlap = 0.1f;
        // A wall piece reaching further than this into a building's footprint is left out; the building stands there.
        private const float WallIntoBuilding = 0.5f;
        private const int ContourSmoothing = 2;
        // Corner-cutting passes over a street's centre line before road tiles and stones follow it.
        private const int StreetSmoothing = 2;
        // Slabs take the direction of the street segment under them: their tangent is read over this short a stretch.
        private const float SlabTangentWindow = 0.01f;
        // A wall's drop is measured this far in front of its face, clear of the heightmap's ramp under the deck.
        private const float WallGroundProbe = 1f;
        // A street-kit flight is stretched to fit its drop by at most this factor either way; beyond it something else is wrong.
        private const float MinFlightFit = 0.5f;
        private const float MaxFlightFit = 2f;
        private static readonly Vector2 NoFootprint = new Vector2(1f, 1f);

        private readonly SettlementStreetStyle style;
        private readonly Transform parent;
        private readonly Transform ignoreUnder;
        private readonly List<SettlementFootprint> laid = new();
        private readonly Dictionary<GameObject, Rect> measured = new();
        private readonly List<Vector3> nodes = new();

        public int SlabsLaid { get; private set; }
        public int StairsLaid { get; private set; }
        public int LandingsLaid { get; private set; }
        public int WallsLaid { get; private set; }
        public int CoursesLaid { get; private set; }
        public int PillarsLaid { get; private set; }
        public int TilesLaid { get; private set; }
        public int NodesLaid { get; private set; }
        public int EndsLaid { get; private set; }
        public int StonesLaid { get; private set; }
        /// <summary>Wall blocks whose drop needed more courses than <c>maxWallCourses</c>; they hang short of the ground.</summary>
        public int WallsTooTall { get; private set; }
        /// <summary>Stair chains by how many levels they climb (index = levels, up to the style's 8-level cap).</summary>
        public int[] ChainsByLevels { get; } = new int[9];

        /// <summary>World-space footprint of every piece laid, for keeping decorations off the streets and walls.</summary>
        public IReadOnlyList<SettlementFootprint> Laid => laid;

        /// <param name="ignoreUnder">Looked through when a piece samples the ground (the settlement's own output).</param>
        public SettlementStreetPaver(SettlementStreetStyle style, Transform parent, Transform ignoreUnder)
        {
            this.style = style;
            this.parent = parent;
            this.ignoreUnder = ignoreUnder;
        }

        /// <summary>A slab's size (x = across the street, y = along it): the widest and deepest of the style's slabs.</summary>
        public static Vector2 SlabSize(SettlementStreetStyle style)
        {
            Vector2 size = Vector2.zero;
            foreach (var piece in style.slabs)
            {
                if (piece.prefab == null) continue;
                Rect r = SettlementPlacementUtil.MeasureFootprint(piece.prefab, NoFootprint);
                size = Vector2.Max(size, r.size);
            }
            return size;
        }

        /// <summary>Paved width of a street <paramref name="slabsAcross"/> slabs wide.</summary>
        public static float PavedWidth(SettlementStreetStyle style, int slabsAcross) =>
            slabsAcross * SlabSize(style).x + (slabsAcross - 1) * style.slabSideGap;

        /// <summary>A road tile's size (x = across the street, y = along it): the first of the style's road tiles.</summary>
        public static Vector2 TileSize(SettlementStreetStyle style)
        {
            foreach (var piece in style.road.tiles)
            {
                if (piece.prefab != null) return SettlementPlacementUtil.MeasureFootprint(piece.prefab, NoFootprint).size;
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Half the corridor a street of <paramref name="order"/> needs, verges excluded: the widest surface it
        /// can get (road on the main street only, slabs on main and side streets, stone path anywhere).
        /// </summary>
        public static float PavedHalfWidth(SettlementStreetStyle style, int order)
        {
            if (!style.IsTileMode) return PavedWidth(style, SlabsAcross(style, order)) * 0.5f;
            float half = PavedHalfWidth(style, order, SettlementStreetStyle.StreetSurface.StonePath);
            if (order <= SettlementStreetNetwork.SideStreet)
                half = Mathf.Max(half, PavedHalfWidth(style, order, SettlementStreetStyle.StreetSurface.Slabs));
            if (order == SettlementStreetNetwork.MainStreet)
                half = Mathf.Max(half, PavedHalfWidth(style, order, SettlementStreetStyle.StreetSurface.Road));
            return half;
        }

        /// <summary>Half the width a street of <paramref name="order"/> is paved or scattered across where it has <paramref name="surface"/>.</summary>
        public static float PavedHalfWidth(SettlementStreetStyle style, int order, SettlementStreetStyle.StreetSurface surface) => surface switch
        {
            SettlementStreetStyle.StreetSurface.Road => TileSize(style).x * 0.5f,
            SettlementStreetStyle.StreetSurface.Slabs => PavedWidth(style, SlabsAcross(style, order)) * 0.5f,
            _ => style.stonePathWidth * 0.5f,
        };

        /// <summary>How deep the style's terrace walls reach back from their face -- the band the sculptor keeps low behind a contour.</summary>
        public static float WallDepth(SettlementStreetStyle style)
        {
            float depth = 0f;
            foreach (var piece in style.terraceWalls)
            {
                if (piece.prefab != null) depth = Mathf.Max(depth, -SettlementPlacementUtil.MeasureFootprint(piece.prefab, NoFootprint).yMin);
            }
            return depth;
        }

        public static int SlabsAcross(SettlementStreetStyle style, int order) => order switch
        {
            SettlementStreetNetwork.MainStreet => style.mainStreetSlabs,
            SettlementStreetNetwork.SideStreet => style.sideStreetSlabs,
            _ => style.alleySlabs,
        };

        // ---------------------------------------------------------------- stairs

        /// <summary>Street kit: one flight per level change, stretched until its foot meets the ground.</summary>
        public void LayStairs(SettlementStreetNetwork network, List<SettlementTerraceField.StreetProfile> profiles, SettlementTerraceField field)
        {
            for (int s = 0; s < network.streets.Count; s++)
            {
                var street = network.streets[s];
                GameObject prefab = street.order == SettlementStreetNetwork.MainStreet
                    ? style.wideStairs ? style.wideStairs : style.narrowStairs
                    : style.narrowStairs ? style.narrowStairs : style.wideStairs;
                if (prefab == null) return;

                foreach (var step in profiles[s].Steps())
                {
                    Vector2 uphill = street.TangentAt(step.arc) * step.Rise;   // +1 or -1: a street-kit profile never steps further
                    Vector2 top = street.PointAt(step.arc) + uphill * SettlementStairChain.StairTuck;
                    float topY = field.HeightOf(Mathf.Max(step.fromLevel, step.toLevel));
                    Quaternion yaw = Yaw(-uphill);

                    // The flight is as long as its slope; stretch it, length and rise together, until its
                    // foot meets the ground there.
                    Vector2 foot = top - uphill * Measure(prefab).yMax;
                    float fit = SampleGroundY(foot, out float footY)
                        ? Mathf.Clamp((topY - footY) / style.stepHeight, MinFlightFit, MaxFlightFit)
                        : 1f;
                    Place(prefab, new Vector3(top.x, topY, top.y), yaw, new Vector3(1f, fit, fit));
                    StairsLaid++;
                }
            }
        }

        /// <summary>
        /// Terrace kit: every chain's flights and landings, top first, plus a pillar at each top corner
        /// where the cheeks meet the upper terrace -- it frames the stair head and hides the cheek's end.
        /// </summary>
        public void LayStairChains(List<SettlementStairChain.Placement> chains, List<SettlementFootprint> buildings)
        {
            foreach (var chain in chains)
            {
                Quaternion yaw = Yaw(chain.forward);
                foreach (var piece in SettlementStairChain.Pieces(style, chain.levels))
                {
                    GameObject prefab = piece.isLanding ? style.landing : chain.flight;
                    if (prefab == null) continue;
                    Vector2 at = chain.top + chain.forward * piece.along;
                    Place(prefab, new Vector3(at.x, chain.topY - piece.drop, at.y), yaw, Vector3.one);
                    if (piece.isLanding) LandingsLaid++;
                    else StairsLaid++;
                }
                ChainsByLevels[Mathf.Min(chain.levels, ChainsByLevels.Length - 1)]++;

                if (style.wallPillar == null) continue;
                Vector2 right = new Vector2(chain.forward.y, -chain.forward.x);
                float halfFlight = Measure(chain.flight).width * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                    TryPillar(chain.top + right * (side * halfFlight), chain.topY, yaw, buildings);
            }
        }

        // ---------------------------------------------------------------- walls

        /// <summary>
        /// A single line of wall blocks along every step between two buildings on different levels,
        /// facing down the slope. Street kit: one block every block-length, stretched to reach the ground
        /// below. Terrace kit: blocks chord to chord so their front corners meet, each a top piece over as
        /// many courses as the drop needs, with pillars at bends, every few blocks and where a run ends.
        /// None where a building stands, and none beyond <paramref name="radius"/> of <paramref name="center"/>.
        /// </summary>
        public void LayWalls(SettlementTerraceField field, List<SettlementFootprint> buildings,
                             Vector2 center, float radius, ref SettlementPlacementUtil.SeededRng rng)
        {
            float reach = radius - WallDepth(style);
            float blockLength = 0f;
            foreach (var piece in style.terraceWalls)
                if (piece.prefab != null) blockLength = Mathf.Max(blockLength, Measure(piece.prefab).width);
            if (blockLength <= 0f) return;

            foreach (var contour in field.Contours())
            {
                // Pull the cell staircase straight, then round what corners remain.
                List<Vector2> line = SettlementPolyline.Smooth(SettlementPolyline.Simplify(contour.points, style.wallStraightening), ContourSmoothing);
                if (style.StacksWalls) LayStackedContour(line, contour.upperLevel, blockLength, field, buildings, center, reach, ref rng);
                else LayStretchedContour(line, contour.upperLevel, blockLength + WallJoint, field, buildings, center, reach, ref rng);
            }
        }

        private void LayStretchedContour(List<Vector2> line, int upperLevel, float pitch, SettlementTerraceField field,
                                         List<SettlementFootprint> buildings, Vector2 center, float reach,
                                         ref SettlementPlacementUtil.SeededRng rng)
        {
            float length = SettlementPolyline.Length(line);
            for (float s = pitch * 0.5f; s < length; s += pitch)
            {
                Vector2 point = SettlementPolyline.PointAt(line, s);
                Vector2 tangent = SettlementPolyline.TangentAt(line, s, pitch);
                if (!WallFits(field, upperLevel, point, tangent, center, reach, out Vector2 downhill, out float topY)) continue;

                GameObject prefab = Pick(style.terraceWalls, ref rng);
                if (prefab == null) continue;
                Quaternion yaw = Yaw(downhill);
                if (IntoAny(FootprintAt(prefab, point, yaw, Vector3.one), buildings)) continue;

                float drop = DropInFront(point, downhill, topY);
                Place(prefab, new Vector3(point.x, topY, point.y), yaw, new Vector3(1f, Mathf.Max(1f, drop / style.stepHeight), 1f));
                WallsLaid++;
            }
        }

        private struct WallBlock
        {
            public Vector2 from, to, downhill;
            public float topY;
            public GameObject top;
        }

        private void LayStackedContour(List<Vector2> line, int upperLevel, float blockLength, SettlementTerraceField field,
                                       List<SettlementFootprint> buildings, Vector2 center, float reach,
                                       ref SettlementPlacementUtil.SeededRng rng)
        {
            var run = new List<WallBlock>();
            float s = 0f;
            while (SettlementPolyline.NextChord(line, s, blockLength, out float next))
            {
                Vector2 from = SettlementPolyline.PointAt(line, s), to = SettlementPolyline.PointAt(line, next);
                s = next;
                Vector2 point = (from + to) * 0.5f;
                GameObject top = null;
                bool fits = WallFits(field, upperLevel, point, (to - from).normalized, center, reach, out Vector2 downhill, out float topY)
                            && (top = Pick(style.terraceWalls, ref rng)) != null
                            && !IntoAny(FootprintAt(top, point, Yaw(downhill), Vector3.one), buildings);
                if (fits) run.Add(new WallBlock { from = from, to = to, downhill = downhill, topY = topY, top = top });
                else LayWallRun(run, buildings, ref rng);
            }
            LayWallRun(run, buildings, ref rng);
        }

        // Lays one unbroken run of wall blocks and its pillars, then empties it.
        private void LayWallRun(List<WallBlock> run, List<SettlementFootprint> buildings, ref SettlementPlacementUtil.SeededRng rng)
        {
            if (run.Count == 0) return;
            var yaws = new List<float>(run.Count);
            for (int i = 0; i < run.Count; i++)
            {
                WallBlock block = run[i];
                Quaternion yaw = Yaw(block.downhill);
                yaws.Add(yaw.eulerAngles.y);
                Vector2 point = (block.from + block.to) * 0.5f;
                var position = new Vector3(point.x, block.topY, point.y);

                GameObject top = Place(block.top, position, yaw, Vector3.one);
                if (style.mirrorAlternateWallTops && i % 2 == 1) MirrorModel(top);
                WallsLaid++;

                int courses = SettlementWallStack.CourseCount(DropInFront(point, block.downhill, block.topY), style.stepHeight,
                                                              style.maxWallCourses, out bool tooTall);
                if (tooTall) WallsTooTall++;
                for (int k = 1; k <= courses; k++)
                {
                    GameObject course = Pick(style.wallCourses, ref rng);
                    if (course == null) continue;
                    Place(course, position + yaw * new Vector3(0f, -k * style.stepHeight, k * style.courseBatter), yaw, Vector3.one);
                    CoursesLaid++;
                }
            }

            if (style.wallPillar != null)
            {
                foreach (int joint in SettlementWallStack.PillarJoints(yaws, style.pillarEvery, style.pillarTurn))
                {
                    WallBlock before = run[Mathf.Max(0, joint - 1)], after = run[Mathf.Min(run.Count - 1, joint)];
                    Vector2 at = joint < run.Count ? after.from : before.to;
                    TryPillar(at, Mathf.Max(before.topY, after.topY), Yaw(before.downhill + after.downhill), buildings);
                }
            }
            run.Clear();
        }

        /// <summary>
        /// Whether a wall belongs at <paramref name="point"/> on a contour below <paramref name="upperLevel"/>:
        /// only the top contour of a multi-level step builds (it builds the whole drop), and only where a
        /// building stands on each side. Gives the downhill direction and the upper terrace's height.
        /// </summary>
        private bool WallFits(SettlementTerraceField field, int upperLevel, Vector2 point, Vector2 tangent, Vector2 center, float reach,
                              out Vector2 downhill, out float topY)
        {
            downhill = default;
            topY = 0f;
            if (Vector2.Distance(point, center) > reach) return false;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 ahead = point + normal * style.fieldCellSize, behind = point - normal * style.fieldCellSize;
            int aheadLevel = field.LevelAt(ahead), behindLevel = field.LevelAt(behind);
            // Equal levels are a pinch where the contour doubles back.
            if (Mathf.Max(aheadLevel, behindLevel) != upperLevel || aheadLevel == behindLevel) return false;
            bool aheadIsLower = aheadLevel < behindLevel;
            if (!field.WallBetween(aheadIsLower ? behind : ahead, aheadIsLower ? ahead : behind, style.wallReach)) return false;
            downhill = aheadIsLower ? normal : -normal;
            topY = field.TerraceHeightAt(aheadIsLower ? behind : ahead);
            return true;
        }

        private float DropInFront(Vector2 point, Vector2 downhill, float topY) =>
            SampleGroundY(point + downhill * WallGroundProbe, out float groundY) ? topY - groundY : style.stepHeight;

        private void TryPillar(Vector2 at, float topY, Quaternion yaw, List<SettlementFootprint> buildings)
        {
            if (IntoAny(FootprintAt(style.wallPillar, at, yaw, Vector3.one), buildings)) return;
            Place(style.wallPillar, new Vector3(at.x, topY, at.y), yaw, Vector3.one);
            PillarsLaid++;
        }

        // Mirrors the piece's model, not its root, so its box colliders keep a positive scale.
        private static void MirrorModel(GameObject piece)
        {
            Transform model = piece.GetComponentInChildren<MeshRenderer>().transform;
            Vector3 scale = model.localScale;
            model.localScale = new Vector3(-scale.x, scale.y, scale.z);
        }

        // ---------------------------------------------------------------- street kit slabs

        /// <summary>Street kit: rows of slabs along every street, as many across as its order calls for.</summary>
        public void LayStreets(SettlementStreetNetwork network, ref SettlementPlacementUtil.SeededRng rng)
        {
            foreach (var street in network.streets)
                LaySlabRows(street.points, 0f, street.Length, SlabsAcross(style, street.order), ref rng);
        }

        // Rows of `across` slabs, a slab length + slabGap apart, along `line` from `from` to `to`.
        private void LaySlabRows(List<Vector2> line, float from, float to, int across, ref SettlementPlacementUtil.SeededRng rng)
        {
            Vector2 slab = SlabSize(style);
            if (slab.y <= 0f) return;
            float pitch = slab.y + style.slabGap;
            for (float s = from + pitch * 0.5f; s < to; s += pitch)
            {
                Vector2 tangent = SettlementPolyline.TangentAt(line, s, SlabTangentWindow);
                Vector2 side = new Vector2(tangent.y, -tangent.x);
                for (int i = 0; i < across; i++)
                {
                    float offset = (i - (across - 1) * 0.5f) * (slab.x + style.slabSideGap);
                    LaySlab(SettlementPolyline.PointAt(line, s) + side * offset, tangent, ref rng);
                }
            }
        }

        /// <summary>From <paramref name="door"/> straight out for <paramref name="length"/> metres: slabs, or the terrace kit's door surface.</summary>
        public void LayDoorStub(Vector2 door, Vector2 outward, float length, ref SettlementPlacementUtil.SeededRng rng)
        {
            if (length <= 0f) return;
            var line = new List<Vector2> { door, door + outward * length };
            if (!style.IsTileMode)
            {
                LaySlabRows(line, 0f, length - SlabSize(style).y * 0.5f, 1, ref rng);
                return;
            }
            switch (style.doorSurface)
            {
                case SettlementStreetStyle.StreetSurface.Road: LayTileRun(line, 0f, length, null, null, ref rng); break;
                case SettlementStreetStyle.StreetSurface.Slabs: LaySlabRows(line, 0f, length, 1, ref rng); break;
                default: LayStones(line, new List<Vector2>(), style.doorPathWidth * 0.5f, fadeAtEnd: false, ref rng); break;
            }
        }

        private void LaySlab(Vector2 xz, Vector2 along, ref SettlementPlacementUtil.SeededRng rng)
        {
            GameObject prefab = Pick(style.slabs, ref rng);
            if (prefab == null) return;
            Quaternion yaw = Yaw(along);
            SettlementFootprint footprint = FootprintAt(prefab, xz, yaw, Vector3.one);
            if (OverlapsLaid(footprint, -AllowedOverlap)) return;
            if (!SampleTiltedGround(xz, out float groundY, out Quaternion tilt)) return;

            SettlementPlacementUtil.SpawnPrefab(prefab, parent, new Vector3(xz.x, groundY, xz.y), tilt * yaw);
            laid.Add(footprint);
            SlabsLaid++;
        }

        // ---------------------------------------------------------------- terrace kit streets

        /// <summary>
        /// Terrace kit, each street stretch by its surface (<paramref name="sections"/>). Road first -- a
        /// node wherever a road meets a road or a slab street, at its sharp bends and at the centre where
        /// the main street's halves meet; continuous tiles between; an end piece wherever a road stretch
        /// stops without a node (a dead end, or the street carrying on as slabs or path) -- then the slab
        /// stretches, then stone paths, each keeping off what is already laid.
        /// </summary>
        public void LayTerraceStreets(SettlementStreetNetwork network, List<SettlementTerraceField.StreetProfile> profiles,
                                      SettlementTerraceField field, List<SettlementStairChain.Placement> chains,
                                      List<SettlementStreetSurfaces.Section>[] sections, ref SettlementPlacementUtil.SeededRng rng)
        {
            int count = network.streets.Count;
            var lines = new List<List<Vector2>>(count);
            var lengths = new float[count];
            var blocked = new List<List<Vector2>>(count);
            for (int s = 0; s < count; s++)
            {
                var street = network.streets[s];
                List<Vector2> line = SettlementPolyline.Smooth(new List<Vector2>(street.points), StreetSmoothing);
                lines.Add(line);
                lengths[s] = SettlementPolyline.Length(line);
                var intervals = new List<Vector2>();
                foreach (var chain in chains)
                {
                    if (chain.street != s) continue;
                    intervals.Add(new Vector2(SettlementPolyline.Project(line, street.PointAt(Mathf.Max(0f, chain.occupied.x))),
                                              SettlementPolyline.Project(line, street.PointAt(Mathf.Min(street.Length, chain.occupied.y)))));
                }
                blocked.Add(intervals);
            }

            // The surface of street s where it passes `at`.
            SettlementStreetStyle.StreetSurface SurfaceAt(int s, Vector2 at) =>
                SettlementStreetSurfaces.At(sections[s], network.streets[s].Project(at, out _));
            bool IsRoad(SettlementStreetStyle.StreetSurface surface) => surface == SettlementStreetStyle.StreetSurface.Road;
            bool IsPaved(SettlementStreetStyle.StreetSurface surface) => surface != SettlementStreetStyle.StreetSurface.StonePath;

            // Where each street starts: on its parent, at the centre against the main street's other half,
            // or -- a main street that only grew one way -- at a dead end.
            var startsOn = new int[count];
            for (int s = 0; s < count; s++)
            {
                startsOn[s] = network.streets[s].parent;
                for (int t = 0; t < count && startsOn[s] < 0; t++)
                    if (t != s && network.streets[s].parent < 0 && network.streets[t].parent < 0) startsOn[s] = t;
            }

            // Every node's place first: a junction blocks the tiles of both streets through it, the older one included.
            float nodeRadius = style.road.node ? Measure(style.road.node).width * 0.5f * style.nodeScale - style.nodeClearance : 0f;
            var nodeArcs = new List<float>[count];
            for (int s = 0; s < count; s++) nodeArcs[s] = new List<float>();
            for (int s = 0; s < count && nodeRadius > 0f; s++)
            {
                var street = network.streets[s];
                Vector2 start = street.points[0], end = street.points[street.points.Count - 1];
                foreach (var (at, arc, other) in new[] { (start, 0f, startsOn[s]), (end, lengths[s], street.endsOn) })
                {
                    if (other < 0) continue;
                    var here = SurfaceAt(s, at);
                    var there = SurfaceAt(other, at);
                    if (!(IsRoad(here) && IsPaved(there)) && !(IsRoad(there) && IsPaved(here))) continue;
                    AddNode(s, arc, at, other, nodeRadius, lines, blocked, nodeArcs, profiles, field, network);
                }
                for (int i = 1; i + 1 < street.points.Count; i++)
                {
                    Vector2 a = street.points[i] - street.points[i - 1], b = street.points[i + 1] - street.points[i];
                    if (Vector2.Angle(a, b) <= style.nodeTurn || !IsRoad(SurfaceAt(s, street.points[i]))) continue;
                    AddNode(s, SettlementPolyline.Project(lines[s], street.points[i]), street.points[i], -1, nodeRadius, lines, blocked, nodeArcs, profiles, field, network);
                }
            }

            float endLength = style.road.end ? Measure(style.road.end).height : 0f;
            for (int s = 0; s < count; s++)
            {
                var line = lines[s];
                float length = lengths[s];
                var profile = profiles[s];
                foreach (var section in sections[s])
                {
                    if (!IsRoad(section.surface)) continue;
                    float from = SettlementPolyline.Project(line, network.streets[s].PointAt(section.from));
                    float to = SettlementPolyline.Project(line, network.streets[s].PointAt(section.to));
                    // A road stretch ends in a frayed end piece wherever no node takes it.
                    bool endAtStart = endLength > 0f && !NodeAt(nodeArcs[s], from, nodeRadius);
                    bool endAtEnd = endLength > 0f && !NodeAt(nodeArcs[s], to, nodeRadius);
                    var runBlocked = new List<Vector2>(blocked[s]) { new(-1f, from), new(to, length + 1f) };
                    if (endAtStart) runBlocked.Add(new Vector2(from, from + endLength));
                    if (endAtEnd) runBlocked.Add(new Vector2(to - endLength, to));
                    foreach (Vector2 run in SettlementRoadTiles.FreeRuns(length, runBlocked))
                    {
                        if (!LayTileRun(line, run.x, run.y, profile, field, ref rng)) continue;
                        if (endAtStart && Mathf.Approximately(run.x, from + endLength)) LayEnd(line, run.x, -1f, profile, field);
                        if (endAtEnd && Mathf.Approximately(run.y, to - endLength)) LayEnd(line, run.y, 1f, profile, field);
                    }
                }
            }

            // Nodes after the tiles: a node is round but its footprint is square, and its corners would
            // reject the tiles that run up to its rim at an angle.
            foreach (var node in nodes)
            {
                Place(style.road.node, node, Quaternion.identity, Vector3.one * style.nodeScale);
                NodesLaid++;
            }

            for (int s = 0; s < count; s++)
            {
                foreach (var section in sections[s])
                {
                    if (section.surface != SettlementStreetStyle.StreetSurface.Slabs) continue;
                    LaySlabRows(network.streets[s].points, section.from, section.to, SlabsAcross(style, network.streets[s].order), ref rng);
                }
            }

            for (int s = 0; s < count; s++)
            {
                var pathBlocked = new List<Vector2>(blocked[s]);
                bool any = false;
                foreach (var section in sections[s])
                {
                    if (section.surface == SettlementStreetStyle.StreetSurface.StonePath) any = true;
                    else pathBlocked.Add(new Vector2(SettlementPolyline.Project(lines[s], network.streets[s].PointAt(section.from)),
                                                     SettlementPolyline.Project(lines[s], network.streets[s].PointAt(section.to))));
                }
                if (!any) continue;
                bool deadEnd = network.streets[s].endsOn < 0 && sections[s][sections[s].Count - 1].surface == SettlementStreetStyle.StreetSurface.StonePath;
                LayStones(lines[s], pathBlocked, style.stonePathWidth * 0.5f, deadEnd, ref rng);
            }
        }

        private static bool NodeAt(List<float> arcs, float arc, float clearance)
        {
            foreach (float node in arcs)
            {
                if (Mathf.Abs(node - arc) <= clearance + 0.01f) return true;
            }
            return false;
        }

        // A node at a junction or bend of street s (and of `other`, the street it meets, if any): laid once
        // however many streets meet there, and every street through it keeps its tiles out of its rim.
        private void AddNode(int s, float arc, Vector2 at, int other, float clearance, List<List<Vector2>> lines,
                             List<List<Vector2>> blocked, List<float>[] nodeArcs,
                             List<SettlementTerraceField.StreetProfile> profiles, SettlementTerraceField field, SettlementStreetNetwork network)
        {
            blocked[s].Add(new Vector2(arc - clearance, arc + clearance));
            nodeArcs[s].Add(arc);
            if (other >= 0)
            {
                float otherArc = SettlementPolyline.Project(lines[other], at);
                blocked[other].Add(new Vector2(otherArc - clearance, otherArc + clearance));
                nodeArcs[other].Add(otherArc);
            }
            foreach (var node in nodes)
            {
                if (Vector2.Distance(new Vector2(node.x, node.z), at) < clearance) return;
            }
            nodes.Add(new Vector3(at.x, field.HeightOf(profiles[s].LevelAt(network.streets[s].Project(at, out _))), at.y));
        }

        /// <summary>
        /// Tiles from <paramref name="from"/> to <paramref name="to"/> along <paramref name="line"/>, stretched
        /// evenly to fill it exactly, each yawed to the line at its centre. At the street's level when a
        /// profile is given, else on the ground. False when the run is left bare.
        /// </summary>
        private bool LayTileRun(List<Vector2> line, float from, float to, SettlementTerraceField.StreetProfile profile,
                                SettlementTerraceField field, ref SettlementPlacementUtil.SeededRng rng)
        {
            float tileLength = TileSize(style).y;
            if (tileLength <= 0f || !SettlementRoadTiles.FitRun(to - from, tileLength, style.tileFitRange, out int count, out float scale)) return false;

            // Tiles of one run abut by design; they are only tested against what was laid before it.
            int before = laid.Count;
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                float s = from + (i + 0.5f) * tileLength * scale;
                GameObject prefab = Pick(style.road.tiles, ref rng);
                if (prefab == null) continue;
                Vector2 point = SettlementPolyline.PointAt(line, s);
                Quaternion yaw = Yaw(SettlementPolyline.TangentAt(line, s, tileLength));
                var stretch = new Vector3(1f, 1f, scale);
                if (OverlapsLaid(FootprintAt(prefab, point, yaw, stretch), -AllowedOverlap, before)) continue;

                float y;
                if (profile != null) y = field.HeightOf(profile.LevelAt(s));
                else if (!SampleGroundY(point, out y)) continue;
                // Where a curve makes neighbouring tiles overlap, every other one sits a hair higher so their tops never share a plane.
                if (i % 2 == 1) y += style.tileLift;
                Place(prefab, new Vector3(point.x, y, point.y), yaw, stretch);
                TilesLaid++;
                placed++;
            }
            return placed > 0;
        }

        // An end piece on the edge of the run's last tile at `arc`, reaching out of the run (`outward` +1 along the line, -1 back).
        private void LayEnd(List<Vector2> line, float arc, float outward, SettlementTerraceField.StreetProfile profile, SettlementTerraceField field)
        {
            Vector2 point = SettlementPolyline.PointAt(line, arc);
            Vector2 forward = SettlementPolyline.TangentAt(line, arc, TileSize(style).y) * outward;
            Place(style.road.end, new Vector3(point.x, field.HeightOf(profile.LevelAt(arc)), point.y), Yaw(forward), Vector3.one);
            EndsLaid++;
        }

        private void LayStones(List<Vector2> line, List<Vector2> blocked, float halfWidth, bool fadeAtEnd, ref SettlementPlacementUtil.SeededRng rng)
        {
            var settings = style.stones;
            var footprints = new Rect[settings.pieces.Length];
            for (int i = 0; i < footprints.Length; i++)
                footprints[i] = settings.pieces[i].prefab ? Measure(settings.pieces[i].prefab) : default;

            var stones = SettlementStonePath.Scatter(line, blocked, halfWidth, fadeAtEnd, settings, footprints,
                                                        footprint => !OverlapsLaid(footprint, settings.minGap), ref rng);
            foreach (var stone in stones)
            {
                if (!SampleTiltedGround(stone.position, out float groundY, out Quaternion tilt)) continue;
                GameObject piece = SettlementPlacementUtil.SpawnPrefab(settings.pieces[stone.piece].prefab, parent,
                                                                       new Vector3(stone.position.x, groundY - stone.sink, stone.position.y),
                                                                       tilt * Quaternion.Euler(0f, stone.yaw, 0f));
                piece.transform.localScale *= stone.scale;
                laid.Add(stone.footprint);
                StonesLaid++;
            }
        }

        // ---------------------------------------------------------------- shared

        private GameObject Place(GameObject prefab, Vector3 position, Quaternion yaw, Vector3 scale)
        {
            GameObject piece = SettlementPlacementUtil.SpawnPrefab(prefab, parent, position, yaw);
            piece.transform.localScale = Vector3.Scale(piece.transform.localScale, scale);
            laid.Add(FootprintAt(prefab, new Vector2(position.x, position.z), yaw, scale));
            return piece;
        }

        private SettlementFootprint FootprintAt(GameObject prefab, Vector2 xz, Quaternion yaw, Vector3 scale)
        {
            Rect local = Measure(prefab);
            Vector2 center = new Vector2(local.center.x * scale.x, local.center.y * scale.z);
            Vector3 offset = yaw * new Vector3(center.x, 0f, center.y);
            return new SettlementFootprint(xz + new Vector2(offset.x, offset.z), new Vector2(local.size.x * scale.x, local.size.y * scale.z), yaw);
        }

        /// <summary>Terrain height at <paramref name="xz"/>, looking through the settlement's own output.</summary>
        private bool SampleGroundY(Vector2 xz, out float groundY) =>
            SettlementPlacementUtil.SampleGround(new Vector3(xz.x, parent.position.y, xz.y), out groundY, ignoreUnder: ignoreUnder);

        /// <summary>Terrain height at <paramref name="xz"/> and the tilt of the ground there, no steeper than <c>maxTilt</c>.</summary>
        private bool SampleTiltedGround(Vector2 xz, out float groundY, out Quaternion tilt)
        {
            tilt = Quaternion.identity;
            var probe = new Vector3(xz.x, parent.position.y, xz.y);
            if (!SettlementPlacementUtil.SampleGround(probe, out groundY, out Vector3 normal, ignoreUnder: ignoreUnder)) return false;
            float angle = Vector3.Angle(Vector3.up, normal);
            if (angle > style.maxTilt) normal = Vector3.Slerp(Vector3.up, normal, style.maxTilt / angle);
            tilt = Quaternion.FromToRotation(Vector3.up, normal);
            return true;
        }

        /// <summary>Whether <paramref name="footprint"/> comes closer than <paramref name="minGap"/> to any of the first <paramref name="upTo"/> pieces laid (negative = may overlap that much).</summary>
        private bool OverlapsLaid(in SettlementFootprint footprint, float minGap, int upTo = int.MaxValue)
        {
            int count = Mathf.Min(upTo, laid.Count);
            for (int i = 0; i < count; i++)
            {
                var other = laid[i];
                float reach = footprint.Circumradius + other.Circumradius + Mathf.Max(0f, minGap);
                if ((footprint.center - other.center).sqrMagnitude > reach * reach) continue;
                if (SettlementFootprint.Gap(footprint, other) < minGap) return true;
            }
            return false;
        }

        private static bool IntoAny(in SettlementFootprint footprint, List<SettlementFootprint> buildings)
        {
            foreach (var building in buildings)
            {
                float reach = footprint.Circumradius + building.Circumradius;
                if ((footprint.center - building.center).sqrMagnitude > reach * reach) continue;
                if (SettlementFootprint.Gap(footprint, building) < -WallIntoBuilding) return true;
            }
            return false;
        }

        private Rect Measure(GameObject prefab)
        {
            if (!measured.TryGetValue(prefab, out Rect rect))
                measured[prefab] = rect = SettlementPlacementUtil.MeasureFootprint(prefab, NoFootprint);
            return rect;
        }

        private static Quaternion Yaw(Vector2 forward) =>
            Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.y), Vector3.up);

        private static GameObject Pick(SettlementStreetStyle.WeightedPiece[] set, ref SettlementPlacementUtil.SeededRng rng)
        {
            int index = SettlementStreetStyle.WeightedPiece.PickIndex(set, ref rng);
            return index < 0 ? null : set[index].prefab;
        }
    }
}
