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
                 "is ~0.12, so 0.22 is about two palette entries darker.")]
        [Range(0f, 0.6f)] public float amount;

        [Tooltip("Line breadth, in screen pixels. This is the distance the edge detector " +
                 "reaches, so a wider line also finds gentler edges — breadth and how much " +
                 "gets outlined are one dial, not two.")]
        [Range(0.5f, 6f)] public float width;

        [Tooltip("The pen's colour. It only reaches the frame through `tint`; at tint 0 " +
                 "the line is a darker shade of whatever it crosses instead.")]
        [ColorUsage(false)] public Color color;

        [Tooltip("How far a line is pulled toward `color` rather than simply darkened. 0 " +
                 "keeps a line the hue of the surface under it, the way a wash does; 1 " +
                 "makes every line the same ink.")]
        [Range(0f, 1f)] public float tint;

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
            width = 1f,
            color = new Color(0.11f, 0.12f, 0.17f),
            tint = 0f,
            lumaThreshold = 0.03f,
            depthThreshold = 0.02f,
            softness = 0.03f,
        };
    }
}
