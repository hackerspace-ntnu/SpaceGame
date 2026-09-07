using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The ink lines drawn over the frame before it snaps to the palette — the pen on top
    /// of the watercolour.
    ///
    /// <para>
    /// Two edge sources, combined with <c>max</c> so a line that both find is not drawn
    /// twice as dark: a gradient in perceptual lightness, which follows shading and gives
    /// the interior strokes on a rock face, and a gradient in scene depth, which gives the
    /// silhouettes an unlit edge would miss.
    /// </para>
    ///
    /// <para>
    /// It darkens Oklab lightness <em>before</em> the snap rather than compositing a black
    /// line after it, so a line lands on whatever palette entry sits a step or two below
    /// the colour it crosses. The look keeps a strictly closed palette, and the ink takes
    /// the hue of what it is drawn over the way a wash does.
    /// </para>
    /// </summary>
    [System.Serializable]
    public struct InkShape
    {
        [Tooltip("Oklab lightness subtracted at a full-strength edge. One lightness step " +
                 "is ~0.10, so 0.22 is about two palette entries darker.")]
        [Range(0f, 0.6f)] public float amount;

        [Tooltip("Lightness gradient between neighbouring pixels that counts as an edge. " +
                 "Lower draws more interior detail; too low and every shading ramp inks.")]
        [Range(0.002f, 0.2f)] public float lumaThreshold;

        [Tooltip("Relative depth gradient that counts as a silhouette. Relative, so a " +
                 "line does not thicken or vanish with distance.")]
        [Range(0.002f, 0.2f)] public float depthThreshold;

        [Tooltip("Width of the ramp above each threshold. Small is a crisp pen line, " +
                 "large is a soft pencil one.")]
        [Range(0.001f, 0.2f)] public float softness;

        public static InkShape Default => new InkShape
        {
            amount = 0.22f,
            lumaThreshold = 0.03f,
            depthThreshold = 0.02f,
            softness = 0.03f,
        };
    }
}
