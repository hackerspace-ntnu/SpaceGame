using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The focus shot's arithmetic: where the lens sits, how far down it looks, and whether the
    /// near edge of the mat is still on the screen.
    ///
    /// <para>
    /// Worth pinning because the failure is silent and looks like an art bug. The frame's bottom
    /// edge is <c>Pitch + Fov/2</c> below horizontal and the board's leading edge is very nearly
    /// that far below it too, so raising the lens by a few centimetres on a fixed pitch slides the
    /// front row of cells off the bottom of the screen — items are still there, still placeable,
    /// and simply not drawn. Nothing throws and no test that does not measure the angle notices.
    /// </para>
    /// <para>
    /// Every number here is in the pack's ORIGINAL frame and every result is an angle, so none of
    /// this moves with <see cref="PackScale.Factor"/> — which is the property that lets the rig be
    /// resized without anybody re-deriving the shot.
    /// </para>
    /// </summary>
    public class PackFocusShotTests
    {
        /// <summary>The old shot, kept as a number so the tests below can compare against it.</summary>
        private const float HeightBeforeTheLift = 1.5f;

        private const float PitchBeforeTheLift = 38f;

        [Test]
        public void ThePitchTracksTheHeightSoTheShotAlwaysAimsAtTheSameSpotOnTheMat()
        {
            float[] heights = { 1.2f, HeightBeforeTheLift, PackFocusCamera.OpenHeight, 2.4f };

            float first = Reach(heights[0]);

            foreach (float height in heights)
                Assert.AreEqual(first, Reach(height), 1e-3f,
                                $"the lens at {height} m aims somewhere else along the ground — a " +
                                "higher shot is supposed to be the SAME shot from higher up, which " +
                                "is only true while the pitch is derived from the height");

            // The old shot is one of the family rather than an exception to it, which is what makes
            // "keep the aim point" a rule the change obeyed rather than a story told afterwards.
            Assert.AreEqual(PitchBeforeTheLift,
                            PackFocusCamera.PitchForHeight(HeightBeforeTheLift), 0.05f,
                            "the pitch the shot shipped with has to fall out of the same formula");
        }

        [Test]
        public void TheMatsLeadingEdgeIsInFrameWithMoreRoomThanTheShotItReplaced()
        {
            float margin = PackFocusCamera.NearEdgeMargin(PackFocusCamera.OpenHeight,
                                                          PackFocusCamera.Pitch);

            Assert.Greater(margin, 1f,
                           "the front row of cells is cropped off the bottom of the frame, or is " +
                           "close enough to the edge that the next tweak will crop it");

            Assert.Greater(margin,
                           PackFocusCamera.NearEdgeMargin(HeightBeforeTheLift, PitchBeforeTheLift),
                           "the lift is supposed to have bought headroom, not spent it");
        }

        /// <summary>
        /// Why the pitch moved with the height, stated as a number rather than as a claim: the same
        /// lift on the old pitch puts the near edge five degrees below the bottom of the frame.
        /// </summary>
        [Test]
        public void RaisingTheLensAloneWouldHaveCroppedTheFrontOfTheMat()
        {
            Assert.Less(PackFocusCamera.NearEdgeMargin(PackFocusCamera.OpenHeight, PitchBeforeTheLift),
                        0f,
                        "raising the lens on the old pitch is supposed to crop the mat — if it no " +
                        "longer does, the shot's geometry has changed and the pitch may be free");
        }

        [Test]
        public void TheShutPackIsFramedFromHigherUpAtTheSamePitch()
        {
            Assert.Greater(PackFocusCamera.ClosedHeight, PackFocusCamera.OpenHeight,
                           "a pack that lands shut is a taller object and is framed from higher up");

            // Small on purpose. The lens tracks the flap through its whole swing, so this is a
            // camera move the player sees every time they open the pack, and a big one would read
            // as the camera doing something of its own (GDC-L1-FEEL-0006).
            Assert.Less(PackFocusCamera.ClosedHeight - PackFocusCamera.OpenHeight, 0.5f,
                        "the lift between the two states is a nudge that keeps the pack framed, " +
                        "not a second shot");
        }

        /// <summary>Ground distance from the lens to the spot the optical axis lands on.</summary>
        private static float Reach(float height) =>
            height / Mathf.Tan(PackFocusCamera.PitchForHeight(height) * Mathf.Deg2Rad);
    }
}
