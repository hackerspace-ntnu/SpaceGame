using UnityEngine;

/// <summary>
/// Tile kinds for the alien ancient-technological ruins generator.
///
/// All prefabs are modelled at 3× Unity unit scale (tileSize = 3,
/// prefab geometry spans 3 world units per tile).  One tile = one 3-unit grid cell.
/// </summary>
public enum TileKind
{
    // ── Horizontal slabs ────────────────────────────────────────────────────
    Floor,              // Ground-level walkable slab
    InteriorFloor,      // Upper-floor walkable slab (inside building)
    Roof,               // Top-of-structure slab
    Frieze,             // Thin decorative relief band at floor-transition level

    // ── Walls – monolithic ──────────────────────────────────────────────────
    WallMonolith,       // Thick blank stone wall — the dominant surface
    WallSlitWindow,     // Monolith wall with one tall narrow slit window
    WallGrandArch,      // Ground-floor wall replaced by a massive arch opening

    // ── Walls – structural texture ──────────────────────────────────────────
    WallButtress,       // Heavy vertical rib / pier on the wall face
    WallPanel,          // Shallow recessed rectangular panel
    WallTech,           // Futuristic wall greeble / relief insert
    WallVent,           // Vent / machinery strip for broad facade planes

    // ── Columns ─────────────────────────────────────────────────────────────
    Pillar,             // Thin corner column (exterior edge marker)
    ColonnadeColumn,    // Thick free-standing drum column (no wall behind)
    Balcony,            // Wall-mounted balcony / ledge

    // ── Rooftop ─────────────────────────────────────────────────────────────
    RoofParapet,        // Low battlement/crenellation wall at roof edge
    RoofTemple,         // Small temple-top structure (central roof feature)
    RoofMachinery,      // Mechanical rooftop detail / antenna cluster

    // ── Large structural ────────────────────────────────────────────────────
    GrandArch,          // Free-standing 3-floor arch spanning a gap
    Bridge,             // Elevated walkway between structures
    AqueductArch,       // Single arch pier supporting an elevated channel

    // ── Traversal ───────────────────────────────────────────────────────────
    Stair,              // Stair ramp ascending in face direction
    ExteriorRamp,       // Wide external approach ramp (monumental entrance)

    // ── Environmental accent ─────────────────────────────────────────────────
    Obelisk,            // Standalone tall tapered needle
    BoulderSmall,       // Small rock scatter (uses actual rock mesh)
    BoulderLarge,       // Large boulder feature (uses actual rock mesh)
}

/// <summary>
/// Which face a wall / arch / parapet is attached to, or which
/// corner (0-3) a pillar sits at.
/// </summary>
public enum WallFace { North, East, South, West }

/// <summary>
/// Semantic role of a column or block in the layout.
/// Drives detail selection (which walls, how many windows, etc.).
/// </summary>
public enum BlockRole
{
    Monolith,       // The central massive tower body
    Buttress,       // Thick wing / arm radiating from the base
    Terrace,        // Wide exposed platform at a height step
    Colonnade,      // Row of free-standing columns (no solid wall)
    BoulderCore,    // Optional central stone mass the ruins wrap around
}

/// <summary>
/// A single tile placement emitted by the generator and consumed by the builder.
/// </summary>
public struct TilePlacement
{
    public TileKind   kind;
    public Vector3Int cell;      // Integer grid position (Y = floor level)
    public WallFace   face;      // Wall facing or pillar corner index
    public BlockRole  role;      // Semantic role for detail selection
    public int        variant;   // Picks from prefab variant array (wraps mod length)
}
