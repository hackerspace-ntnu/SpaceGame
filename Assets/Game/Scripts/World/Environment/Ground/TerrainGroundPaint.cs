using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Lets the terrain brush paint StylizedTerrain's grounds. Each painted ground is an ordinary
    /// TerrainLayer; this tells the shader which of the terrain's splat textures hold those layers
    /// and which ground each channel paints, through the terrain's splat property block, so
    /// painting one overrides the procedural biomes there. The textures are the terrain's own, so
    /// strokes show live. Three grounds take their palette from the material (dry grassland,
    /// forest floor, its own ground); every PaintedGroundPalette brings its own, written into the
    /// shader's palette table, and its pattern texture, copied into the shader's pattern array.
    ///
    /// Runs in edit mode too: adding or removing terrain layers reshuffles the splat textures, and
    /// the binding is redone whenever that happens or a listed palette is edited.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Terrain))]
    public sealed class TerrainGroundPaint : MonoBehaviour
    {
        // StylizedTerrain samples at most this many splat textures for painted grounds.
        private const int MaxSplats = 4;
        // Shader ground codes: 0 none, 1-3 the material's grounds, FirstTableCode + n palette row n.
        private const float DryCode = 1f;
        private const float ForestCode = 2f;
        private const float OwnCode = 3f;
        private const float FirstTableCode = 4f;

        private static readonly int[] SplatIds =
        {
            Shader.PropertyToID("_PaintSplat0"),
            Shader.PropertyToID("_PaintSplat1"),
            Shader.PropertyToID("_PaintSplat2"),
            Shader.PropertyToID("_PaintSplat3"),
        };
        private static readonly int[] GroundIds =
        {
            Shader.PropertyToID("_PaintSplatGround0"),
            Shader.PropertyToID("_PaintSplatGround1"),
            Shader.PropertyToID("_PaintSplatGround2"),
            Shader.PropertyToID("_PaintSplatGround3"),
        };
        private static readonly int PalettesId = Shader.PropertyToID("_PaintPalettes");
        private static readonly int PatternsId = Shader.PropertyToID("_PaintPatterns");
        private static readonly int RectId = Shader.PropertyToID("_PaintMapRect");

        [Tooltip("Paints the material's Biome A (dry grassland) palette.")]
        [SerializeField] private TerrainLayer dryGrassland;
        [Tooltip("Paints the material's Biome B (forest floor) palette.")]
        [SerializeField] private TerrainLayer forestFloor;
        [Tooltip("Paints the material's own ground (its height bands and painted-patch colours).")]
        [SerializeField] private TerrainLayer ownGround;
        [Tooltip("Grounds with palettes of their own. Their layers should sit next to each other on the terrain: "
                 + "all painted grounds together may span at most four splat textures (16 layers).")]
        [SerializeField] private PaintedGroundPalette[] grounds = Array.Empty<PaintedGroundPalette>();

        // Created on first use: Unity refuses a MaterialPropertyBlock built in a field initializer.
        private MaterialPropertyBlock block;
        private Terrain terrain;
        private Texture2D paletteTable;
        private Texture2DArray patternArray;
        private TerrainLayer[] boundLayers;
        private Texture2D[] boundTextures;

        private void OnEnable()
        {
            terrain = GetComponent<Terrain>();
            block ??= new MaterialPropertyBlock();
#if UNITY_EDITOR
            PaintedGroundPalette.Changed += OnPaletteChanged;
#endif
            Bind();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && terrain != null && block != null)
                Bind();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            PaintedGroundPalette.Changed -= OnPaletteChanged;
#endif
            if (terrain != null && block != null)
            {
                // A zero rect is the shader's "nothing painted".
                terrain.GetSplatMaterialPropertyBlock(block);
                block.SetVector(RectId, Vector4.zero);
                terrain.SetSplatMaterialPropertyBlock(block);
            }
            if (paletteTable != null)
                DestroyImmediate(paletteTable);
            if (patternArray != null)
                DestroyImmediate(patternArray);
            paletteTable = null;
            patternArray = null;
            boundLayers = null;
            boundTextures = null;
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!Application.isPlaying && LayoutChanged())
                Bind();
        }

        private void OnPaletteChanged(PaintedGroundPalette palette)
        {
            if (Array.IndexOf(grounds, palette) >= 0)
                Bind();
        }

        private bool LayoutChanged()
        {
            TerrainData data = terrain.terrainData;
            if (data == null)
                return boundLayers != null;
            return !SameItems(boundLayers, data.terrainLayers) || !SameItems(boundTextures, data.alphamapTextures);
        }

        private static bool SameItems<T>(T[] a, T[] b) where T : UnityEngine.Object
        {
            if (a == null || b == null || a.Length != b.Length)
                return a == b;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
#endif

        private void Bind()
        {
            TerrainData data = terrain.terrainData;
            if (data == null)
                return;
            TerrainLayer[] layers = data.terrainLayers;
            Texture2D[] textures = data.alphamapTextures;

            // Splat texture n holds layers 4n..4n+3, one per RGBA channel.
            var splats = new List<int>(MaxSplats);
            var codes = new Vector4[MaxSplats];
            var unshown = new List<string>();
            void Place(TerrainLayer layer, float code)
            {
                int index = layer != null ? Array.IndexOf(layers, layer) : -1;
                if (index < 0)
                    return;
                int slot = splats.IndexOf(index / 4);
                if (slot < 0)
                {
                    if (splats.Count == MaxSplats)
                    {
                        unshown.Add(layer.name);
                        return;
                    }
                    slot = splats.Count;
                    splats.Add(index / 4);
                }
                codes[slot][index % 4] = code;
            }
            Place(dryGrassland, DryCode);
            Place(forestFloor, ForestCode);
            Place(ownGround, OwnCode);
            for (int row = 0; row < grounds.Length; row++)
                if (grounds[row] != null)
                    Place(grounds[row].Layer, FirstTableCode + row);
            if (unshown.Count > 0)
                Debug.LogError($"[TerrainGroundPaint] '{name}': the painted grounds span more than {MaxSplats} splat textures, "
                               + $"so {string.Join(", ", unshown)} will not show. Move their layers next to the other painted grounds.", this);

            terrain.GetSplatMaterialPropertyBlock(block);
            for (int slot = 0; slot < MaxSplats; slot++)
            {
                block.SetTexture(SplatIds[slot], slot < splats.Count ? textures[splats[slot]] : Texture2D.blackTexture);
                block.SetVector(GroundIds[slot], codes[slot]);
            }
            int[] slices = CopyPatterns();
            block.SetTexture(PatternsId, patternArray);
            block.SetTexture(PalettesId, WritePaletteTable(slices));
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            block.SetVector(RectId, new Vector4(origin.x, origin.z, 1f / size.x, 1f / size.z));
            terrain.SetSplatMaterialPropertyBlock(block);

            boundLayers = layers;
            boundTextures = textures;
        }

        // Copies every palette's pattern into one texture array, GPU side (the sources need not be
        // readable). Returns each palette's slice: -1 for none, or for a pattern whose size, format
        // or mips differ from the first one's, which a texture array cannot hold.
        private int[] CopyPatterns()
        {
            var slices = new int[grounds.Length];
            var patterns = new List<Texture2D>();
            for (int row = 0; row < grounds.Length; row++)
            {
                Texture2D pattern = grounds[row] != null ? grounds[row].Pattern : null;
                slices[row] = pattern != null ? patterns.IndexOf(pattern) : -1;
                if (pattern == null || slices[row] >= 0)
                    continue;
                if (patterns.Count > 0 && !SameLayout(patterns[0], pattern))
                {
                    Debug.LogError($"[TerrainGroundPaint] '{name}': pattern '{pattern.name}' of {grounds[row].name} is "
                                   + $"{pattern.width}x{pattern.height} {pattern.graphicsFormat} with {pattern.mipmapCount} mips, "
                                   + $"unlike '{patterns[0].name}', so that ground is drawn without it. Import every pattern alike.", this);
                    continue;
                }
                slices[row] = patterns.Count;
                patterns.Add(pattern);
            }

            Texture2D first = patterns.Count > 0 ? patterns[0] : Texture2D.blackTexture;
            int depth = Mathf.Max(1, patterns.Count);
            if (patternArray == null || patternArray.depth != depth || !SameLayout(patternArray, first))
            {
                if (patternArray != null)
                    DestroyImmediate(patternArray);
                patternArray = new Texture2DArray(first.width, first.height, depth, first.graphicsFormat,
                                                  TextureCreationFlags.MipChain, first.mipmapCount)
                {
                    name = "PaintedGroundPatterns",
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 8,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                // Upload once and drop the CPU copy: the slices are filled GPU side, and a later
                // Apply would overwrite them with the empty CPU data.
                patternArray.Apply(false, true);
            }
            for (int slice = 0; slice < patterns.Count; slice++)
                Graphics.CopyTexture(patterns[slice], 0, patternArray, slice);
            return slices;
        }

        private static bool SameLayout(Texture a, Texture2D b) =>
            a.width == b.width && a.height == b.height && a.graphicsFormat == b.graphicsFormat && a.mipmapCount == b.mipmapCount;

        private Texture WritePaletteTable(int[] slices)
        {
            if (grounds.Length == 0)
                return Texture2D.blackTexture;
            if (paletteTable == null || paletteTable.height != grounds.Length)
            {
                if (paletteTable != null)
                    DestroyImmediate(paletteTable);
                paletteTable = new Texture2D(PaintedGroundPalette.TableWidth, grounds.Length, TextureFormat.RGBAFloat, false, true)
                {
                    name = "PaintedGroundPalettes",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            var table = new Color[PaintedGroundPalette.TableWidth * grounds.Length];
            for (int row = 0; row < grounds.Length; row++)
                if (grounds[row] != null)
                    grounds[row].Write(table, row, slices[row]);
            paletteTable.SetPixels(table);
            paletteTable.Apply(false);
            return paletteTable;
        }
    }
}
