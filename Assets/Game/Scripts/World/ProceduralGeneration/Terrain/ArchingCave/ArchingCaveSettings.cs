using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Designer-tunable settings for the <see cref="ArchingCaveFeature"/> — the monumental desert
    /// rock complex of cave-like chambers, pillars and arches.
    ///
    /// <para><b>The model.</b> The ArchingCave is a CAVE whose ceiling has been opened up in places.
    /// A solid rock block is built between the floor and a ceiling height, then an open CAVITY
    /// (chambers + passages, just like the cave system's room/corridor graph) is carved out of it.
    /// Pillars and arches are the rock that SURVIVES the carve — emergent, never placed solids.
    /// OPEN zones carve straight through the ceiling (open to the sky); CANOPIED zones keep their
    /// rock roof but get skylight holes punched through it.</para>
    ///
    /// Every value drives deterministic math seeded off <see cref="FeatureContext.Seed"/> — nothing
    /// here is mutable or frame-dependent. Same settings + seed + footprint = identical complex.
    /// </summary>
    [System.Serializable]
    public class ArchingCaveSettings
    {
        // -------------------------------------------------------------------------
        // Site layout — how the chamber graph is planned across the footprint.
        // -------------------------------------------------------------------------

        [Header("Site layout")]
        [Tooltip("How many chambers the planner carves across the footprint. More chambers = a larger, more sprawling cave complex. The random-walk planner connects them all into one walkable cavity.")]
        [Range(3, 24)] public int zoneCount = 9;

        [Tooltip("Spread of chamber radii. 0 = every chamber the same size (systematic, discouraged); 1 = wildly varied (tiny alcoves next to vast halls). The project lead wants non-uniform sizes.")]
        [Range(0f, 1f)] public float zoneSizeVariation = 0.6f;

        [Tooltip("Average chamber radius as a fraction of the footprint's smaller half-extent. The planner scatters radii around this, modulated by Zone Size Variation.")]
        [Range(0.08f, 0.45f)] public float zoneRadiusFraction = 0.22f;

        [Tooltip("Width of the carved passages connecting chambers, in metres. The continuous cavity floor threads these so the whole cave is one connected walkable space.")]
        [Range(3f, 24f)] public float corridorWidth = 9f;

        // -------------------------------------------------------------------------
        // Cave shape — the solid rock block the cavity is carved from.
        // -------------------------------------------------------------------------

        [Header("Cave shape")]
        [Tooltip("Height of the rock ceiling above the walkable floor, in metres. Canopied chambers keep a rock roof at this height; open chambers carve straight past it up out of the rock.")]
        [Range(12f, 80f)] public float ceilingHeight = 34f;

        [Tooltip("How much vertical room each chamber's carved cavity has, as a fraction of the ceiling height. ~1 = the cavity reaches the ceiling; lower = squatter chambers with thick roof rock.")]
        [Range(0.4f, 1.3f)] public float chamberHeight = 0.95f;

        [Tooltip("Smooth-union radius blending chamber and passage cavities together, in metres. Larger = cavities flow into one another more softly; the carved cave reads as one organic space.")]
        [Range(0.5f, 8f)] public float caveSmoothness = 3.4f;

        // -------------------------------------------------------------------------
        // Pillars — emergent rock columns surviving between carved cavities.
        // -------------------------------------------------------------------------

        [Header("Pillars")]
        [Tooltip("Lower / upper bound of the protected rock core radius in metres. Each keep-solid pillar hint picks a continuous radius in this band; the cavity carve avoids it so a column survives.")]
        public Vector2 pillarThickness = new Vector2(3f, 11f);

        [Tooltip("Master pillar density multiplier. Each chamber carries its own continuous density; this scales them all. 0 = no kept columns (chambers wide open), 1 = as planned, >1 = denser groves.")]
        [Range(0f, 2f)] public float pillarDensity = 1f;

        [Tooltip("How much the protected pillar core narrows from base to top. 0 = straight columns, 1 = strongly tapered hoodoo spires.")]
        [Range(0f, 1f)] public float pillarTaper = 0.4f;

        // -------------------------------------------------------------------------
        // Canopy — the rock ceiling over canopied zones, pierced by skylights.
        // -------------------------------------------------------------------------

        [Header("Canopy & skylights")]
        [Tooltip("Master canopy amount. Each chamber carries a continuous canopy value; this scales it. 0 = the whole site is open to the sky; 1 = canopied chambers keep a full rock roof. A mix across the site is the design intent.")]
        [Range(0f, 1f)] public float canopyAmount = 0.5f;

        [Tooltip("Radius of the irregular skylight holes carved up through canopy roofs, in metres. Daylight streams down through these.")]
        [Range(4f, 30f)] public float skylightHoleSize = 12f;

        // -------------------------------------------------------------------------
        // Shaping — erosion realism and overall vertical scale.
        // -------------------------------------------------------------------------

        [Header("Shaping")]
        [Tooltip("Strength of the domain-warp erosion that ripples the carved cave walls so the rock reads as one cohesive eroded sandstone mass. 0 = geometric SDF surfaces, 1 = heavily carved.")]
        [Range(0f, 1f)] public float erosion = 0.55f;

        [Tooltip("Overall height multiplier for the complex. Scales the ceiling height and chamber heights site-wide.")]
        [Range(0.4f, 3f)] public float overallHeight = 1f;

        // -------------------------------------------------------------------------
        // Floor shaping — keeps the carved cavity floor walkable for NavMeshAgents.
        // -------------------------------------------------------------------------

        [Header("Floor shaping")]
        [Tooltip("How aggressively the carved cavity floor is flattened toward a level plane. 1 = perfectly flat, 0 = the natural rounded cavity bottom. 0.5-0.8 gives a 'mostly flat with bumps' walkable floor.")]
        [Range(0f, 1f)] public float floorFlattenStrength = 0.7f;

        [Tooltip("Strength of small-scale noise added on top of the flattened floor (metres). Gives the floor an organic feel without breaking NavMesh.")]
        [Range(0f, 1.5f)] public float floorRoughness = 0.5f;

        // -------------------------------------------------------------------------
        // Chunking — the internal sub-tile granularity (the performance strategy).
        // -------------------------------------------------------------------------

        [Header("Internal chunking")]
        [Tooltip("Edge length of each internal sub-tile, in metres. The large site is split into a grid of these; each is meshed separately into its own sub-mesh so no single voxel volume is huge. Smaller = more sub-meshes, each cheaper.")]
        [Range(20f, 120f)] public float subTileSize = 56f;
    }
}
