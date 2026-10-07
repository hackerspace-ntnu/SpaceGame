using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// How every canvas in the game scales, and the one place that says so.
    ///
    /// <para>
    /// The project used to answer this question in four different ways at once: the authored
    /// canvases matched WIDTH, thirteen runtime-built ones matched width and height equally, three
    /// more set a reference resolution and left the match at Unity's default, and two had no scaler
    /// at all. Screens drawn at the same moment therefore disagreed about how big a pixel was on
    /// anything but a 16:9 monitor, and two lobby helpers re-derived the conversion from a constant
    /// that only ever described one of those four rules. Everything here exists so that question has
    /// exactly one answer.
    /// </para>
    ///
    /// <para>
    /// The rule is <see cref="CanvasScaler.ScreenMatchMode.Expand"/>: scale by whichever of
    /// width/1920 and height/1080 is SMALLER, so the canvas is never smaller than the reference on
    /// either axis. Two consequences are worth knowing before laying anything out:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>At 16:9 and at every WIDER aspect the canvas is exactly 1080 tall and only its width
    /// grows, so a layout authored at 1920x1080 is reproduced pixel for pixel. This is what makes an
    /// ultrawide monitor show the same UI as a laptop rather than a squashed one.</item>
    /// <item>At an aspect NARROWER than 16:9 the canvas is exactly 1920 wide and grows taller. Extra
    /// height appears between the top and bottom edges, so anything measured from an edge stays put
    /// and anything that has to line up with the 3D set behind it does not — see
    /// <see cref="MenuEntry.ContentTopFor"/>.</item>
    /// </list>
    ///
    /// <para>
    /// Matching WIDTH instead — the rule the menu was authored with — keeps the canvas 1920 wide and
    /// lets its HEIGHT collapse on a wide monitor: 810 canvas pixels on a 21:9 screen against the
    /// 1080 everything is measured in. That is what used to squeeze a menu page's vertical budget on
    /// exactly the monitors most likely to be playing.
    /// </para>
    /// </summary>
    public static class UIScale
    {
        /// <summary>
        /// The resolution every layout in the project is authored against, and the size the canvas
        /// is guaranteed to be at least.
        /// </summary>
        public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        /// <summary>
        /// Points the scaler at the one rule. Call this instead of writing the four properties by
        /// hand — every place that wrote them by hand is how the project ended up with four rules.
        /// </summary>
        public static void Configure(CanvasScaler scaler)
        {
            if (scaler == null) return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        /// <summary>
        /// Adds a scaler to <paramref name="host"/> if it has none, configures it either way, and
        /// hands it back. For a canvas built from a <c>GameObject</c> constructor that already lists
        /// <c>typeof(CanvasScaler)</c>, and for one that does not.
        /// </summary>
        public static CanvasScaler Apply(GameObject host)
        {
            if (host == null) return null;

            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = host.AddComponent<CanvasScaler>();

            Configure(scaler);
            return scaler;
        }

        /// <summary>
        /// How many screen pixels one canvas pixel is worth, on a screen of the given size.
        ///
        /// The same number Unity's own scaler arrives at, computed rather than read so it can be
        /// reasoned about before a canvas exists and asserted in a test without one. A scaler
        /// updates during layout, so a canvas built this frame has not necessarily been given its
        /// factor yet — asking here is both earlier and cheaper than waiting for that.
        /// </summary>
        public static float ScaleFactor(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f) return 1f;

            return Mathf.Min(screenWidth / ReferenceResolution.x, screenHeight / ReferenceResolution.y);
        }

        /// <summary>
        /// The canvas's own size, in canvas pixels, on a screen of the given size.
        ///
        /// A screen with no size at all — which is what a headless run reports — answers with the
        /// reference rather than with zero, so a layout asking how much room it has gets the shape
        /// it was authored for instead of a degenerate one.
        /// </summary>
        public static Vector2 CanvasSize(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f) return ReferenceResolution;

            float scale = ScaleFactor(screenWidth, screenHeight);

            return new Vector2(screenWidth / scale, screenHeight / scale);
        }

        /// <summary>The canvas's own size on the screen the game is actually running on.</summary>
        public static Vector2 CanvasSize() => CanvasSize(Screen.width, Screen.height);
    }
}
