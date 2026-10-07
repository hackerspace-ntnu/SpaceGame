using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// How much of a label survives at the size it has been left.
    ///
    /// Ordered deliberately: each rung has strictly less room than the one before it, which is what
    /// lets a test assert the ladder only ever descends as space runs out.
    /// </summary>
    public enum RankLabelRung
    {
        /// <summary>Plenty of room. The authored size, the whole name.</summary>
        Roomy = 0,

        /// <summary>The whole name, shrunk to fit.</summary>
        Scaled = 1,

        /// <summary>The name without its prefix, plus its occupancy.</summary>
        Shortened = 2,

        /// <summary>The team's number and its occupancy — the least that is still a label.</summary>
        Floor = 3
    }

    /// <summary>Which names are worth drawing at all.</summary>
    public enum RankNameVisibility
    {
        All = 0,

        OwnTeamAndHost = 1,

        /// <summary>Never "none": a player must always be able to find themselves in the rank.</summary>
        YouAndHost = 2
    }

    /// <summary>What a label should be drawn as.</summary>
    public readonly struct LabelFit
    {
        public readonly RankLabelRung Rung;

        public readonly float FontSize;

        public LabelFit(RankLabelRung rung, float fontSize)
        {
            Rung = rung;
            FontSize = fontSize;
        }
    }

    /// <summary>
    /// Sizes the rank's overlays from the room they actually have on screen.
    ///
    /// <para>
    /// The bug this exists to kill: team plates and nameplates were built at a fixed size in canvas
    /// pixels, while the thing they label is measured in metres and moves away from the camera as
    /// teams are added. Past four teams the labels overlapped each other into a smear, and no amount
    /// of camera work fixes a constant.
    /// </para>
    ///
    /// <para>
    /// The bottom rung is still a word and a count, never a bare colour swatch: team identity is not
    /// allowed to be carried by colour alone.
    /// </para>
    ///
    /// <para>
    /// Pure, and free of TextMeshPro — the caller measures its own text and passes the widths in, so
    /// the rule can be tested without a font, a canvas or a camera.
    /// </para>
    /// </summary>
    public static class RankOverlayScale
    {
        /// <summary>
        /// The smallest a label is ever drawn, in canvas pixels on the 1080-high reference canvas.
        ///
        /// Below this the text is present but not readable, which is worse than a shorter label that
        /// is — hence a ladder rather than an unbounded shrink.
        /// </summary>
        public const float MinFontSize = 18f;

        /// <summary>
        /// How much of the room a name needs before the rank stops drawing all of them.
        ///
        /// Under about half, drawing every name produces a smear rather than a list, and the useful
        /// information — which of these is me, which is the host — is the first thing lost in it.
        /// </summary>
        public const float NameThinThreshold = 0.45f;

        /// <summary>
        /// The size a label <paramref name="widthPx"/> wide at <paramref name="authoredSize"/> has
        /// to drop to in order to fit <paramref name="availablePx"/>. Never larger than authored:
        /// this shrinks labels, it does not grow them.
        /// </summary>
        public static float SizeFor(float authoredSize, float widthPx, float availablePx)
        {
            if (widthPx <= 0.01f) return authoredSize;

            return Mathf.Min(authoredSize, authoredSize * Mathf.Max(0f, availablePx) / widthPx);
        }

        /// <summary>
        /// Picks the longest version of a label that is still legible in the room available.
        ///
        /// The three widths are the same label measured three ways at
        /// <paramref name="authoredSize"/>: the full name, the short name, and the floor form. The
        /// floor rung is clamped UP to <see cref="MinFontSize"/> rather than allowed to shrink
        /// further — at the bottom of the ladder a legible label that slightly overlaps its
        /// neighbour beats an unreadable one that does not.
        /// </summary>
        public static LabelFit Fit(float authoredSize, float availablePx, float fullWidthPx,
            float shortWidthPx, float floorWidthPx)
        {
            float full = SizeFor(authoredSize, fullWidthPx, availablePx);

            if (full >= authoredSize) return new LabelFit(RankLabelRung.Roomy, authoredSize);
            if (full >= MinFontSize) return new LabelFit(RankLabelRung.Scaled, full);

            float shortened = SizeFor(authoredSize, shortWidthPx, availablePx);
            if (shortened >= MinFontSize) return new LabelFit(RankLabelRung.Shortened, shortened);

            float floor = SizeFor(authoredSize, floorWidthPx, availablePx);
            return new LabelFit(RankLabelRung.Floor, Mathf.Max(MinFontSize, floor));
        }

        /// <summary>
        /// Which names to draw, given how far apart two people stand on screen and how wide a name
        /// is at its authored size.
        /// </summary>
        public static RankNameVisibility NamesFor(float seatPitchPx, float nameWidthPx)
        {
            if (nameWidthPx <= 0.01f) return RankNameVisibility.All;

            if (seatPitchPx >= nameWidthPx) return RankNameVisibility.All;
            if (seatPitchPx >= nameWidthPx * NameThinThreshold) return RankNameVisibility.OwnTeamAndHost;

            return RankNameVisibility.YouAndHost;
        }
    }
}
