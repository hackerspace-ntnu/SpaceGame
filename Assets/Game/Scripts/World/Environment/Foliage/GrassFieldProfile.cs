using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One grass biome: what the grass looks like and how far it reaches — the tuft shape, its
    /// height and how it clumps, how densely it is scattered, and the streaming radius. The
    /// colours live on <see cref="Material"/>. A <see cref="GrassField"/> grows each biome on the
    /// terrain layer it is paired with.
    /// </summary>
    [CreateAssetMenu(menuName = "World/Grass Profile", fileName = "GrassProfile")]
    public class GrassFieldProfile : ScriptableObject
    {
        [Header("Look")]
        [Tooltip("A material using SpaceGame/StylizedGrass. Its root colour should match the " +
                 "terrain layer the grass grows on.")]
        [SerializeField] private Material material;

        [Header("Placement")]
        [Tooltip("Tufts per square metre where the grass layer is fully painted.")]
        [SerializeField, Min(0.1f)] private float tuftsPerSquareMetre = 9f;

        [Tooltip("Grass thins out below this terrain-layer weight and is gone at half of it, " +
                 "so the edge of a painted area frays instead of ending in a hard line.")]
        [SerializeField, Range(0.05f, 1f)] private float minimumLayerWeight = 0.5f;

        [Tooltip("No grass on ground steeper than this.")]
        [SerializeField, Range(0f, 90f)] private float maxSlopeDegrees = 38f;

        [Tooltip("Tuft height range in metres. The player is about 3 m tall.")]
        [SerializeField] private Vector2 heightRange = new Vector2(0.9f, 1.35f);

        [Tooltip("0 picks every tuft's height at random within the range; 1 groups them into " +
                 "short and tall clumps, so the field has stands of long grass in it.")]
        [SerializeField, Range(0f, 1f)] private float heightClumping = 0.35f;

        [Tooltip("Size of the short and tall clumps in metres.")]
        [SerializeField, Min(0.5f)] private float heightClumpSize = 6f;

        [Tooltip("Fraction of tufts that carry a flower at the tip of one blade.")]
        [SerializeField, Range(0f, 0.2f)] private float flowerChance = 0.015f;

        [Header("Natural Patches")]
        [Tooltip("How far, in paint texels, the edge of a painted area wanders with noise, so it " +
                 "ends in an organic outline instead of the paint map's squares.")]
        [SerializeField, Range(0f, 2f)] private float edgeWarp = 1f;
        [Tooltip("How much the density swells and thins across a field: 0 is even, 1 opens " +
                 "clearings between thick clumps.")]
        [SerializeField, Range(0f, 1f)] private float densityVariation = 0.35f;
        [Tooltip("Size of the thick clumps and clearings in metres.")]
        [SerializeField, Min(1f)] private float densityVariationSize = 14f;

        [Header("Ground Colour")]
        [Tooltip("How strongly painted ground takes this grass's colour, so the field still reads " +
                 "from afar once its tufts have sunk away. High for carpets, low for sparse grass " +
                 "over sand or stone. Shown by terrain shaders that read it (StylizedTerrain).")]
        [SerializeField, Range(0f, 1f)] private float groundTint = 0.85f;

        [Header("Tuft Shape (fractions of the tuft height)")]
        [Tooltip("Optional authored tuft (Y up, origin at the root, Read/Write enabled). It is " +
                 "rescaled to height 1; its normals are kept and mixed with the ground's by the " +
                 "material's Blade Facing. Empty builds one from the settings below.")]
        [SerializeField] private Mesh tuftMesh;
        [SerializeField, Range(1, 24)] private int bladesPerTuft = 5;
        [Tooltip("Segments per blade. More bends more smoothly and costs four triangles each.")]
        [SerializeField, Range(1, 6)] private int bladeSegments = 3;
        [Tooltip("Widest point of a blade, a third of the way up. Broad leaves overlap into scales.")]
        [SerializeField, Range(0.02f, 0.8f)] private float bladeWidth = 0.42f;
        [Tooltip("How far the spine is pushed out from the edges, so the two halves of a blade " +
                 "face slightly different ways and catch the light differently.")]
        [SerializeField, Range(0f, 1f)] private float bladeFold = 0.3f;
        [SerializeField, Range(0f, 0.6f)] private float tuftSpread = 0.35f;
        [SerializeField, Range(0f, 1f)] private float bladeLean = 0.35f;

        [Header("Detail Levels (switch distance is jittered ±10% per tuft)")]
        [Tooltip("Tufts farther than this draw every blade as one folded segment: the same " +
                 "blades and shading at a fraction of the triangles.")]
        [SerializeField, Min(0f)] private float midTuftDistance = 8f;
        [Tooltip("Tufts farther than this draw the far tuft: a few flat two-triangle blades.")]
        [SerializeField, Min(0f)] private float farTuftDistance = 20f;
        [SerializeField, Range(1, 12)] private int farBladesPerTuft = 3;
        [Tooltip("Far blades are wider, so the thinner tuft still covers the ground.")]
        [SerializeField, Range(1f, 4f)] private float farBladeWidthScale = 1.3f;
        [Tooltip("Tufts farther than this are one triangle turned to the camera: a tuft's " +
                 "silhouette for a sixth of the far tuft's cost, which is what lets grass reach far.")]
        [SerializeField, Min(0f)] private float distantTuftDistance = 40f;
        [Tooltip("Width of the distant triangle's base, as a fraction of the tuft height.")]
        [SerializeField, Range(0.3f, 3f)] private float distantTuftWidth = 1.2f;

        [Header("Streaming")]
        [Tooltip("Side of one streamed chunk in metres: the unit the grass is scattered, kept " +
                 "and released in. It does not change the draw calls.")]
        [SerializeField, Min(4f)] private float chunkSize = 16f;

        [Header("Distance")]
        [Tooltip("Beyond this the tufts have fully sunk into the ground and are not drawn; the " +
                 "ground colour carries the field on from here.")]
        [SerializeField, Min(5f)] private float drawDistance = 300f;

        [Tooltip("Tufts start sinking at this fraction of the draw distance.")]
        [SerializeField, Range(0.2f, 0.95f)] private float fadeStartFraction = 0.8f;

        [Tooltip("Every tuft is drawn out to here. Beyond it the field thins with distance, so " +
                 "far grass costs about as much as near grass.")]
        [SerializeField, Min(1f)] private float fullDensityDistance = 30f;

        [Tooltip("How fast the field thins beyond Full Density Distance: density falls as " +
                 "(full / distance) to this power. 2 keeps the number of tufts on screen about " +
                 "the same at every distance; higher thins faster. 2.6 keeps a 300 m meadow " +
                 "near 1 ms (M1 Pro, 1080p), the ground colour filling in behind.")]
        [SerializeField, Range(1f, 4f)] private float densityFalloff = 2.6f;

        [Tooltip("Thinned tufts are widened to keep the field looking full, up to this many " +
                 "times their width.")]
        [SerializeField, Range(1f, 4f)] private float maxWidthBoost = 3f;

        /// <summary>Raised when a profile is edited in the Inspector, so the grass using it rebuilds live.</summary>
        public static event System.Action<GrassFieldProfile> Edited;

        private void OnValidate() => Edited?.Invoke(this);

        public Material Material => material;
        public float TuftsPerSquareMetre => tuftsPerSquareMetre;
        public float MinimumLayerWeight => minimumLayerWeight;
        public float MaxSlopeDegrees => maxSlopeDegrees;
        public Vector2 HeightRange => heightRange;
        public float HeightClumping => heightClumping;
        public float HeightClumpSize => heightClumpSize;
        public float FlowerChance => flowerChance;
        public float EdgeWarp => edgeWarp;
        public float DensityVariation => densityVariation;
        public float DensityVariationSize => densityVariationSize;
        public float GroundTint => groundTint;
        public Mesh TuftMesh => tuftMesh;
        public int BladesPerTuft => bladesPerTuft;
        public int BladeSegments => bladeSegments;
        public float BladeWidth => bladeWidth;
        public float BladeFold => bladeFold;
        public float MidTuftDistance => midTuftDistance;
        public float FarTuftDistance => farTuftDistance;
        public int FarBladesPerTuft => farBladesPerTuft;
        public float FarBladeWidthScale => farBladeWidthScale;
        public float DistantTuftDistance => distantTuftDistance;
        public float DistantTuftWidth => distantTuftWidth;
        public float TuftSpread => tuftSpread;
        public float BladeLean => bladeLean;
        public float ChunkSize => chunkSize;
        public float DrawDistance => drawDistance;
        public float FadeStart => drawDistance * fadeStartFraction;
        public float FullDensityDistance => fullDensityDistance;
        public float DensityFalloff => densityFalloff;
        public float MaxWidthBoost => maxWidthBoost;

        /// <summary>
        /// The fraction of tufts drawn at <paramref name="range"/> metres from the camera — the
        /// same curve GrassCull.compute thins by.
        /// </summary>
        public float DensityAt(float range) =>
            range <= fullDensityDistance ? 1f : Mathf.Pow(fullDensityDistance / range, densityFalloff);
    }
}
