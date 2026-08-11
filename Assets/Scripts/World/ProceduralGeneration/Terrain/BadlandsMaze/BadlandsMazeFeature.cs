using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// THE BADLANDS MAZE FEATURE — a labyrinth of mesas carved by a wide eroded river system.
/// ====================================================================================
///
/// A large AREA terrain feature: a solid desert rock massif eroded by a WIDE, BRANCHING channel
/// network into a dynamic maze. The channels and chambers are the carved void — the "wide river
/// system that ran through" — and the rock that survives between them is a labyrinth of mesas
/// with undercut overhangs and stratified cliff bands. The player walks the sunken channel floors
/// and looks UP at the towering walls; boulders and small rocks litter the floor alongside.
///
/// It is built by a four-stage "careful planning" pipeline, mirroring <see cref="ArchingCaveFeature"/>
/// — plan the top-order structure first, then place, then realise:
///   STAGE 1  <see cref="BadlandsMazePlanner"/> — lay out the channel graph (chambers joined by
///            meandering walkable channels; guaranteed connected).
///   STAGE 2  <see cref="BadlandsMazePlacer"/>  — scatter mesa lumps into the surviving rock and
///            litter boulders across the channel floors.
///   STAGE 3  <see cref="BadlandsMazeSdf"/>     — realise the plan as ONE global signed distance
///            field: the massif MINUS the carved channels, plus floor and boulders, eroded.
///   STAGE 3b <see cref="BadlandsMazeChunker"/> — split the site into a grid of sub-tiles and mesh
///            each tile's portion of the field into its own seamless sub-mesh.
///
/// MULTI-MESH: like ArchingCave, this overrides <see cref="ProducesMultipleMeshes"/> and
/// <see cref="BuildMeshes"/>. The single-mesh path of every other feature is untouched.
///
/// AREA feature: it uses the footprint POLYGON as the maze extent. It must NOT be added to
/// <c>TerrainFeatureSpawner.UsesPath</c>. Deterministic — seeded entirely off
/// <see cref="FeatureContext.Seed"/>.
/// </summary>
public sealed class BadlandsMazeFeature : TerrainFeature
{
    /// <summary>Per-spawner settings, injected before build. Never null after <see cref="ApplySettings"/>.</summary>
    BadlandsMazeSettings _settings;

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.BadlandsMaze;

    /// <summary>Voxel field — the maze has genuine overhangs (undercut mesa walls).</summary>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new BadlandsMazeSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        _settings = settings as BadlandsMazeSettings ?? new BadlandsMazeSettings();
    }

    // -------------------------------------------------------------------------
    // Multi-mesh capability — this feature emits one sub-mesh per internal tile.
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool ProducesMultipleMeshes => true;

    /// <summary>
    /// Builds the whole maze and returns it as a list of seamless sub-meshes. Runs all four
    /// pipeline stages: plan the channel graph, place mesas and boulders, build the global SDF,
    /// then chunk it into per-tile sub-meshes.
    /// </summary>
    public override List<Mesh> BuildMeshes(FeatureContext context, TerrainMeshSettings meshSettings)
    {
        var settings = _settings ?? new BadlandsMazeSettings();

        // The maze extent is the footprint polygon's bounds (this is an AREA feature).
        Bounds footprint = context.LocalBounds;

        // The surrounding desert terrain top under the footprint centre is the massif rim level.
        float rimY = context.LocalGroundHeight(footprint.center.x, footprint.center.z);

        // The walkable channel floor sits channelDepth below the rim — a sunken maze the player
        // descends into. Clamped so the floor never sits above the rim.
        float floorY = rimY - Mathf.Max(0f, settings.channelDepth);

        // STAGE 1 — plan the channel graph across the footprint.
        BadlandsMazePlan plan = BadlandsMazePlanner.Plan(settings, context, footprint, floorY, rimY);

        // STAGE 2 — place mesas into the surviving rock and scatter boulders on the floors.
        BadlandsMazePlacer.Place(plan, settings, context, footprint);

        // STAGE 3 — wrap the placed plan in the global SDF.
        Bounds siteVolume = ComputeSiteVolume(plan, settings, footprint, floorY);
        var sdf = new BadlandsMazeSdf(plan, settings, context.Seed, siteVolume)
            .WithTuning(context.Tuning);

        // STAGE 3b — chunk the global SDF into seamless per-tile sub-meshes.
        return BadlandsMazeChunker.BuildSubMeshes(sdf, settings, meshSettings);
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
    /// Local-space volume the whole maze occupies: the footprint XZ extent, and a Y range from a
    /// little below the channel floor up past the tallest mesa, with erosion headroom.
    /// </summary>
    static Bounds ComputeSiteVolume(
        BadlandsMazePlan plan, BadlandsMazeSettings settings, Bounds footprint, float floorY)
    {
        // Start above the nominal massif top; raise to clear the tallest placed mesa.
        float topY = plan.RimY + settings.massifHeight + 8f;
        for (int i = 0; i < plan.Mesas.Count; i++)
            topY = Mathf.Max(topY, plan.Mesas[i].TopY);

        // Headroom for the rock-body erosion warp + side crags so the tallest bulge fits.
        OverhangSettings body = settings.mesaBody ?? new OverhangSettings();
        float headroom = settings.erosion * 8f + body.erosion + body.sideJaggedness + 6f;
        float minY = floorY - 10f;
        float maxY = topY + headroom;

        Vector3 centre = new Vector3(footprint.center.x, (minY + maxY) * 0.5f, footprint.center.z);
        Vector3 size = new Vector3(footprint.size.x, maxY - minY, footprint.size.z);
        return new Bounds(centre, size);
    }
}
