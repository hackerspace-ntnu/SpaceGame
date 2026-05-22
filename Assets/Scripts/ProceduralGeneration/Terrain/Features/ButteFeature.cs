using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="ButteFeature"/>. Serializable so a designer can override
/// them on the spawner while the four core knobs (<see cref="TerrainFeatureTuning"/>) still
/// control noise, overlap, height and jaggedness uniformly across all features.
/// </summary>
[System.Serializable]
public class ButteSettings
{
    [Tooltip("Fraction of the tower's radial span that forms the flat or domed cap, [0.05, 0.5]. " +
             "Smaller = narrower spire; larger = wider mesa cap.")]
    [Range(0.05f, 0.5f)]
    public float capRadiusFraction = 0.18f;

    [Tooltip("Upward taper: how much the tower narrows as it rises, expressed as a fraction of " +
             "the base radius per unit of height. Gives a wind-carved spire silhouette.")]
    [Range(0f, 0.6f)]
    public float taperAmount = 0.25f;

    [Tooltip("Amplitude multiplier for the vertical fluting/striation noise on the walls. " +
             "Higher values give more eroded, deeply-channelled sides.")]
    [Range(0f, 2f)]
    public float flutingStrength = 1.1f;

    [Tooltip("Spatial frequency of the vertical fluting noise (higher = finer flutes).")]
    [Range(0.01f, 0.3f)]
    public float flutingFrequency = 0.08f;

    [Tooltip("Fraction of total height over which the wall profile rises (the 'wall steepness' " +
             "fraction fed to TerrainProfiles.Plateau). Small values = nearly vertical walls.")]
    [Range(0.02f, 0.35f)]
    public float wallFraction = 0.07f;

    [Tooltip("Slight upward dome on the cap, as a fraction of total butte height.")]
    [Range(0f, 0.15f)]
    public float capDome = 0.04f;
}

/// <summary>
/// Terrain feature that sculpts a butte: an isolated, near-vertical rock tower / spire, taller
/// relative to its width than a mesa. Characteristic of desert ancient-planet landscapes — the
/// butte sits on the existing terrain, has a small flat-to-domed cap, deeply fluted and eroded
/// walls, and a slight taper so it reads as a wind-carved spire rather than a cylindrical pillar.
///
/// Shape summary:
/// <list type="bullet">
///   <item>Radial silhouette — distance from the footprint centre drives a steep
///         <see cref="TerrainProfiles.Plateau"/> wall rising to a narrow cap.</item>
///   <item>Organic wall erosion — vertical fluting via <see cref="TerrainNoiseHelper.Fbm"/>
///         modulates the effective radius so the outline is craggy and non-circular.</item>
///   <item>Taper — the effective radius shrinks linearly with height, sharpening the spire.</item>
///   <item>Cap dome — a gentle Ridge-shaped dome crowns the flat top.</item>
///   <item>Overlap blend — only the base/skirt blends into the surrounding terrain; the main
///         body stays vertical and un-softened.</item>
/// </list>
///
/// All output is deterministic off <see cref="FeatureContext.Seed"/>; no calls to Time or
/// Random.value are made.
/// </summary>
public sealed class ButteFeature : TerrainFeature
{
    // -------------------------------------------------------------------------
    // Per-feature settings — a designer may assign a custom instance via the
    // spawner; otherwise these defaults are used.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional per-instance settings. When null the class-default <see cref="ButteSettings"/>
    /// values are used. A spawner can expose these via a public field and wire them in before
    /// calling <c>BuildDensity</c>.
    /// </summary>
    public ButteSettings Settings = new ButteSettings();

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.Butte;

    /// <summary>A butte has no genuine overhang — the cheap heightfield path is correct.</summary>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new ButteSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings) => Settings = settings as ButteSettings;

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box         = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;
        ButteSettings s    = Settings ?? new ButteSettings();
        int seed           = context.Seed;

        // -------------------------------------------------------------------------
        // Footprint geometry.
        // The butte is inscribed as an ellipse in the spawner box so it reads as a
        // tower regardless of how wide or narrow the designer made the footprint.
        // -------------------------------------------------------------------------
        Vector2 centreXZ  = new Vector2(box.center.x, box.center.z);
        // Semi-axes of the elliptical footprint, shrunk a little so the wall never
        // exactly touches the box edge (prevents an abrupt seam at the boundary).
        float semiX       = box.extents.x * 0.88f;
        float semiZ       = box.extents.z * 0.88f;
        float minSemi     = Mathf.Min(semiX, semiZ);

