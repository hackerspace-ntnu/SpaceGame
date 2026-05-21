using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// STAGE 2 — STRUCTURE PLACEMENT (keep-solid hints + skylight holes).
/// ====================================================================================
///
/// Given the planned chamber graph from <see cref="ArchingCavePlanner"/>, this decides WHAT goes
/// WHERE — but in the carve-based model it never places solid rock. Instead it places:
///
///   • KEEP-SOLID PILLAR HINTS — spots inside a chamber the cavity carve is pushed away from, so
///     a free-standing column of cave rock SURVIVES the carve there. Count/size/spacing are
///     driven by the chamber's CONTINUOUS parameters (a dense grove vs an open clearing). Arches
///     are NOT placed: an arch is simply the rock that survives spanning between two such
///     pillar hints when their protected cores nearly touch — it emerges from the carve.
///   • SKYLIGHT HOLES — vertical shafts carved up through a CANOPIED chamber's surviving rock
///     ceiling so dappled daylight comes down. Open chambers get none (their whole ceiling is
///     already carved away).
///
/// All placement is deterministic off <see cref="FeatureContext.Seed"/> and non-uniform. The
/// output lists are appended to the <see cref="ArchingCavePlan"/> for the SDF builder.
///
/// IMPORTANT — passages stay clear: pillar hints are kept inside the chamber discs (not in the
/// passage lanes), so the connected cavity floor is never blocked by surviving rock.
/// </summary>
public static class ArchingCavePlacer
{
    /// <summary>Fills the plan's Pillars / Skylights lists from its chamber graph.</summary>
    public static void Place(ArchingCavePlan plan, ArchingCaveSettings settings, int seed)
    {
        var rng = new System.Random(seed * 31 + 90113);

        foreach (var zone in plan.Zones)
        {
            PlacePillarHints(plan, settings, zone, rng);
            PlaceSkylights(plan, settings, zone, rng);
        }
    }

    // -------------------------------------------------------------------------
    // Keep-solid pillar hints — the carve survives rock columns at these spots.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scatters keep-solid pillar hints inside one chamber. The COUNT scales with the chamber's
    /// continuous <see cref="ArchingCaveZone.PillarDensity"/> and area; positions are jittered
    /// (never a grid); core radii pick continuously from the settings band, biased thinner in
    /// dense groves and thicker in sparse clearings so the result reads dynamic, not systematic.
    /// Each hint protects a vertical column of rock from floor to ceiling — the surviving rock
    /// reads as a true pillar, and two near-touching hints leave an arch of rock between them.
    /// </summary>
    static void PlacePillarHints(ArchingCavePlan plan, ArchingCaveSettings settings,
        ArchingCaveZone zone, System.Random rng)
    {
        float density = zone.PillarDensity * settings.pillarDensity;
        if (density <= 0.01f) return;

        // Count from area / nominal spacing, scaled by density. Nominal spacing is a few pillar
        // widths so a dense chamber is a true grove and a sparse one a near-empty clearing.
        float avgThick = (settings.pillarThickness.x + settings.pillarThickness.y) * 0.5f;
        float spacing = Mathf.Lerp(avgThick * 6f, avgThick * 2.4f, Mathf.Clamp01(density));
        float zoneArea = Mathf.PI * zone.Radius * zone.Radius;
        int count = Mathf.Clamp(Mathf.RoundToInt(zoneArea / (spacing * spacing)), 0, 48);

        // The protected column rises from the floor to (around) the ceiling so the surviving
        // rock spans the full chamber height — a real column, or an arch leg.
        float top = plan.CeilingY;

        // Rejection-sample positions inside the chamber disc, jittered, with a minimum gap. Keep
        // hints off the chamber rim (0.78 of radius) so each surviving column is fully free-
        // standing inside the cavity rather than fused into the chamber wall.
        var placed = new List<Vector2>();
        int attempts = count * 12;
        while (placed.Count < count && attempts-- > 0)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float r = zone.Radius * Mathf.Sqrt((float)rng.NextDouble()) * 0.78f;
            Vector2 p = zone.Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;

            bool clash = false;
            foreach (var q in placed)
                if (Vector2.Distance(p, q) < spacing * 0.7f) { clash = true; break; }
            if (clash) continue;

            // Thinner cores in dense groves, thicker in clearings — continuous blend.
            float sizeT = Mathf.Lerp((float)rng.NextDouble(), (float)rng.NextDouble(),
                                     Mathf.Clamp01(density));
            float baseRadius = Mathf.Lerp(settings.pillarThickness.x, settings.pillarThickness.y,
                                          1f - sizeT);

            placed.Add(p);
            plan.Pillars.Add(new ArchingCavePillar
            {
                Center = p,
                BaseRadius = baseRadius,
                FootY = plan.FloorY,
                TopY = top,
                Taper = settings.pillarTaper * (0.6f + (float)rng.NextDouble() * 0.8f),
                ZoneId = zone.Id,
            });
        }
    }

    // -------------------------------------------------------------------------
    // Skylight holes — carved up through a canopied chamber's rock ceiling.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Places skylight holes for a CANOPIED chamber — vertical shafts the carve punches up through
    /// the surviving rock roof so dappled light comes down. The chamber must clear a canopy
    /// threshold (open chambers get none — their ceiling is already fully carved away). Hole count
    /// rises as the canopy gets thinner/patchier so light always finds a way in.
    /// </summary>
    static void PlaceSkylights(ArchingCavePlan plan, ArchingCaveSettings settings,
        ArchingCaveZone zone, System.Random rng)
    {
        float canopy = zone.CanopyAmount * settings.canopyAmount;
        if (canopy < 0.3f) return;          // open chambers stay open to the sky, no roof to pierce

        int holeCount = Mathf.Clamp(
            Mathf.RoundToInt(zone.Radius / settings.skylightHoleSize * Mathf.Lerp(1.4f, 0.5f, canopy)),
            1, 8);

        for (int h = 0; h < holeCount; h++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float r = zone.Radius * Mathf.Sqrt((float)rng.NextDouble()) * 0.72f;
            plan.Skylights.Add(new ArchingCaveSkylight
            {
                Center = zone.Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r,
                Radius = settings.skylightHoleSize * (0.55f + (float)rng.NextDouble() * 0.9f),
                ZoneId = zone.Id,
            });
        }
    }
}
