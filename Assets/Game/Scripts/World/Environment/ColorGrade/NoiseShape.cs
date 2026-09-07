using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Which mark the noise pass draws. Off by default, and each kind is a different answer
    /// to a different complaint, not a stack of effects to layer.
    /// </summary>
    public enum NoiseKind
    {
        /// <summary>No noise pass at all — the committed look.</summary>
        None = 0,

        /// <summary>Soft two-octave mottling, as if the frame were painted on paper. It
        /// gives flat fields a tooth without adding anything that reads as an object.
        /// Anchored to the screen, so it sits on the image the way paper does rather than
        /// on the surfaces in it.</summary>
        Paper = 1,

        /// <summary>A per-pixel offset applied before the snap, so a gradient too gentle to
        /// cross a palette boundary breaks into a stipple of the two entries either side of
        /// it instead of a hard band. Use this on skies, which is where the banding is.
        /// </summary>
        Dither = 2,

        /// <summary>Sparse dark flecks. Ink spatter rather than texture.</summary>
        Speckle = 3,
    }

    /// <summary>
    /// A noise field applied before the palette snap.
    ///
    /// <para>
    /// Before, deliberately: the snap resolves nothing finer than half a lightness step, so
    /// anything composited after it is either invisible or off-palette. Moving lightness
    /// first means the noise decides *which entry* a pixel lands on, and the result stays
    /// inside the palette.
    /// </para>
    ///
    /// <para>
    /// Every kind here is anchored to the screen rather than to the world. That is a
    /// deliberate limit rather than an oversight: world-anchored surface marks were tried
    /// and rejected twice, and screen-anchored noise only reads honestly when it is meant
    /// to be *on the picture* — paper, print, spatter — not on the terrain.
    /// </para>
    /// </summary>
    [System.Serializable]
    public struct NoiseShape
    {
        public NoiseKind kind;

        [Tooltip("Peak lightness shift in Oklab L. Half a lightness step is ~0.05, which is " +
                 "the point at which every pixel can reach its neighbouring palette entry.")]
        [Range(0f, 0.15f)] public float amount;

        [Tooltip("Screen pixels per noise cell. Ignored by Dither, which is per-pixel by " +
                 "definition.")]
        [Range(1f, 32f)] public float scale;

        [Tooltip("Speckle only: the fraction of cells that carry a fleck.")]
        [Range(0f, 1f)] public float density;

        public static NoiseShape Default => new NoiseShape
        {
            kind = NoiseKind.None,
            amount = 0.03f,
            scale = 6f,
            density = 0.15f,
        };
    }
}