        // -------------------------------------------------------------------------
        // Heights.
        // -------------------------------------------------------------------------
        float butteHeight  = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);

        // Buttes are taller than wide — enforce a minimum aspect of 1.2× the shorter radius.
        butteHeight        = Mathf.Max(butteHeight, minSemi * 1.2f);

        // Vertical band sampling constants.
        float groundAtCentre = context.LocalGroundHeight(centreXZ.x, centreXZ.y);
        float maxSurfaceY  = groundAtCentre + butteHeight + tuning.noiseAmount * s.flutingStrength + 4f;
        float minSurfaceY  = groundAtCentre - 4f;

        // -------------------------------------------------------------------------
        // Height lambda — all the butte maths happens here.
        // -------------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY  = context.LocalGroundHeight(x, z);

            // --- 1. Radial distance from the ellipse centre, normalised to [0,1] ---------
            float dx       = (x - centreXZ.x) / semiX;
            float dz       = (z - centreXZ.y) / semiZ;
            float radialT  = Mathf.Sqrt(dx * dx + dz * dz);   // 0 = centre, 1 = ellipse rim

            // --- 2. Overlap weight: blend only the outer skirt into the terrain -----------
            // The tower shape stays radial (radialT drives the Plateau/taper/fluting above),
            // but the footprint blend is now gated by the designer-drawn polygon so the butte
            // never spills outside the spawner's drawn outline.
            // polyDist: metres inside the polygon boundary (negative = outside).
            // radialDist: metres inside the ellipse rim — keeps the spire silhouette radial.
            // Taking the minimum honours whichever boundary is tighter at each point.
            float polyDist   = context.FootprintDistanceInside(x, z);
            float distInside = Mathf.Min(minSemi * (1f - radialT), polyDist);
            float weight   = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // --- 3. Fluting noise: modulate the effective radius organically ---------------
            // Two fbm layers at different scales break up the circular silhouette and carve
            // vertical channels (flutes) into the wall — deterministic off seed.
            Vector3 pFlute = new Vector3(x, 0f, z);   // fluting is purely lateral
            float flute    = TerrainNoiseHelper.Fbm(pFlute, s.flutingFrequency, seed + 3, 3);
            flute          = TerrainNoiseHelper.ApplyJaggedness(flute, tuning.jaggedness);
            // Positive flute pulls the surface outward (eroded notch), negative inward (rib).
            // Scale by the overlap weight so fluting only affects the visible tower, not the
            // invisible region outside the footprint.
            float fluteDisp = flute * s.flutingStrength * tuning.noiseAmount * weight;

            // --- 4. Plateau profile: steep walls, flat cap --------------------------------
            // edgeDistance is how far (normalised) we are from the rim toward the centre.
            // cap fraction shrinks the "flat" zone — small value = spire, large = mesa top.
            float edgeNorm = Mathf.Clamp01((1f - radialT) / 1f);  // already [0,1]: 0=rim, 1=centre
            float profile  = TerrainProfiles.Plateau(edgeNorm, s.wallFraction);

            // --- 5. Taper: narrow the effective column as it rises ------------------------
            // We model taper by reducing 'profile' contribution for points that are far from
            // the centre, proportional to the normalised height they would sit at.
            // After the profile step we compute where this point sits on the tower and push
            // points near the rim downward slightly — giving the spire shape.
            // Taper: multiply wall profile by (1 - taperAmount * radialT)  then re-clamp.
            float taperScale = Mathf.Clamp01(1f - s.taperAmount * radialT);
            profile          = profile * taperScale;

            // --- 6. Cap dome: gentle ridge crown on the flat top --------------------------
            // Only applied where profile is near 1 (i.e., inside the cap radius).
            float capRadius  = s.capRadiusFraction;   // fraction of span
            float capT       = Mathf.Clamp01(1f - radialT / capRadius);  // 1 at centre, 0 at cap rim
            float dome       = TerrainProfiles.Ridge(capT * 2f - 1f, 0.4f) * s.capDome;

            // --- 7. Surface noise on the cap for organic detail ---------------------------
            Vector3 pNoise = new Vector3(x, 0f, z);
            float surfNoise = TerrainNoiseHelper.SurfaceNoise(pNoise, tuning, seed);

            // Only apply general surface noise within the cap — the wall read comes from
            // the fluting which is already applied via fluteDisp.
            float capBlend   = Mathf.Clamp01(capT * 2f);       // 0→1 transitioning into the cap
            float topNoise   = surfNoise * capBlend;

            // --- 8. Assemble final height -------------------------------------------------
            float towerH     = (profile + dome) * butteHeight + topNoise + fluteDisp;

            // Clamp so we never go below ground regardless of noise pulls.
            towerH           = Mathf.Max(0f, towerH);

            return groundY + towerH * weight;
        };

        // Coverage mask: the butte only occupies the columns inside its elliptical tower footprint
        // (intersected with the designer polygon). Outside that the column is plain ground —
        // meshing it would build a flat apron around the spire, which is not wanted. A column is
        // covered exactly where the overlap weight is non-zero, using the same min(ellipse, polygon)
        // distance the height lambda uses.
        System.Func<float, float, bool> coverageFn = (x, z) =>
        {
            float dx       = (x - centreXZ.x) / semiX;
            float dz       = (z - centreXZ.y) / semiZ;
            float radialT  = Mathf.Sqrt(dx * dx + dz * dz);
            float polyDist   = context.FootprintDistanceInside(x, z);
            float distInside = Mathf.Min(minSemi * (1f - radialT), polyDist);
            return TerrainNoiseHelper.OverlapWeight(distInside, tuning) > 0f;
        };

        float bandPadding = context.VoxelSize * 2f;
        return new HeightfieldDensity(heightFn, box, minSurfaceY, maxSurfaceY, bandPadding, coverageFn);
    }
}
