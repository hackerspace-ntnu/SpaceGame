using UnityEngine;

/// <summary>
/// A single decoration scatter rule — "place these prefabs on these surfaces with this density,
/// subject to these constraints." A <see cref="CaveDecorationSettings"/> holds an array of rules;
/// each rule is evaluated independently by <see cref="DecorationScatterer"/>.
///
/// Design notes:
///   • Rules are data, not behaviour. The scatterer interprets them.
///   • If <see cref="prefabs"/> is empty, the scatterer emits a procedural primitive coloured
///     <see cref="fallbackColor"/> at the configured scale — so you can iterate on placement
///     before any art exists.
///   • Density is "samples per square metre of eligible surface area". Spacing then thins those
///     samples so two scatter points are never closer than <see cref="minSpacing"/>.
///   • All randomisation derives from the cave seed + the rule's own salt so re-generating with
///     the same seed produces identical decor placement.
/// </summary>
[System.Serializable]
public class DecorationRule
{
    [Tooltip("Display name shown in inspector / debug logs.")]
    public string name = "Decoration";

    [Tooltip("Set false to skip this rule entirely (useful for A/B-testing without removing the entry).")]
    public bool enabled = true;

    [Header("Prefabs")]
    [Tooltip("Prefabs to randomly pick from. Empty = procedural primitive fallback so you can see placement before art exists.")]
    public GameObject[] prefabs;

    [Tooltip("Colour used for the procedural fallback primitive when no prefab is assigned.")]
    public Color fallbackColor = new Color(0.55f, 0.45f, 0.35f);

    [Tooltip("Procedural fallback emission colour. Black = no emission.")]
    public Color fallbackEmission = Color.black;

    [Tooltip("Procedural fallback primitive shape.")]
    public PrimitiveType fallbackPrimitive = PrimitiveType.Cylinder;

    [Header("Where")]
    [Tooltip("Which surfaces this rule may spawn on.")]
    public DecorationSurfaceType surfaceType = DecorationSurfaceType.Floor;

    [Tooltip("Per-room-kind filter. Empty = any room/corridor.")]
    public RoomKind[] allowedRoomKinds;

    [Tooltip("Minimum world-space Y. -inf to disable.")]
    public float minWorldY = -1e6f;

    [Tooltip("Maximum world-space Y. +inf to disable.")]
    public float maxWorldY = 1e6f;

    [Tooltip("Maximum allowed surface tilt (radians, 0 = perfectly aligned). Used to reject scatter points on steep floors / slanted ceilings.")]
    [Range(0f, 1.57f)] public float maxSurfaceTilt = 1.0f;

    [Tooltip("If > 0, prefer points within this distance of any liquid pool waterline (vegetation hugging water).")]
    public float liquidProximityBoost = 0f;

    [Header("How many")]
    [Tooltip("Approximate samples per square metre of eligible surface. 0.05–0.2 = sparse, 0.5+ = dense moss.")]
    [Range(0f, 4f)] public float densityPerSqM = 0.1f;

    [Tooltip("Minimum distance (metres) between two placed instances. Acts as a Poisson thinning step.")]
    public float minSpacing = 1.0f;

    [Tooltip("Hard cap on instance count for this rule, regardless of density. 0 = no cap.")]
    public int maxInstances = 0;

    [Header("Clumping (natural-looking patches)")]
    [Tooltip("Spatial frequency of the density-modulation noise. 0 = uniform scatter (default). 0.05–0.15 = nice clumps. Higher = smaller patches. The noise is sampled at each candidate point's XZ position and used as an acceptance probability bonus — high-noise areas get dense scatter, low-noise areas stay empty.")]
    [Range(0f, 0.5f)] public float densityNoiseScale = 0f;

    [Tooltip("How sharp the clump boundaries are. 0 = soft gradient (some thinning in empty zones). 1 = hard cutoff (empty zones completely bare). 0.5 is a good default.")]
    [Range(0f, 1f)] public float densityNoiseContrast = 0.5f;

    [Header("Transform")]
    [Tooltip("Uniform scale range applied to each instance.")]
    public Vector2 scaleRange = new Vector2(0.7f, 1.4f);

    [Tooltip("How strongly the prefab's up axis aligns to the surface normal. 0 = vertical (world up), 1 = matches normal exactly.")]
    [Range(0f, 1f)] public float normalAlignment = 1f;

    [Tooltip("Random yaw rotation range (degrees).")]
    public Vector2 yawRange = new Vector2(0f, 360f);

    [Tooltip("How far each instance is pushed into the wall / floor along the inward normal. Negative = away from wall.")]
    public float surfaceEmbed = 0f;

    [Header("Lighting (procedural-fallback only)")]
    [Tooltip("If > 0, attaches a point light to each placed instance. Color = fallbackEmission.")]
    public float attachedLightRange = 0f;

    [Tooltip("Intensity of any attached light.")]
    public float attachedLightIntensity = 1f;

    [Header("Seeding")]
    [Tooltip("Per-rule salt mixed into the seed. Change this to re-roll one rule's placement without changing others.")]
    public int seedSalt = 0;
}
