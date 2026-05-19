using UnityEngine;

/// <summary>
/// Factory helpers that return preconfigured <see cref="DecorationRule"/> instances. Use these as
/// starting points when authoring a <see cref="CaveProfile"/>:
///
///   var profile = ScriptableObject.CreateInstance&lt;CaveProfile&gt;();
///   profile.decoration.rules = new[] {
///       PrebuiltDecorationRules.Stalagmites(),
///       PrebuiltDecorationRules.Stalactites(),
///       PrebuiltDecorationRules.BioluminescentFronds(),
///   };
///
/// Every rule has its prefab list empty so it falls back to procedural primitives — usable
/// out-of-the-box, replace prefabs once art exists. Designed for an alien cave setting, so the
/// colour palette skews toward unusual blues / cyans / purples / greens.
/// </summary>
public static class PrebuiltDecorationRules
{
    // -------------------------------------------------------------------------
    // Earth-like (still useful for "less alien" caves)
    // -------------------------------------------------------------------------

    public static DecorationRule Stalagmites() => new DecorationRule
    {
        name = "Stalagmites",
        surfaceType = DecorationSurfaceType.Floor,
        fallbackPrimitive = PrimitiveType.Cylinder,
        fallbackColor = new Color(0.45f, 0.40f, 0.35f),
        densityPerSqM = 0.05f,
        minSpacing = 1.8f,
        scaleRange = new Vector2(0.6f, 1.6f),
        normalAlignment = 0.8f,
        maxSurfaceTilt = 0.6f,
        surfaceEmbed = -0.2f,
        seedSalt = 1001,
    };

    public static DecorationRule Stalactites() => new DecorationRule
    {
        name = "Stalactites",
        surfaceType = DecorationSurfaceType.Ceiling,
        fallbackPrimitive = PrimitiveType.Cylinder,
        fallbackColor = new Color(0.4f, 0.35f, 0.30f),
        densityPerSqM = 0.04f,
        minSpacing = 1.5f,
        scaleRange = new Vector2(0.4f, 1.2f),
        normalAlignment = 0.95f,
        maxSurfaceTilt = 0.7f,
        surfaceEmbed = -0.2f,
        seedSalt = 1002,
    };

    public static DecorationRule Boulders() => new DecorationRule
    {
        name = "Boulders",
        surfaceType = DecorationSurfaceType.Floor,
        fallbackPrimitive = PrimitiveType.Sphere,
        fallbackColor = new Color(0.35f, 0.33f, 0.30f),
        densityPerSqM = 0.025f,
        minSpacing = 2.5f,
        scaleRange = new Vector2(0.5f, 1.8f),
        normalAlignment = 0.2f,
        maxSurfaceTilt = 0.5f,
        surfaceEmbed = -0.3f,
        seedSalt = 1003,
    };

    public static DecorationRule DebrisPiles() => new DecorationRule
    {
        name = "Debris",
        surfaceType = DecorationSurfaceType.Floor,
        fallbackPrimitive = PrimitiveType.Sphere,
        fallbackColor = new Color(0.25f, 0.22f, 0.20f),
        densityPerSqM = 0.08f,
        minSpacing = 0.7f,
        scaleRange = new Vector2(0.15f, 0.45f),
        normalAlignment = 0f,
        maxSurfaceTilt = 0.7f,
        surfaceEmbed = -0.1f,
        seedSalt = 1004,
    };

    // -------------------------------------------------------------------------
    // Alien — bioluminescent, crystalline, fungal
    // -------------------------------------------------------------------------

    /// <summary>Tall thin glowing strands on walls/floors. Pairs well with point lights.</summary>
    public static DecorationRule BioluminescentFronds() => new DecorationRule
    {
        name = "BioluminescentFronds",
        surfaceType = DecorationSurfaceType.Any,
        fallbackPrimitive = PrimitiveType.Cylinder,
        fallbackColor = new Color(0.1f, 0.3f, 0.5f),
        fallbackEmission = new Color(0.2f, 0.8f, 1.5f) * 2.5f,
        densityPerSqM = 0.06f,
        minSpacing = 1.2f,
        scaleRange = new Vector2(0.3f, 0.9f),
        normalAlignment = 0.85f,
        surfaceEmbed = -0.05f,
        attachedLightRange = 4f,
        attachedLightIntensity = 0.6f,
        seedSalt = 2001,
    };

    /// <summary>Cluster of small glowing pods on ceilings.</summary>
    public static DecorationRule SporePods() => new DecorationRule
    {
        name = "SporePods",
        surfaceType = DecorationSurfaceType.Ceiling,
        fallbackPrimitive = PrimitiveType.Sphere,
        fallbackColor = new Color(0.4f, 0.2f, 0.5f),
        fallbackEmission = new Color(0.8f, 0.3f, 1.2f) * 1.6f,
        densityPerSqM = 0.04f,
        minSpacing = 1.0f,
        scaleRange = new Vector2(0.25f, 0.5f),
        normalAlignment = 0.7f,
        surfaceEmbed = -0.15f,
        attachedLightRange = 3f,
        attachedLightIntensity = 0.4f,
        seedSalt = 2002,
    };

    /// <summary>Large embedded crystal formations on walls — emissive, no attached light by default.</summary>
    public static DecorationRule CrystalLattices() => new DecorationRule
    {
        name = "CrystalLattices",
        surfaceType = DecorationSurfaceType.Wall,
        fallbackPrimitive = PrimitiveType.Cube,
        fallbackColor = new Color(0.5f, 0.7f, 0.9f),
        fallbackEmission = new Color(0.4f, 0.7f, 1.2f) * 2.0f,
        densityPerSqM = 0.02f,
        minSpacing = 2.2f,
        scaleRange = new Vector2(0.4f, 1.4f),
        normalAlignment = 0.9f,
        yawRange = new Vector2(0f, 360f),
        surfaceEmbed = -0.3f,
        attachedLightRange = 6f,
        attachedLightIntensity = 1.4f,
        seedSalt = 2003,
    };

    /// <summary>Fleshy throbbing blobs that cluster near liquid pools.</summary>
    public static DecorationRule PulsingNodules() => new DecorationRule
    {
        name = "PulsingNodules",
        surfaceType = DecorationSurfaceType.Any,
        fallbackPrimitive = PrimitiveType.Sphere,
        fallbackColor = new Color(0.5f, 0.1f, 0.3f),
        fallbackEmission = new Color(1.0f, 0.2f, 0.4f) * 1.0f,
        densityPerSqM = 0.08f,
        minSpacing = 0.7f,
        scaleRange = new Vector2(0.2f, 0.6f),
        normalAlignment = 0.6f,
        surfaceEmbed = -0.05f,
        liquidProximityBoost = 6f,
        seedSalt = 2004,
    };

    /// <summary>Generic moss/fungal scatter for slightly wet caves.</summary>
    public static DecorationRule AlienMoss() => new DecorationRule
    {
        name = "AlienMoss",
        surfaceType = DecorationSurfaceType.Any,
        fallbackPrimitive = PrimitiveType.Sphere,
        fallbackColor = new Color(0.25f, 0.45f, 0.30f),
        fallbackEmission = new Color(0.1f, 0.3f, 0.15f) * 0.3f,
        densityPerSqM = 0.35f,
        minSpacing = 0.4f,
        scaleRange = new Vector2(0.15f, 0.4f),
        normalAlignment = 0.4f,
        surfaceEmbed = -0.05f,
        liquidProximityBoost = 8f,
        seedSalt = 2005,
    };
}
