using UnityEngine;

public enum MapMarkerType
{
    Neutral,
    Quest,
    Hostile,
    Friendly,
    Discovery
}

public static class MapMarkerColors
{
    public static Color For(MapMarkerType type) => type switch
    {
        MapMarkerType.Quest     => new Color(1.00f, 0.85f, 0.20f),
        MapMarkerType.Hostile   => new Color(1.00f, 0.30f, 0.30f),
        MapMarkerType.Friendly  => new Color(0.30f, 1.00f, 0.50f),
        MapMarkerType.Discovery => new Color(0.85f, 0.55f, 1.00f),
        _                       => new Color(0.85f, 0.95f, 1.00f),
    };
}
