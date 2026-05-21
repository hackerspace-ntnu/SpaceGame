using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// THE PLAN — pure-data output of the ArchingCave "careful planning" pipeline.
/// ====================================================================================
///
/// The feature is built in stages, exactly as the project lead mandated: plan the TOP-ORDER
/// structure first, then place the right things in the right places, then realise it as geometry.
/// This file holds the data each planning stage produces — it carries NO behaviour.
///
/// <para><b>The model — a carved cave, not unioned blobs.</b> The ArchingCave is a CAVE-LIKE
/// rock mass whose ceiling has been opened up in places. The plan therefore describes the
/// OPEN CAVITY to be carved out of a solid rock block, exactly the way the cave system's
/// <c>CaveGraph</c> describes rooms and corridors. The rock that SURVIVES between adjacent
/// carved cavities forms the pillars; the rock that survives spanning over a passage forms
/// the arches — both are emergent, never placed solids.</para>
///
///   • <see cref="ArchingCaveZone"/>   — a graph node: a CHAMBER of open space (a carved
///                                       cavity) with CONTINUOUS parameters (openness,
///                                       canopy, height…). No discrete "zone types".
///   • <see cref="ArchingCaveEdge"/>   — a PASSAGE of open space connecting two chambers.
///   • <see cref="ArchingCavePillar"/> — a KEEP-SOLID hint: a spot the cavity carve avoids,
///                                       so a column of rock survives there as a pillar.
///   • <see cref="ArchingCaveSkylight"/>— a vertical hole carved up THROUGH a canopied zone's
///                                       rock ceiling so dappled light comes down.
///   • <see cref="ArchingCavePlan"/>   — the whole bundle, handed to the SDF builder.
///
/// Stage 1 (<see cref="ArchingCavePlanner"/>) fills Zones + Edges.
/// Stage 2 (<see cref="ArchingCavePlacer"/>) fills Pillars + Skylights.
/// Stage 3 (<see cref="ArchingCaveSdf"/> / <see cref="ArchingCaveChunker"/>) turns it into meshes.
/// </summary>
public sealed class ArchingCavePlan
{
    /// <summary>Graph nodes — the planned chambers (cavities) across the footprint.</summary>
    public readonly List<ArchingCaveZone> Zones = new List<ArchingCaveZone>();

    /// <summary>Graph edges — open passages connecting chambers (guaranteed connected).</summary>
    public readonly List<ArchingCaveEdge> Edges = new List<ArchingCaveEdge>();

    /// <summary>Keep-solid pillar hints — spots the cavity carve avoids so rock columns survive.</summary>
    public readonly List<ArchingCavePillar> Pillars = new List<ArchingCavePillar>();

    /// <summary>Skylight holes carved up through canopied zones' rock ceilings (Stage 2).</summary>
    public readonly List<ArchingCaveSkylight> Skylights = new List<ArchingCaveSkylight>();

    /// <summary>Local-Y the continuous walkable cavity floor sits at. The whole floor is one
    /// slope-limited surface at (around) this height; carved chambers rest on it.</summary>
    public float FloorY;

    /// <summary>Local-Y of the solid rock ceiling over CANOPIED zones — the underside of the
    /// rock roof. Open zones carve straight past this, up out of the rock block entirely.</summary>
    public float CeilingY;
}

/// <summary>
/// A planned chamber — one node of the top-level zone graph, realised as a carved CAVITY of open
/// space (a sphere / vertical capsule) subtracted from the solid rock block. Carries CONTINUOUS
/// parameters only: there are deliberately no discrete zone "types". Blending these freely is how
/// the site reads as "cohesive but not systematic, all dynamic" — no two zones are alike.
/// </summary>
public struct ArchingCaveZone
{
    /// <summary>Index of this zone in <see cref="ArchingCavePlan.Zones"/>.</summary>
    public int Id;

    /// <summary>Zone centre in feature-local XZ (Y is the floor height).</summary>
    public Vector2 Center;

    /// <summary>Continuous zone radius in metres — the horizontal reach of the open chamber.
    /// Non-uniform across the site by design.</summary>
    public float Radius;

    /// <summary>0 = fully canopied (rock roof overhead), 1 = wide open to the sky. Continuous.
    /// Drives whether the cavity carve is punched straight through the ceiling.</summary>
    public float Openness;

    /// <summary>0 = empty clearing, 1 = a dense grove of (emergent) pillars. Continuous —
    /// drives how many keep-solid pillar hints the placer scatters in this chamber.</summary>
    public float PillarDensity;

    /// <summary>0 = no skylight holes, 1 = a canopied zone richly pierced by skylights.
    /// Continuous; only meaningful when <see cref="Openness"/> is low.</summary>
    public float CanopyAmount;

    /// <summary>Per-zone chamber-height multiplier — taller halls vs squat ones. Continuous.</summary>
    public float HeightScale;
}

/// <summary>A planned passage connecting two chambers — a carved capsule of open space so the
/// site's cavity is one connected, walkable space. A NavMeshAgent crosses the whole complex.</summary>
public struct ArchingCaveEdge
{
    /// <summary>Zone id at each end of the passage.</summary>
    public int FromZone, ToZone;
}

/// <summary>
/// A KEEP-SOLID pillar hint. The new model never unions a solid pillar capsule — instead this marks
/// a vertical column the cavity carve is pushed AWAY from, so a free-standing tower of cave rock
/// SURVIVES the carve there. Pillars are therefore emergent leftover rock, exactly like mesa
/// overhangs. They are NavMesh obstacles the floor routes around.
/// </summary>
public struct ArchingCavePillar
{
    /// <summary>Pillar centre-axis in feature-local XZ.</summary>
    public Vector2 Center;

    /// <summary>Radius of the protected rock core in metres — the carve keeps this much rock.</summary>
    public float BaseRadius;

    /// <summary>Local-Y the pillar foot sits on (the floor).</summary>
    public float FootY;

    /// <summary>Local-Y the pillar protection reaches up to (typically the ceiling, so the
    /// surviving rock spans floor-to-ceiling and reads as a true column or arch leg).</summary>
    public float TopY;

    /// <summary>0 = straight column, 1 = the protected core narrows toward the top (hoodoo).</summary>
    public float Taper;

    /// <summary>Owning zone id — lets the SDF tint erosion per zone.</summary>
    public int ZoneId;
}

/// <summary>A skylight hole — a vertical shaft of open space carved up THROUGH the rock ceiling of
/// a canopied zone so daylight streams down. Open zones get none (their whole ceiling is gone).</summary>
public struct ArchingCaveSkylight
{
    /// <summary>Hole centre in feature-local XZ.</summary>
    public Vector2 Center;

    /// <summary>Hole radius in metres.</summary>
    public float Radius;

    /// <summary>Owning zone id — the hole only carves within range of its chamber.</summary>
    public int ZoneId;
}
