// How a settlement is laid out when it is planned rather than grown as a cluster: the rules its street
// network grows by, how wide each kind of street is, how the ground is cut into terraces, and the
// pieces that pave it. Optional -- a SettlementConfig without one keeps the cluster layout. Every
// piece is a plain scenery prefab measured by its mesh; see SettlementStreetPaver for where each
// piece's origin must sit. Distances in metres.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    [CreateAssetMenu(fileName = "SettlementStreetStyle", menuName = "Settlement/Street Style")]
    public class SettlementStreetStyle : ScriptableObject
    {
        // A class, for the same reason as SettlementConfig.SpawnEntry: a field added later loads as
        // its initializer instead of 0.
        [Serializable]
        public class WeightedPiece
        {
            public GameObject prefab;
            [Tooltip("Relative odds of this piece among its list. 0 = never.")]
            [Min(0f)] public float weight = 1f;

            /// <summary>Index of a weighted random piece of <paramref name="set"/>, -1 if none can be picked. One draw.</summary>
            public static int PickIndex(WeightedPiece[] set, ref SettlementPlacementUtil.SeededRng rng)
            {
                float total = 0f;
                foreach (var piece in set)
                {
                    if (piece.prefab != null) total += piece.weight;
                }
                if (total <= 0f) return -1;

                float roll = rng.NextFloat01() * total;
                int last = -1;
                for (int i = 0; i < set.Length; i++)
                {
                    if (set[i].prefab == null || set[i].weight <= 0f) continue;
                    last = i;
                    roll -= set[i].weight;
                    if (roll < 0f) return i;
                }
                return last;
            }
        }

        [Header("Street growth")]
        [Tooltip("Length of one grown street segment. Streets bend only between segments.")]
        [Min(2f)] public float segmentLength = 10f;
        [Tooltip("Most a street turns from one segment to the next, in degrees.")]
        [Range(0f, 30f)] public float maxTurn = 6f;
        [Tooltip("Chance a segment takes the flattest of its possible bends instead of a random one. Higher = streets follow the hillside, fewer stairs.")]
        [Range(0f, 1f)] public float contourBias = 0.5f;
        [Tooltip("Chance the main street sends a side street off at a segment end, once branchSpacing allows it.")]
        [Range(0f, 1f)] public float sideStreetChance = 0.8f;
        [Tooltip("Chance a side street sends an alley off at a segment end, once branchSpacing allows it. Alleys never branch.")]
        [Range(0f, 1f)] public float alleyChance = 0.5f;
        [Tooltip("Shortest distance along a street between two branches leaving the same side -- the depth of a block of houses.")]
        [Min(0f)] public float branchSpacing = 45f;
        [Tooltip("Random deviation from a right angle where a branch leaves its street, in degrees.")]
        [Range(0f, 30f)] public float branchAngleJitter = 8f;
        [Tooltip("A street ending this close to another joins it (a T junction) instead of running past.")]
        [Min(0f)] public float snapDistance = 6f;
        [Tooltip("Street frontage grown per metre of frontage the buildings need. Above 1 leaves gaps and corners room.")]
        [Min(1f)] public float frontageSlack = 1.3f;
        [Tooltip("Hard cap on grown segments, whatever the buildings still need.")]
        [Min(1)] public int maxSegments = 800;

        [Header("Street sections")]
        [Tooltip("Slabs laid side by side across the main street.")]
        [Min(1)] public int mainStreetSlabs = 2;
        [Tooltip("Slabs laid side by side across a side street.")]
        [Min(1)] public int sideStreetSlabs = 1;
        [Tooltip("Slabs laid side by side across an alley.")]
        [Min(1)] public int alleySlabs = 1;
        [Tooltip("Unpaved margin each side of the paving, inside the street.")]
        [Min(0f)] public float verge = 1f;
        [Tooltip("Distance from the street's edge to the front of the buildings lining it.")]
        [Min(0f)] public float setback = 0.5f;

        [Header("Plaza (every planned settlement is laid out round one: its town centre)")]
        [Tooltip("Share of the buildings, the largest first, that ring the plaza facing its centre; the rest line the lanes leaving it.")]
        [Range(0f, 1f)] public float plazaHouseShare = 0.6f;
        [Tooltip("Smallest distance from the plaza's centre to the fronts of the buildings round it.")]
        [Min(2f)] public float plazaMinRadius = 8f;
        [Tooltip("Largest the plaza grows to fit its ring. A building that would push it past this goes to a lane instead (at least three always ring it).")]
        [Min(2f)] public float plazaMaxRadius = 16f;
        [Tooltip("Fewest lanes leaving the plaza.")]
        [Min(1)] public int plazaMinLanes = 2;
        [Tooltip("Most lanes leaving the plaza.")]
        [Min(1)] public int plazaMaxLanes = 4;
        [Tooltip("Lane-side buildings per lane: more buildings left after the ring means more lanes, between the min and max.")]
        [Min(1)] public int plazaHousesPerLane = 3;

        [Header("Density")]
        [Tooltip("Share of the ground inside the town's radius that its buildings cover. Sets the radius the density falls off over: lower = a more spread-out town.")]
        [Range(0.05f, 1f)] public float buildingCoverage = 0.35f;
        [Tooltip("Inner part of the town's radius where buildings stand at minBuildingGap and back rows are built. Outside it the gaps loosen toward maxBuildingGap at the rim.")]
        [Range(0f, 1f)] public float denseCore = 0.5f;
        [Tooltip("Chance, inside the dense core, that a gap in a street's row of houses is left as a passage with a house built behind it.")]
        [Range(0f, 1f)] public float backRowChance = 0.4f;
        [Tooltip("Width of the passage between two street-front houses that leads to a back-row house.")]
        [Min(0.5f)] public float passageWidth = 1.6f;
        [Tooltip("Gap between a street-front house and the back-row house behind it.")]
        [Min(0f)] public float backRowGap = 0.5f;
        [Tooltip("Street kept past its last house before the rest is cut away.")]
        [Min(0f)] public float trimMargin = 3f;

        [Header("Terraces")]
        [Tooltip("Height between two terrace levels. Must match the rise of the stair and terrace-wall prefabs.")]
        [Min(0.25f)] public float stepHeight = 1.5f;
        [Tooltip("Shortest stretch of street between two flights of stairs.")]
        [Min(1f)] public float minRunBetweenStairs = 9f;
        [Tooltip("Radius the natural ground is averaged over before a street's levels are read off it. Larger = fewer, calmer stairs; smaller = streets that hug every rise.")]
        [Min(0f)] public float groundSmoothing = 6f;
        [Tooltip("How far from a street or building the ground is reshaped before it fades back to natural. Larger = gentler banks around the settlement.")]
        [Min(1f)] public float gradeDistance = 10f;
        [Tooltip("A retaining wall stands between two buildings on different levels only when each is within this distance of the wall line.")]
        [Min(0f)] public float wallReach = 4f;
        [Tooltip("How far a terrace wall may be pulled straight across the level map's cells, in metres. Larger = longer straight runs of wall.")]
        [Min(0f)] public float wallStraightening = 0.75f;
        [Tooltip("Cell size of the terrace level map. Smaller follows the ground more closely and costs more.")]
        [Min(0.5f)] public float fieldCellSize = 1f;

        [Header("Pieces")]
        [Tooltip("Paving slabs: long side across the street (local X), short side along it (local Z), origin at the ground centre.")]
        public WeightedPiece[] slabs = Array.Empty<WeightedPiece>();
        [Tooltip("Gap between two rows of slabs along a street.")]
        [Min(0f)] public float slabGap = 0.25f;
        [Tooltip("Gap between two slabs side by side across a street.")]
        [Min(0f)] public float slabSideGap = 0.15f;
        [Tooltip("Stairs for streets one slab wide. Origin at the top of the flight where it meets the wall, descending along +Z.")]
        public GameObject narrowStairs;
        [Tooltip("Stairs for wider streets.")]
        public GameObject wideStairs;
        [Tooltip("Terrace wall blocks: origin at the top of the wall face, +Z pointing down the slope, deck reaching back along -Z.")]
        public WeightedPiece[] terraceWalls = Array.Empty<WeightedPiece>();
        [Tooltip("Steepest ground a slab or stepping stone tilts to follow, in degrees.")]
        [Range(0f, 45f)] public float maxTilt = 10f;

        // Values are serialized: append, never reorder.
        public enum StreetSurface { Road, StonePath, Slabs }

        /// <summary>Continuous road tiles. Tiles: origin at the ground centre, laid end to end along local Z, all the same length.</summary>
        [Serializable]
        public class RoadPieces
        {
            [Tooltip("Road tiles, all the same length along local Z. Never mirrored: their stones continue across the seam only unmirrored.")]
            public WeightedPiece[] tiles = Array.Empty<WeightedPiece>();
            [Tooltip("Round junction piece, origin at its ground centre. Laid at every road junction and sharp bend.")]
            public GameObject node;
            [Tooltip("Dead-end piece, origin on the edge that joins the last tile, reaching out along +Z.")]
            public GameObject end;
        }

        /// <summary>Loose stepping stones scattered down a street; the sand between them is the street.</summary>
        [Serializable]
        public class StoneScatter
        {
            [Tooltip("Stepping stones, origin at the ground centre.")]
            public WeightedPiece[] pieces = Array.Empty<WeightedPiece>();
            [Tooltip("Mean distance between stones along the path, centre to centre.")]
            [Min(0.1f)] public float spacing = 1.2f;
            [Tooltip("How far the spacing varies, as a fraction of it either way.")]
            [Range(0f, 0.9f)] public float spacingJitter = 0.25f;
            [Tooltip("Furthest a stone strays sideways from the centre line, as a fraction of the path's half-width.")]
            [Range(0f, 1f)] public float lateral = 0.35f;
            [Tooltip("Chance a second stone sits beside one, reading as a wider spot.")]
            [Range(0f, 1f)] public float pairChance = 0.15f;
            [Tooltip("How far a stone's size varies, as a fraction either way. The stones are not modular, so this is allowed.")]
            [Range(0f, 0.5f)] public float scaleJitter = 0.15f;
            [Tooltip("Most a stone is sunk below the ground on top of its own depth, so the tops are not all at one height.")]
            [Min(0f)] public float sinkMax = 0.03f;
            [Tooltip("Stones never lie closer together than this.")]
            [Min(0f)] public float minGap = 0.08f;
            [Tooltip("At a dead end this many last stones thin out and shrink.")]
            [Min(0)] public int fadeStones = 3;
            [Tooltip("Size of the very last stone at a dead end, as a fraction of a normal one.")]
            [Range(0.1f, 1f)] public float fadeScale = 0.6f;
            [Tooltip("Tries with a new sideways draw before a stone that clashes with something is left out.")]
            [Min(0)] public int retries = 2;
        }

        [Header("Terrace kit (optional; the street kit above is used where a list is empty)")]
        [Tooltip("Share of all street length paved as road: the main street's stretches nearest the centre. 0 = no road.")]
        [Range(0f, 1f)] public float roadShare = 0.15f;
        [Tooltip("Share of all street length laid with slabs (the street kit's slabs), after the road: main and side streets, nearest the centre first. The rest is stone path.")]
        [Range(0f, 1f)] public float slabShare = 0.25f;
        [Tooltip("A settlement with fewer buildings than this gets no road at all, only slabs and stone paths.")]
        [Min(0)] public int minBuildingsForRoad = 8;
        [Tooltip("When ranking street stretches for road and slabs, each street order down counts as this many metres further from the centre.")]
        [Min(0f)] public float orderRankDistance = 40f;
        [Tooltip("Surface of the path from a front door to its street.")]
        public StreetSurface doorSurface = StreetSurface.StonePath;
        [Tooltip("Width of a stone path from a front door to its street; narrow enough to pass between two houses.")]
        [Min(0.3f)] public float doorPathWidth = 1.2f;
        public RoadPieces road = new();
        public StoneScatter stones = new();
        [Tooltip("Width of a stone-path street's corridor. Keep it the narrow flight's walk so its stairs fit.")]
        [Min(0.5f)] public float stonePathWidth = 2.25f;
        [Tooltip("A road run's tiles are stretched along the street by at most this fraction to fill it exactly; a run that would need more is left bare.")]
        [Range(0f, 0.5f)] public float tileFitRange = 0.15f;
        [Tooltip("A road that bends more than this at one point (degrees) gets a node there.")]
        [Range(0f, 90f)] public float nodeTurn = 20f;
        [Tooltip("Size of a node against its prefab. Above 1 makes a junction read as a widening of the road.")]
        [Min(0.1f)] public float nodeScale = 1.3f;
        [Tooltip("How far inside a node's rim the road tiles stop.")]
        [Min(0f)] public float nodeClearance = 0.1f;
        [Tooltip("Every other road tile is raised this much, so where curved tiles overlap their coplanar tops do not flicker.")]
        [Min(0f)] public float tileLift = 0.003f;

        [Tooltip("Stair flight for roads and wide slab streets. Origin at the top nosing, descending along +Z; the next flight chains at (0, -stepHeight, flightRun). Stone paths never get stairs.")]
        public GameObject flightWide;
        [Tooltip("Stair flight for slab streets one slab wide.")]
        public GameObject flightNarrow;
        [Tooltip("Landing between flights. Origin at its back edge, where the flight above ends; the next flight chains at (0, 0, landingRun).")]
        public GameObject landing;
        [Tooltip("Horizontal distance from one flight's origin to the next one's. Must match the flight prefabs (TerraceKitAssetTests).")]
        [Min(0.5f)] public float flightRun = 2.7f;
        [Tooltip("Horizontal distance from a landing's origin to the flight below it. Must match the landing prefab (TerraceKitAssetTests).")]
        [Min(0.5f)] public float landingRun = 3.5f;
        [Tooltip("Most terraces one chain of flights climbs at once, on side streets and alleys. Each needs a flight's run on the lower side.")]
        [Range(1, 8)] public int maxStairLevels = 4;
        [Tooltip("Most terraces the main street's stairs climb at once. 1 keeps it winding up the hill a level at a time.")]
        [Range(1, 8)] public int mainStreetStairLevels = 1;
        [Tooltip("Flights between two landings on a long chain.")]
        [Min(1)] public int flightsPerLanding = 2;
        [Tooltip("How far under the stair's nosing line the ground is sculpted along a chain, so the stairs read as built into the hillside.")]
        [Min(0f)] public float stairGroundBelowNosing = 0.5f;

        [Tooltip("Wall courses stacked under a terrace wall's top piece (terraceWalls). Origin at the top of the face, one stepHeight tall.")]
        public WeightedPiece[] wallCourses = Array.Empty<WeightedPiece>();
        [Tooltip("Pier set at wall bends, every few blocks, at run ends and at the head of each stair chain. Origin at the top of the face.")]
        public GameObject wallPillar;
        [Tooltip("How much further downhill each course stands than the one above it. Must match the course prefab's set-forward.")]
        [Min(0f)] public float courseBatter = 0.1f;
        [Tooltip("Most courses under one top piece. The pillar must reach down past all of them (TerraceKitAssetTests).")]
        [Range(0, 8)] public int maxWallCourses = 3;
        [Tooltip("Wall blocks between two pillars on a straight run.")]
        [Min(1)] public int pillarEvery = 3;
        [Tooltip("Turn (degrees) a wall builds up since its last pillar before the next joint gets one.")]
        [Range(0f, 90f)] public float pillarTurn = 12f;
        [Tooltip("Mirror every other top piece, moving its patch and spout. Only for top pieces without stones across their ends.")]
        public bool mirrorAlternateWallTops = true;

        /// <summary>Whether streets are laid as terrace-kit roads and stone paths instead of slab rows.</summary>
        public bool IsTileMode => road.tiles.Length > 0 || stones.pieces.Length > 0;

        /// <summary>Whether a level change is a chain of flights (several levels, with landings) instead of one stretched flight.</summary>
        public bool ChainsStairs => flightWide != null || flightNarrow != null;

        /// <summary>Whether terrace walls are stacked from courses instead of one block stretched to the drop.</summary>
        public bool StacksWalls => wallCourses.Length > 0;

        /// <summary>Every prefab a street, a stair or a door path is laid with -- what is walked along -- as opposed to the walls.</summary>
        public IEnumerable<GameObject> SurfacePieces()
        {
            foreach (GameObject piece in PavedPieces()) yield return piece;
            foreach (WeightedPiece stone in stones.pieces)
                if (stone != null && stone.prefab != null) yield return stone.prefab;
        }

        /// <summary>The surface pieces that are built paving (road, slabs, stairs), as opposed to a stone path's stepping stones.</summary>
        public IEnumerable<GameObject> PavedPieces()
        {
            foreach (WeightedPiece[] set in new[] { slabs, road.tiles })
                foreach (WeightedPiece piece in set)
                    if (piece != null && piece.prefab != null) yield return piece.prefab;
            foreach (GameObject single in new[] { narrowStairs, wideStairs, road.node, road.end, flightWide, flightNarrow, landing })
                if (single != null) yield return single;
        }

        /// <summary>Most levels a street of <paramref name="order"/> climbs at one place.</summary>
        public int StairLevels(int order) =>
            !ChainsStairs ? 1 : order == SettlementStreetNetwork.MainStreet ? mainStreetStairLevels : maxStairLevels;
    }
}
