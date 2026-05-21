using UnityEngine;

/// <summary>
/// Designer-tunable settings for the <see cref="BadlandsMazeFeature"/> — a solid rock massif
/// eroded by a wide branching channel network into a walkable labyrinth of mesas, overhangs and
/// cliffs, scattered with boulders.
///
/// The feature is built by a multi-stage "careful planning" pipeline (channel graph → mesa &amp;
/// boulder placement → global SDF → internally-chunked meshing), exactly mirroring
/// <see cref="ArchingCaveSettings"/>. These knobs are the HIGH-LEVEL controls: how the channel
/// network branches, how tall the rock is, how much it overhangs, how craggy it erodes, and how
/// many boulders litter the floor — plus the per-section <b>feature toggles</b> the brief asked
/// for.
///
/// Every value drives deterministic math seeded off <see cref="FeatureContext.Seed"/> — nothing
/// here is mutable or frame-dependent. Same settings + seed + footprint = identical maze.
/// </summary>
[System.Serializable]
public class BadlandsMazeSettings
{
    // -------------------------------------------------------------------------
    // Channel network — the "river system" that carves the maze.
    // -------------------------------------------------------------------------

    [Header("Channel network")]
    [Tooltip("How many open chambers (junctions / pools) the channel graph lays across the footprint. More chambers = a longer, more sprawling maze. The random-walk planner connects them all into one walkable network.")]
    [Range(4, 30)] public int chamberCount = 12;

    [Tooltip("Spread of chamber radii. 0 = every pool the same size (systematic, discouraged); 1 = wildly varied — tiny side-pockets next to vast open basins.")]
    [Range(0f, 1f)] public float chamberSizeVariation = 0.65f;

    [Tooltip("Average chamber radius as a fraction of the footprint's smaller half-extent. The planner scatters radii around this, modulated by Chamber Size Variation.")]
    [Range(0.05f, 0.35f)] public float chamberRadiusFraction = 0.15f;

    [Tooltip("Width of the walkable channels connecting the chambers, in metres. This is the river bed the player walks along — wide, not a single thin line.")]
    [Range(6f, 40f)] public float channelWidth = 16f;

    [Tooltip("How much the channel width fluctuates along its length. 0 = uniform-width corridors; 1 = the channel pinches and flares like an eroded wash.")]
    [Range(0f, 1f)] public float channelWidthVariation = 0.5f;

    [Tooltip("Sideways meander of the channels. 0 = dead-straight cuts between chambers; 1 = strongly winding, snaking river beds.")]
    [Range(0f, 1f)] public float channelMeander = 0.55f;

    // -------------------------------------------------------------------------
    // The rock massif — the solid the channels are carved OUT of.
    // -------------------------------------------------------------------------

    [Header("Rock massif")]
    [Tooltip("Height of the rock massif above the channel floor, in metres. This is how tall the mesa walls tower over the player.")]
    [Range(15f, 120f)] public float massifHeight = 55f;

    [Tooltip("Per-mesa height variation. 0 = every mesa top at the same level (a flat plateau cut by channels); 1 = a jagged skyline of tall buttes and low benches.")]
    [Range(0f, 1f)] public float massifHeightVariation = 0.6f;

    [Tooltip("Depth the channels are cut BELOW the surrounding terrain, in metres. The player walks this much lower than the desert outside — a sunken maze.")]
    [Range(0f, 20f)] public float channelDepth = 4f;

    // -------------------------------------------------------------------------
    // Mesa rock-body shaping — the SAME model MesaFeature uses for its overhangs.
    // -------------------------------------------------------------------------

    [Header("Mesa overhang shaping")]
    [Tooltip("Rock-body shaping for every mesa in the maze. This is the EXACT same OverhangSettings block MesaFeature uses — each maze mesa is built as a RockBodySdf whose horizontal cross-section varies with height, so it bulges, pinches and overhangs as a direct consequence of its body shape. Turn 'Enable Overhangs' on inside this block to get the overhanging mesas (it is on by default for the maze).")]
    public OverhangSettings mesaBody = new OverhangSettings { enableOverhangs = true };

    // -------------------------------------------------------------------------
    // Boulders — the small rocks scattered alongside the massif.
    // -------------------------------------------------------------------------

    [Header("Boulders & small rocks")]
    [Tooltip("FEATURE TOGGLE — scatter free-standing boulders and small rocks across the channel floors and against the mesa feet. Off: bare channel floors.")]
    public bool enableBoulders = true;

    [Tooltip("Master boulder density. 0 = none, 1 = as planned, 2 = a heavy rock field. Scales how many boulders the placer scatters per channel-floor area.")]
    [Range(0f, 2f)] public float boulderDensity = 1f;

    [Tooltip("Lower / upper bound of boulder radius in metres. Each boulder picks a continuous radius in this band; most land small, a few large.")]
    public Vector2 boulderSize = new Vector2(0.6f, 4.5f);

    [Tooltip("How irregular / lumpy the boulders are. 0 = smooth ellipsoids; 1 = heavily faceted, eroded rocks.")]
    [Range(0f, 1f)] public float boulderLumpiness = 0.6f;

    // -------------------------------------------------------------------------
    // Shaping — erosion realism.
    // -------------------------------------------------------------------------

    [Header("Shaping")]
    [Tooltip("Strength of the global domain-warp erosion applied to the WHOLE maze field after the mesas, channels, floor and boulders are composed — ties everything into one cohesive water-eroded sandstone mass. The per-mesa face carving is controlled separately in the Mesa Overhang Shaping block. 0 = clean composition, 1 = heavily carved.")]
    [Range(0f, 1f)] public float erosion = 0.6f;

    // -------------------------------------------------------------------------
    // Chunking — the internal sub-tile granularity (the performance strategy).
    // -------------------------------------------------------------------------

    [Header("Internal chunking")]
    [Tooltip("Edge length of each internal sub-tile, in metres. The large maze is split into a grid of these; each is meshed separately into its own sub-mesh so no single voxel volume is huge. Smaller = more sub-meshes, each cheaper.")]
    [Range(20f, 120f)] public float subTileSize = 56f;
}
