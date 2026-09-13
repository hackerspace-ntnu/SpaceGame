// Tests for the one rule every canvas in the project scales by.
//
// The bug these were written against: the project answered "how big is a canvas pixel" four
// different ways at once — the authored canvases matched width, thirteen runtime-built ones matched
// width and height equally, three set a reference resolution and left the match at Unity's default,
// and two had no scaler at all. Two lobby helpers then converted between screen and canvas pixels
// with a constant describing only one of those four answers, so the Versus rank's nameplates came
// out about 15% too small on a 21:9 monitor and too large on a window narrower than 16:9.
//
// Nothing here needs a canvas: UIScale computes the same numbers Unity's scaler arrives at, which is
// exactly what makes them assertable without one.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    public class UIScalingTests
    {
        /// <summary>
        /// Real screens, not round numbers: two ultrawides, a 4K and a 1080p 16:9, a MacBook's
        /// 16:10, a Surface's 3:2, and the two narrow aspects that are the only ones where the
        /// canvas grows taller than the reference.
        /// </summary>
        private static readonly (int Width, int Height, string Name)[] Screens =
        {
            (3440, 1440, "21:9 ultrawide"),
            (2560, 1080, "21:9 1080p ultrawide"),
            (3840, 2160, "4K 16:9"),
            (1920, 1080, "1080p 16:9"),
            (2560, 1600, "16:10 laptop"),
            (2256, 1504, "3:2"),
            (1600, 1200, "4:3"),
            (1280, 1024, "5:4"),
        };

        /// <summary>
        /// The property the whole change rests on: a layout authored at 1920x1080 always fits,
        /// because the canvas is never smaller than that on either axis.
        ///
        /// This is what a width-matching or a half-matching scaler cannot promise — both let the
        /// canvas HEIGHT collapse below 1080 on a wide monitor, which is how a page's vertical
        /// budget used to shrink on exactly the screens most likely to be playing.
        /// </summary>
        [Test]
        public void TheCanvasIsNeverSmallerThanTheReference()
        {
            foreach ((int width, int height, string name) in Screens)
            {
                Vector2 canvas = UIScale.CanvasSize(width, height);

                Assert.GreaterOrEqual(canvas.x, UIScale.ReferenceResolution.x - 0.01f,
                    $"{name} gets a canvas only {canvas.x:0} wide, so a layout authored at " +
                    $"{UIScale.ReferenceResolution.x:0} px wide is cut off.");

                Assert.GreaterOrEqual(canvas.y, UIScale.ReferenceResolution.y - 0.01f,
                    $"{name} gets a canvas only {canvas.y:0} tall, so a layout authored at " +
                    $"{UIScale.ReferenceResolution.y:0} px tall is cut off.");
            }
        }

        /// <summary>
        /// At 16:9 and at every wider aspect the canvas is exactly the authored height, so the UI is
        /// reproduced pixel for pixel and an ultrawide monitor shows the same thing a laptop does.
        ///
        /// This is the assertion that would have caught the reported bug directly: under the old
        /// half-matching rule a 2560x1080 screen got a 943-tall canvas against the 1080 every offset
        /// in the project is measured in.
        /// </summary>
        [Test]
        public void SixteenNineAndWiderGetTheAuthoredCanvasHeight()
        {
            foreach ((int width, int height, string name) in Screens)
            {
                if ((float)width / height < UIScale.ReferenceResolution.x / UIScale.ReferenceResolution.y)
                    continue;

                Assert.AreEqual(UIScale.ReferenceResolution.y, UIScale.CanvasSize(width, height).y, 0.01f,
                    $"{name} must get the authored canvas height, or every offset measured from " +
                    "the top of the page lands somewhere else than it was authored.");
            }
        }

        /// <summary>
        /// The scale factor and the canvas size have to be two ways of saying the same thing, or
        /// anything converting between screen and canvas pixels picks the wrong one.
        /// </summary>
        [Test]
        public void TheScaleFactorAndTheCanvasSizeAgree()
        {
            foreach ((int width, int height, string name) in Screens)
            {
                float scale = UIScale.ScaleFactor(width, height);
                Vector2 canvas = UIScale.CanvasSize(width, height);

                Assert.AreEqual(width, canvas.x * scale, 0.01f, $"{name}: width disagrees");
                Assert.AreEqual(height, canvas.y * scale, 0.01f, $"{name}: height disagrees");
            }
        }

        /// <summary>
        /// Menu entries are dark navy and only read against sand, so the first row of content has to
        /// stay below the skyline at every aspect.
        ///
        /// The skyline sits at a fixed fraction of the frame — fixed camera pitch, fixed vertical
        /// field of view — while the content offset is a fixed number of pixels down from the top.
        /// Those agree at one aspect only, and on a canvas grown taller than the reference the fixed
        /// offset rises above the skyline: a 5:4 canvas is 1536 tall, where an unscaled -560 would
        /// put the entries about 50 px into the sky.
        /// </summary>
        [Test]
        public void ContentStaysBelowTheHorizonAtEveryAspect()
        {
            foreach ((int width, int height, string name) in Screens)
            {
                float canvasHeight = UIScale.CanvasSize(width, height).y;

                float contentFromTop = -MenuEntry.ContentTopFor(canvasHeight);
                float horizonFromTop = MenuEntry.HorizonFor(canvasHeight);

                Assert.GreaterOrEqual(contentFromTop, horizonFromTop,
                    $"On {name} the first row of content sits {horizonFromTop - contentFromTop:0} px " +
                    "above the skyline, where dark navy entries are drawn over bright sky.");
            }
        }

        /// <summary>
        /// The authored offsets are unchanged on every screen 16:9 or wider, which is what makes
        /// this change a fix rather than a re-layout: the shot the menu was composed against is
        /// reproduced exactly, and only a narrower window sees anything move.
        /// </summary>
        [Test]
        public void SixteenNineAndWiderKeepTheAuthoredOffsets()
        {
            float reference = UIScale.ReferenceResolution.y;

            foreach ((int width, int height, string name) in Screens)
            {
                if ((float)width / height < UIScale.ReferenceResolution.x / reference) continue;

                float canvasHeight = UIScale.CanvasSize(width, height).y;

                Assert.AreEqual(MenuEntry.ContentTopFor(reference), MenuEntry.ContentTopFor(canvasHeight), 0.01f,
                    $"{name} moved the content band away from where the page was authored.");
                Assert.AreEqual(MenuEntry.HorizonFor(reference), MenuEntry.HorizonFor(canvasHeight), 0.01f,
                    $"{name} moved the skyline away from where the page was authored.");
            }
        }

        /// <summary>
        /// Nothing outside <see cref="UIScale"/> configures a scaler by hand.
        ///
        /// Read from the source rather than from a live canvas because the failure is invisible at
        /// runtime: a screen with its own opinion about the match mode renders perfectly well, and
        /// simply disagrees with the screen drawn beside it about how big a pixel is. Four rules is
        /// what the project had, and no single screen looked wrong.
        /// </summary>
        [Test]
        public void EveryCanvasIsConfiguredThroughUIScale()
        {
            var offenders = new List<string>();

            foreach (string path in ScriptPaths())
            {
                string source = File.ReadAllText(path);

                if (Regex.IsMatch(source, @"scaler\.(uiScaleMode|referenceResolution|screenMatchMode|matchWidthOrHeight)\s*=")
                    || Regex.IsMatch(source, @"AddComponent<CanvasScaler>"))
                    offenders.Add(Path.GetFileName(path));
            }

            CollectionAssert.IsEmpty(offenders,
                "These files set up a CanvasScaler themselves. A screen with its own opinion about " +
                "the match mode disagrees with every other screen about how big a canvas pixel is, " +
                "on any monitor that is not 16:9. Call UIScale.Configure or UIScale.Apply instead.");
        }

        /// <summary>
        /// Nothing re-derives the screen-to-canvas conversion from the reference width.
        ///
        /// This is the reported bug itself, pinned. Two lobby helpers held constants of the shape
        /// <c>1920 / Screen.width</c>, which is the answer for a scaler matching WIDTH — a rule the
        /// canvas they drew on did not follow. Ask <see cref="UIScale"/>, or measure in canvas space
        /// to begin with, as <c>LobbyOverlayLayer.TryToCanvas</c> now does.
        /// </summary>
        [Test]
        public void NothingDerivesTheCanvasSizeFromTheReferenceWidth()
        {
            var offenders = new List<string>();

            foreach (string path in ScriptPaths())
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;

                    if (Regex.IsMatch(line, @"1920f?\s*[*/]\s*Screen\.") ||
                        Regex.IsMatch(line, @"Screen\.\w+\s*/\s*1920f?"))
                        offenders.Add($"{Path.GetFileName(path)}: {line.Trim()}");
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "These lines convert between screen and canvas pixels by assuming the canvas is " +
                "1920 wide. That is only true for a scaler matching width. Use UIScale.CanvasSize.");
        }

        /// <summary>Every gameplay script, excluding the one file that is allowed to say all this.</summary>
        private static IEnumerable<string> ScriptPaths()
        {
            foreach (string path in Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories))
                if (Path.GetFileName(path) != "UIScale.cs")
                    yield return path;
        }
    }
}
