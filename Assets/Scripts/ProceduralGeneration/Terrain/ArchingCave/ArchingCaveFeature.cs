using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// THE ARCHING CAVE FEATURE — a monumental desert rock complex of pillars and arches.
/// ====================================================================================
///
/// A large AREA terrain feature: a CAVE-LIKE rock complex whose ceiling has been opened up in
/// places. The open walkable space — chambers joined by passages — is CARVED out of a solid rock
/// block; the rock that survives the carve forms cave-like walls, emergent PILLARS (rock left
/// between adjacent cavities) and emergent ARCHES (rock spanning over passages). Some chambers are
/// carved straight through the ceiling, OPEN to the sky; others stay CANOPIED by a surviving rock
/// roof pierced by skylight holes.
///
/// It is built by a four-stage "careful planning" pipeline — plan the top-order structure first,
/// then place the right things in the right places:
///   STAGE 1  <see cref="ArchingCavePlanner"/> — lay out the chamber graph (irregular chambers
///            with continuous parameters, joined by walkable passages; guaranteed connected).
///   STAGE 2  <see cref="ArchingCavePlacer"/>  — scatter keep-solid pillar hints and skylight
///            holes, driven by each chamber's continuous params (dense grove vs open clearing).
///   STAGE 3  <see cref="ArchingCaveSdf"/>     — realise the plan as ONE global signed distance
///            field: a solid rock block with the chamber/passage cavity carved out of it,
///            domain-warp eroded into one cohesive cave.
///   STAGE 3b <see cref="ArchingCaveChunker"/> — split the site into a grid of sub-tiles and mesh
///            each tile's portion of the field into its own sub-mesh, all seamless because they
///            share the one global field on a common voxel lattice.
///
/// MULTI-MESH: this is the first feature to produce more than one mesh. It overrides
/// <see cref="ProducesMultipleMeshes"/> and <see cref="BuildMeshes"/> — the rest of the feature
/// system's single-mesh path is untouched and every other feature keeps working unchanged.
///
/// AREA feature: it uses the footprint POLYGON as the site extent. It must NOT be added to
/// <c>TerrainFeatureSpawner.UsesPath</c>. Deterministic — seeded entirely off
/// <see cref="FeatureContext.Seed"/>.
/// </summary>
public sealed class ArchingCaveFeature : TerrainFeature
{
    /// <summary>Per-spawner settings, injected before build. Never null after <see cref="ApplySettings"/>.</summary>
    ArchingCaveSettings _settings;

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.ArchingCave;

    /// <summary>Voxel field — the complex has genuine overhangs (arches, canopy roofs).</summary>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new ArchingCaveSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        _settings = settings as ArchingCaveSettings ?? new ArchingCaveSettings();
    }

    // -------------------------------------------------------------------------
    // Multi-mesh capability — this feature emits one sub-mesh per internal tile.
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool ProducesMultipleMeshes => true;

    /// <summary>
    /// Builds the whole complex and returns it as a list of seamless sub-meshes. Runs all four
    /// pipeline stages: plan the zone graph, place the structures, build the global SDF, then
    /// chunk it into per-tile sub-meshes.
    /// </summary>
    public override List<Mesh> BuildMeshes(FeatureContext context, TerrainMeshSettings meshSettings)
    {
        var settings = _settings ?? new ArchingCaveSettings();

        // The site extent is the footprint polygon's bounds (this is an AREA feature).
        Bounds footprint = context.LocalBounds;

        // The walkable floor sits at the ground height under the footprint centre, so the complex
        // is grounded on the underlying terrain.
        float floorY = context.LocalGroundHeight(footprint.center.x, footprint.center.z);

        // STAGE 1 — plan the zone graph across the footprint.
        ArchingCavePlan plan = ArchingCavePlanner.Plan(settings, context, footprint, floorY);

        // STAGE 2 — place pillars, arches and canopy roofs into the plan.
        ArchingCavePlacer.Place(plan, settings, context.Seed);

        // STAGE 3 — wrap the placed plan in the global SDF. The site volume spans the footprint
        // XZ and a Y range tall enough for the canopy roof plus erosion headroom.
        Bounds siteVolume = ComputeSiteVolume(plan, settings, footprint, floorY);
        var sdf = new ArchingCaveSdf(plan, settings, context.Seed, siteVolume);

        // STAGE 3b — chunk the global SDF into seamless per-tile sub-meshes.
        return ArchingCaveChunker.BuildSubMeshes(sdf, settings, meshSettings);
    }

    /// <summary>
    /// Single-mesh path is unused for this feature — <see cref="BuildMeshes"/> is called instead.
    /// Returns null so a stray call surfaces clearly rather than silently producing geometry.
    /// </summary>
    public override ITerrainDensity BuildDensity(FeatureContext context) => null;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Local-space volume the whole complex occupies: the footprint XZ extent, and a Y range from
    /// a little below the floor up past the rock ceiling and the tallest open-chamber / skylight
    /// carve — open chambers carve well above the ceiling, so the volume must contain that air.
    /// </summary>
    static Bounds ComputeSiteVolume(
        ArchingCavePlan plan, ArchingCaveSettings settings, Bounds footprint, float floorY)
    {
        float ceilSpan = plan.CeilingY - floorY;

        // Open chambers carve up to roughly CeilingY + 0.45*span + a height-scale term, and
        // skylight shafts reach CeilingY + 0.4*span. Size the volume above all of that.
        float maxChamberHeight = ceilSpan * settings.chamberHeight * 1.5f;   // 1.5 = max HeightScale
        float openTop = plan.CeilingY + ceilSpan * 0.45f + maxChamberHeight * 0.15f;
        float topY = Mathf.Max(openTop, plan.CeilingY + ceilSpan * 0.4f) + 8f;

        float headroom = settings.erosion * 8f + 6f;
        float minY = floorY - 12f;
        float maxY = topY + headroom;

        Vector3 centre = new Vector3(footprint.center.x, (minY + maxY) * 0.5f, footprint.center.z);
        Vector3 size = new Vector3(footprint.size.x, maxY - minY, footprint.size.z);
        return new Bounds(centre, size);
    }
}
