using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Every tunable of the wanted-poster look, in one asset the baker reads
    /// (<see cref="WantedPosterBaker.StylePath"/>). Colours are sRGB, as on a printed sheet.
    /// Sheet positions and sizes are in sheet units: the sheet is 100 wide and 125 tall.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Posters/Wanted Poster Style", fileName = "WantedPosterStyle")]
    public sealed class WantedPosterStyle : ScriptableObject
    {
        [Header("Inks")]
        public Color paper = new(0.871f, 0.839f, 0.788f, 1f);
        public Color ink = new(0.204f, 0.149f, 0.122f, 1f);
        [Tooltip("Spot colour for skin, the ALIVE tag and the label shadows.")]
        public Color slate = new(0.247f, 0.322f, 0.408f, 1f);
        public Color slateLight = new(0.376f, 0.463f, 0.549f, 1f);
        [Tooltip("Opaque highlight ink, and the fill of undyed cloth.")]
        public Color cream = new(0.945f, 0.914f, 0.847f, 1f);
        [Tooltip("The DEAD tag.")]
        public Color dead = new(0.722f, 0.227f, 0.173f, 1f);

        [Header("Portrait render")]
        [Tooltip("Pixels across the rendered portrait; its height follows the sheet's picture box.")]
        public int renderWidth = 1100;
        [Tooltip("Portrait height over width.")]
        public float portraitAspect = 1.1f;
        [Tooltip("Key light intensity, from the front, left and above.")]
        public float keyLight = 1.4f;
        [Tooltip("Rim light intensity, from behind, right and above.")]
        public float rimLight = 0.8f;

        [Header("Screenprint: tone")]
        [Tooltip("Tone above this prints as a cream highlight.")]
        [Range(0f, 1f)] public float highlight = 0.80f;
        [Tooltip("Tone below this gets a band of shadow dots.")]
        [Range(0f, 1f)] public float shadow = 0.44f;
        [Tooltip("Tone below this prints solid ink.")]
        [Range(0f, 1f)] public float deep = 0.22f;
        [Tooltip("Skin tone is read from its brightest channel times this, so a blue skin does not read as near-black.")]
        [Range(0f, 1f)] public float skinValueScale = 0.85f;
        [Tooltip("Tone below this is black before the remap to 0..1.")]
        [Range(0f, 0.5f)] public float toneFloor = 0.08f;
        [Tooltip("Width of the tone range above the floor.")]
        [Range(0.1f, 1f)] public float toneRange = 0.75f;
        [Tooltip("Softening of the tone before banding, in pixels of the portrait render.")]
        public int toneBlur = 3;
        [Tooltip("How far a band is smoothed into a shape, in pixels.")]
        public int bandCleanup = 3;

        [Header("Screenprint: dots and lines")]
        [Tooltip("Shadow dot spacing, as a fraction of the render width.")]
        public float shadowCell = 0.0155f;
        [Tooltip("Dot coverage in the shadow band on coloured cloth and skin.")]
        [Range(0f, 1f)] public float shadowCoverage = 0.42f;
        [Tooltip("Dot coverage in the shadow band on undyed cloth, which prints as dense dots on cream.")]
        [Range(0f, 1f)] public float neutralShadowCoverage = 0.66f;
        [Tooltip("Half-width of an outline, in pixels of the portrait render.")]
        public int lineRadius = 2;
        [Tooltip("Extra outline outside the silhouette, in pixels.")]
        public int silhouetteRadius = 3;
        [Tooltip("Neighbouring normals further apart than this draw a crease line, in degrees.")]
        public float creaseAngle = 50f;
        [Tooltip("A depth step larger than this draws a line, as a fraction of the subject's height.")]
        public float depthStep = 0.02f;
        [Tooltip("Colour layer offset from the key lines, in pixels: a print slightly off register.")]
        public Vector2Int register = new(3, 2);
        [Tooltip("Cloth whose red exceeds its blue by this factor, and is at least this saturated, prints in the accent colour.")]
        public float warmRatio = 1.15f;
        [Range(0f, 1f)] public float warmSaturation = 0.18f;
        [Tooltip("Eye colours are printed slightly brighter than rendered.")]
        public float eyeBoost = 1.08f;

        [Header("Sheet")]
        public Vector2Int sheetPixels = new(1024, 1280);
        [Tooltip("The paper a new poster component is fitted with, in metres.")]
        public Vector2 sheetSize = new(0.72f, 0.9f);
        [Tooltip("How far the paper stands off the poster's back, and the collider's depth, in metres.")]
        public Vector2 sheetDepth = new(0.006f, 0.02f);
        [Tooltip("Picture box on the sheet: x, y, width, height in sheet units.")]
        public Rect pictureBox = new(10f, 25.4f, 80f, 81.6f);
        [Tooltip("The portrait's height as a multiple of the picture box height. It is bottom-aligned and may rise above the box.")]
        public float pictureOverscan = 1.04f;

        [Header("Sheet: backdrop")]
        public Vector2 sunCentre = new(50f, 56f);
        public float sunRadius = 46f;
        [Range(0f, 1f)] public float sunTint = 0.24f;
        [Tooltip("Stripes across the sun below its centre: period and thickness in sheet units.")]
        public Vector2 sunStripes = new(3f, 1.1f);
        [Range(0f, 1f)] public float textureTint = 0.46f;
        public Vector2 moonCentre = new(92f, 12f);
        public float moonRadius = 24f;
        [Tooltip("Moon dots: spacing, radius in sheet units, and screen angle in degrees.")]
        public Vector3 moonDots = new(2.6f, 0.75f, 20f);
        [Tooltip("Mesa ridge outline across the bottom, in sheet units.")]
        public Vector2[] mesas =
        {
            new(-5f, 125f), new(-5f, 96f), new(18f, 72f), new(36f, 94f), new(52f, 82f),
            new(74f, 104f), new(105f, 86f), new(105f, 125f),
        };
        [Tooltip("Mesa hatching: period, thickness in sheet units, angle in degrees.")]
        public Vector3 mesaHatch = new(2.2f, 0.8f, -35f);
        [Range(0f, 1f)] public float mesaTint = 0.22f;

        [Header("Sheet: banner")]
        public float bannerHeight = 20.2f;
        [Tooltip("The banner is the poster's accent colour darkened to this fraction of its brightness.")]
        [Range(0f, 1f)] public float bannerShade = 0.42f;
        public float bannerRule = 1.6f;
        [Tooltip("Cap height of WANTED, in sheet units.")]
        public float titleCap = 12.2f;
        [Tooltip("Widest WANTED may be, as a fraction of the sheet.")]
        public float titleMaxWidth = 0.9f;
        public float titleTracking = 0.02f;
        [Tooltip("Worn-ink holes in the banner: noise feature size in sheet units, and the noise level above which the ink wears through (higher wears less).")]
        public Vector2 wear = new(0.9f, 0.68f);

        [Header("Sheet: brackets")]
        public float bracketLength = 11f;
        public float bracketThickness = 2.2f;
        [Tooltip("How far each bracket sits outside the picture box.")]
        public float bracketOutset = 1.4f;
        public Vector2 bracketShadow = new(0.6f, 0.6f);

        [Header("Sheet: tag")]
        public float tagCap = 6f;
        [Tooltip("Padding: left (room for the eyelet), right, top, bottom, in sheet units.")]
        public Vector4 tagPadding = new(8f, 3.6f, 3.4f, 3.2f);
        [Tooltip("How far the pointed end reaches in, and the cut off the bottom-right corner, in sheet units.")]
        public float tagNotch = 3f;
        public float tagCornerCut = 2.4f;
        public float tagEyeletRadius = 1.3f;
        [Tooltip("The eyelet's centre from the tag's pointed end, its ring width in sheet units, and the ring's darkness.")]
        public Vector3 tagEyelet = new(4.9f, 0.6f, 0.35f);
        [Tooltip("Tag offset from the picture box's bottom-right corner, in sheet units.")]
        public Vector2 tagOffset = new(3f, -0.8f);
        [Tooltip("Rotation in degrees; positive turns anticlockwise.")]
        public float tagAngle = 7f;
        public Vector2 tagShadow = new(0.5f, 0.7f);
        public float tagTracking = 0.08f;

        [Header("Sheet: fields")]
        public float fieldLeft = 10f;
        public float fieldRight = 90f;
        public float fieldFirstBaseline = 114f;
        public float fieldLineGap = 5f;
        public float labelCap = 2.75f;
        public float valueCap = 2.9f;
        public float labelGap = 2.6f;
        public float labelShadow = 0.3f;
        public float labelTracking = 0.04f;
        public float valueTracking = 0.05f;
        [Tooltip("Space between HT: and its value, in sheet units.")]
        public float heightGap = 1f;

        [Header("Sheet: wear and tear")]
        [Tooltip("Coffee-ring radius in sheet units, and its strength.")]
        public Vector2 stain = new(15f, 0.3f);
        [Tooltip("Where the ring's centre may land, in sheet units; each poster picks its own spot from its name.")]
        public Rect stainArea = new(-5f, 30f, 110f, 85f);
        public Color stainColour = new(0.471f, 0.275f, 0.118f, 1f);
        public Color vignetteColour = new(0.314f, 0.235f, 0.157f, 1f);
        [Tooltip("Crease relief: noise scale in sheet units, relief height, and how strongly it darkens.")]
        public Vector3 creases = new(14f, 5f, 0.62f);
        [Tooltip("Light raking the creases: azimuth and elevation in degrees.")]
        public Vector2 creaseLight = new(235f, 48f);
        [Range(0f, 1f)] public float grain = 0.16f;
        [Tooltip("Edge darkening of the sheet, 0 for none.")]
        [Range(0f, 1f)] public float vignette = 0.28f;
    }
}
