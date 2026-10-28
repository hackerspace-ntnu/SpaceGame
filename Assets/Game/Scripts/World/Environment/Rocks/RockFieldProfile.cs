using System;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One rock look — say Red Desert — and everything that grows where it is painted: a few
    /// <see cref="RockSizeClass"/>es (small, big, massive), each with its own models, sizes,
    /// spacing and reach, all clumping on the same patch noise so the scree gathers where the
    /// boulders are. The colours live on the variants' materials (SpaceGame/StylizedRock).
    /// A <see cref="RockField"/> grows each profile on the terrain detail layer it is paired with.
    /// </summary>
    [CreateAssetMenu(menuName = "World/Rock Profile", fileName = "RockProfile")]
    public class RockFieldProfile : ScriptableObject
    {
        [Header("Placement")]
        [Tooltip("The paint strength (the brush's Target Strength) that grows every rock. Weaker " +
                 "paint grows proportionally fewer: at 1, a 5% stroke grows 5% of them.")]
        [SerializeField, Range(0.05f, 1f)] private float fullDensityPaint = 1f;

        [Tooltip("How far, in paint cells, the edge of a painted area wanders with noise.")]
        [SerializeField, Range(0f, 2f)] private float edgeWarp = 1f;

        [Tooltip("No rocks on ground steeper than this.")]
        [SerializeField, Range(0f, 90f)] private float maxSlopeDegrees = 60f;

        [Header("Natural Patches")]
        [Tooltip("0 spreads the rocks evenly; 1 gathers every size class into the same patches, " +
                 "with bare ground between them.")]
        [SerializeField, Range(0f, 1f)] private float clumping = 0.6f;

        [Tooltip("Size of the patches in metres.")]
        [SerializeField, Min(1f)] private float clumpSize = 22f;

        [Tooltip("How much bigger the rocks in a patch's heart are than those at its edge, as a " +
                 "share of each class's size range.")]
        [SerializeField, Range(0f, 1f)] private float sizeClumping = 0.4f;

        [Header("Size Classes")]
        [SerializeField] private RockSizeClass[] sizeClasses = Array.Empty<RockSizeClass>();

        /// <summary>Raised when a profile is edited in the Inspector, so the rocks using it rebuild live.</summary>
        public static event Action<RockFieldProfile> Edited;

        private void OnValidate() => Edited?.Invoke(this);

        public float FullDensityPaint => fullDensityPaint;
        public float EdgeWarp => edgeWarp;
        public float MaxSlopeDegrees => maxSlopeDegrees;
        public float Clumping => clumping;
        public float ClumpSize => clumpSize;
        public float SizeClumping => sizeClumping;
        public RockSizeClass[] SizeClasses => sizeClasses;
    }

    /// <summary>
    /// One size of rock within a profile: which models, how big, how many and how far they are
    /// drawn. Each class streams on its own, so pebbles keep a short reach while a massive
    /// boulder stays on the horizon.
    /// </summary>
    [Serializable]
    public class RockSizeClass
    {
        [Tooltip("Only for the Inspector.")]
        [SerializeField] private string name = "Rocks";

        [Tooltip("The models this class picks from, by weight. Each mesh's origin is its base " +
                 "(Y up); generated Mesh LODs are drawn by distance.")]
        [SerializeField] private RockVariant[] variants = Array.Empty<RockVariant>();

        [Header("Size")]
        [Tooltip("Rock width in metres (its widest horizontal extent). The player is about 3 m tall.")]
        [SerializeField] private Vector2 widthRange = new Vector2(0.3f, 0.8f);

        [Tooltip("Each axis is stretched by up to this fraction either way, so one model gives " +
                 "squat, tall, long and narrow rocks.")]
        [SerializeField, Range(0f, 0.6f)] private float stretch = 0.25f;

        [Tooltip("How much of each rock's height is buried, so it sits in the ground.")]
        [SerializeField, Range(0f, 0.8f)] private float sink = 0.15f;

        [Header("Placement")]
        [Tooltip("One rock at most per cell of this size (metres), jittered within it.")]
        [SerializeField, Min(0.25f)] private float spacing = 2f;

        [Tooltip("Share of the cells that hold a rock where the paint is full.")]
        [SerializeField, Range(0f, 1f)] private float chance = 0.4f;

        [Tooltip("0 stands every rock upright; 1 tilts it with the ground under it.")]
        [SerializeField, Range(0f, 1f)] private float slopeAlign = 0.6f;

        [Tooltip("A random extra tilt, in degrees.")]
        [SerializeField, Range(0f, 45f)] private float maxTiltDegrees = 12f;

        [Header("Distance")]
        [Tooltip("Rocks are not drawn past this, and no chunk is kept past it.")]
        [SerializeField, Min(5f)] private float drawDistance = 80f;

        [Tooltip("Rocks rise out of the ground over this last share of the draw distance, eased, " +
                 "instead of appearing at full size.")]
        [SerializeField, Range(0.02f, 0.6f)] private float fadeBand = 0.25f;

        [Tooltip("A rock smaller than this share of the screen height is not drawn; it shrinks " +
                 "away just before, so the cut-off is not a pop.")]
        [SerializeField, Range(0.0005f, 0.05f)] private float cullScreenSize = 0.006f;

        [Tooltip("Below this share of the screen height a rock draws its Mesh LOD 1.")]
        [SerializeField, Range(0.001f, 1f)] private float lod1ScreenSize = 0.12f;

        [Tooltip("Below this share of the screen height a rock draws its Mesh LOD 2.")]
        [SerializeField, Range(0.001f, 1f)] private float lod2ScreenSize = 0.04f;

        [Tooltip("Side of one streamed chunk in metres: the unit this class is scattered, kept " +
                 "and released in.")]
        [SerializeField, Min(4f)] private float chunkSize = 24f;

        [Header("Physics and Shadows")]
        [SerializeField] private bool castShadows = true;

        [Tooltip("Rocks of this class get a collider when they are near a player.")]
        [SerializeField] private bool collides;

        public string Name => name;
        public RockVariant[] Variants => variants;
        public Vector2 WidthRange => widthRange;
        public float Stretch => stretch;
        public float Sink => sink;
        public float Spacing => spacing;
        public float Chance => chance;
        public float SlopeAlign => slopeAlign;
        public float MaxTiltDegrees => maxTiltDegrees;
        public float DrawDistance => drawDistance;
        public float FadeBand => fadeBand;
        public float CullScreenSize => cullScreenSize;
        public float Lod1ScreenSize => lod1ScreenSize;
        public float Lod2ScreenSize => lod2ScreenSize;
        public float ChunkSize => chunkSize;
        public bool CastShadows => castShadows;
        public bool Collides => collides;
    }

    /// <summary>One model a size class can pick, with the material that colours it.</summary>
    [Serializable]
    public struct RockVariant
    {
        public Mesh mesh;

        [Tooltip("A material using SpaceGame/StylizedRock.")]
        public Material material;

        [Min(0f)] public float weight;
    }
}
