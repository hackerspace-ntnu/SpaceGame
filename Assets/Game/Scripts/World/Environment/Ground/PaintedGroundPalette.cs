using System;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One paintable ground for StylizedTerrain: the TerrainLayer the brush paints with, and the
    /// palette and scales the shader paints that ground in. Listed on TerrainGroundPaint, which
    /// writes every palette into one row of the shader's palette table.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/World/Painted Ground Palette", fileName = "Palette_Ground")]
    public sealed class PaintedGroundPalette : ScriptableObject
    {
        /// <summary>Texels per row, in the order StylizedTerrain's tablePalette/tableShape/tablePattern read them.</summary>
        public const int TableWidth = 12;

        [Tooltip("The terrain layer that paints this ground. It must be on the terrain's layer list.")]
        [SerializeField] private TerrainLayer layer;

        [Header("Pattern")]
        [Tooltip("Stylized, tileable texture of this ground, drawn without visible repeats. Every pattern on one "
                 + "terrain must share size, format and mip count. None: the ground is fully procedural.")]
        [SerializeField] private Texture2D pattern;
        [Tooltip("Metres one repeat of the pattern covers.")]
        [SerializeField, Min(0.1f)] private float patternTile = 3.5f;
        [Tooltip("0: palette colours only; 1: the pattern, shaded by the painted patches.")]
        [SerializeField, Range(0f, 1f)] private float patternStrength = 1f;
        [Tooltip("Lets the anti-tiling turn the pattern too. Off for directional patterns such as furrows.")]
        [SerializeField] private bool patternRotates = true;

        [Header("Colours")]
        [SerializeField] private Color baseColour = new(0.6f, 0.5f, 0.35f);
        [SerializeField] private Color darkerPatches = new(0.5f, 0.42f, 0.29f);
        [SerializeField] private Color palerPatches = new(0.68f, 0.57f, 0.41f);
        [SerializeField] private Color windStreaks = new(0.64f, 0.54f, 0.38f);
        [Tooltip("Pebbles and near gravel.")]
        [SerializeField] private Color pebbles = new(0.66f, 0.6f, 0.5f);
        [SerializeField] private Color cliff = new(0.52f, 0.4f, 0.28f);
        [SerializeField] private Color cliffStrata = new(0.6f, 0.47f, 0.33f);
        [Tooltip("Added to the ambient light: the colour of this ground's shadows.")]
        [SerializeField] private Color shadowTint = new(0.12f, 0.1f, 0.2f);

        [Header("Detail")]
        [Tooltip("Ripples bending the light on flat ground; above 2 they read through the toon light steps as furrows.")]
        [SerializeField, Range(0f, 4f)] private float ripples;
        [Tooltip("Share of pebble cells that hold a pebble.")]
        [SerializeField, Range(0f, 1f)] private float pebbleDensity = 0.03f;
        [Tooltip("Strength of the small painted dabs.")]
        [SerializeField, Range(0f, 2f)] private float dabs = 0.8f;
        [SerializeField, Range(0f, 1f)] private float streaks = 0.3f;

        [Header("Shape")]
        [Tooltip("Multiplies the material's region, patch and dab sizes.")]
        [SerializeField, Min(0.05f)] private float patchScale = 1f;
        [Tooltip("Metres between pebbles at full density.")]
        [SerializeField, Min(0.05f)] private float pebbleSpacing = 1.4f;
        [Tooltip("Largest pebble radius as a share of the spacing; above ~0.4 neighbours clip each other.")]
        [SerializeField, Range(0.02f, 0.45f)] private float pebbleRadius = 0.0929f;
        [Tooltip("Multiplies the ripple wavelengths (2.2 m and 3.6 m).")]
        [SerializeField, Min(0.05f)] private float rippleScale = 1f;

        [Header("Soil (near ground detail)")]
        [Tooltip("Multiplies the material's gravel strength.")]
        [SerializeField, Range(0f, 2f)] private float gravel = 1f;
        [Tooltip("Multiplies the material's crack darkening.")]
        [SerializeField, Range(0f, 3f)] private float cracks = 1f;
        [Tooltip("0: every pebble in the pebble colour; 1: spread down to a darker stone.")]
        [SerializeField, Range(0f, 1f)] private float pebbleToneVariation;
        [Tooltip("How much ground the gravel clumps take; cracks fill the rest.")]
        [SerializeField, Range(0f, 1f)] private float gravelCoverage = 0.45f;

        public TerrainLayer Layer => layer;
        public Texture2D Pattern => pattern;

#if UNITY_EDITOR
        /// <summary>Raised when a palette is edited, so terrains showing it rewrite their table.</summary>
        public static event Action<PaintedGroundPalette> Changed;

        private void OnValidate() => Changed?.Invoke(this);
#endif

        /// <summary>
        /// Writes this palette into row <paramref name="row"/> of a table TableWidth texels wide, its
        /// pattern being slice <paramref name="patternSlice"/> of the pattern array (-1: none).
        /// </summary>
        public void Write(Color[] table, int row, int patternSlice)
        {
            int i = row * TableWidth;
            table[i + 0] = Shading(baseColour);
            table[i + 1] = Shading(darkerPatches);
            table[i + 2] = Shading(palerPatches);
            table[i + 3] = Shading(windStreaks);
            table[i + 4] = Shading(pebbles);
            table[i + 5] = Shading(cliff);
            table[i + 6] = Shading(cliffStrata);
            table[i + 7] = Shading(shadowTint);
            table[i + 8] = new Color(ripples, pebbleDensity, dabs, streaks);
            table[i + 9] = new Color(gravel, cracks, pebbleToneVariation, gravelCoverage);
            table[i + 10] = new Color(patchScale, pebbleSpacing, pebbleRadius, rippleScale);
            table[i + 11] = new Color(patternSlice, patternTile, patternSlice >= 0 ? patternStrength : 0f, patternRotates ? 1f : 0f);
        }

        // The space the shader receives material colours in.
        private static Color Shading(Color c) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
    }
}
