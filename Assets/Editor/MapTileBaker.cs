using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes one stylized top-down PNG per chunk by reading the chunk's
/// TerrainData heightmap directly. No camera or render pass needed.
///
/// Output: Assets/Resources/MapTiles/Tile_{cx}_{cy}.png
/// Runtime loads via Resources.Load&lt;Texture2D&gt;($"MapTiles/Tile_{cx}_{cy}").
///
/// The look is a sci-fi schematic: dark base + glowing cyan contour lines
/// on evenly-spaced height bands. Re-run after regenerating chunks.
/// </summary>
public class MapTileBaker : EditorWindow
{
    private WorldStreamingConfig config;
    private int tilePixelSize = 128;
    private int contourBands = 8;
    private float contourThickness = 0.06f;
    private Color baseColor = new Color(0.04f, 0.10f, 0.16f, 1f);
    private Color lineColor = new Color(0.20f, 0.85f, 1.00f, 1f);
    private Color highColor = new Color(0.55f, 0.95f, 1.00f, 1f);
    private string outputFolder = "Assets/Resources/MapTiles";

    private readonly HashSet<Vector2Int> selectedChunks = new HashSet<Vector2Int>();
    private bool selectiveExpanded;
    private Vector2 scroll;

    [MenuItem("Tools/World Streaming/Bake Map Tiles")]
    public static void ShowWindow() => GetWindow<MapTileBaker>("Map Tile Baker");

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Map Tile Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bakes per-chunk PNGs for the in-game map by sampling each chunk's TerrainData.\n" +
            "Output goes to a Resources folder so runtime loads them by chunk coord.",
            MessageType.Info);

        EditorGUILayout.Space(8);

        config = (WorldStreamingConfig)EditorGUILayout.ObjectField(
            "World Streaming Config", config, typeof(WorldStreamingConfig), false);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Tile Settings", EditorStyles.boldLabel);
        tilePixelSize  = EditorGUILayout.IntSlider("Tile Pixel Size", tilePixelSize, 32, 512);
        contourBands   = EditorGUILayout.IntSlider("Contour Bands", contourBands, 2, 24);
        contourThickness = EditorGUILayout.Slider("Contour Thickness", contourThickness, 0.01f, 0.25f);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
        baseColor = EditorGUILayout.ColorField("Base", baseColor);
        lineColor = EditorGUILayout.ColorField("Contour Line", lineColor);
        highColor = EditorGUILayout.ColorField("Peak Tint", highColor);

        EditorGUILayout.Space(6);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        EditorGUILayout.Space(10);

        GUI.enabled = config != null && config.chunks != null && config.chunks.Length > 0;
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Bake All Tiles", GUILayout.Height(36)))
        {
            BakeTiles(null);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(8);

        if (config != null && config.chunks != null && config.chunks.Length > 0)
        {
            selectiveExpanded = EditorGUILayout.Foldout(selectiveExpanded, "Bake Selected Chunks", true);
            if (selectiveExpanded)
            {
                DrawSelectableGrid();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All"))
                {
                    selectedChunks.Clear();
                    for (int x = 0; x < config.gridDimensions.x; x++)
                        for (int y = 0; y < config.gridDimensions.y; y++)
                            selectedChunks.Add(new Vector2Int(x, y));
                }
                if (GUILayout.Button("Clear Selection")) selectedChunks.Clear();
                EditorGUILayout.EndHorizontal();

                GUI.enabled = selectedChunks.Count > 0;
                GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
                if (GUILayout.Button($"Bake {selectedChunks.Count} Selected", GUILayout.Height(28)))
                {
                    BakeTiles(selectedChunks);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
        }

        GUI.enabled = true;
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelectableGrid()
    {
        const float cellSize = 36f;
        float w = config.gridDimensions.x * cellSize;
        float h = config.gridDimensions.y * cellSize;
        var rect = GUILayoutUtility.GetRect(w + 40, h + 20);
        var ev = Event.current;

        for (int y = config.gridDimensions.y - 1; y >= 0; y--)
        {
            for (int x = 0; x < config.gridDimensions.x; x++)
            {
                int drawY = config.gridDimensions.y - 1 - y;
                var cell = new Rect(rect.x + 20 + x * cellSize, rect.y + 10 + drawY * cellSize,
                                    cellSize - 2, cellSize - 2);
                var coord = new Vector2Int(x, y);
                bool sel = selectedChunks.Contains(coord);
                EditorGUI.DrawRect(cell, sel ? new Color(0.3f, 0.55f, 1f) : new Color(0.3f, 0.3f, 0.3f));
                GUI.Label(cell, $"{x},{y}", EditorStyles.miniLabel);

                if (ev.type == EventType.MouseDown && cell.Contains(ev.mousePosition))
                {
                    if (ev.button == 1) selectedChunks.Remove(coord);
                    else if (sel) selectedChunks.Remove(coord);
                    else selectedChunks.Add(coord);
                    ev.Use();
                    Repaint();
                }
            }
        }
    }

    private void BakeTiles(HashSet<Vector2Int> only)
    {
        if (config == null || config.chunks == null || config.chunks.Length == 0)
        {
            EditorUtility.DisplayDialog("Bake", "No config or chunks.", "OK");
            return;
        }

        CreateFolderRecursive(outputFolder);

        // Save current scene setup so we can restore it
        var savedSetup = EditorSceneManager.GetSceneManagerSetup();

        int total = only != null ? only.Count : config.chunks.Length;
        int done = 0;
        int baked = 0;
        int skipped = 0;

        try
        {
            foreach (var chunk in config.chunks)
            {
                if (only != null && !only.Contains(chunk.gridCoord)) continue;

                EditorUtility.DisplayProgressBar("Baking Map Tiles",
                    $"Chunk {chunk.gridCoord.x},{chunk.gridCoord.y}", (float)done / total);

                if (!chunk.hasTerrain || string.IsNullOrEmpty(chunk.scenePath))
                {
                    WriteFlatTile(chunk.gridCoord);
                    skipped++;
                    done++;
                    continue;
                }

                // Open the chunk scene additively, find its Terrain, bake, close.
                var scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive);
                Terrain terrain = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    terrain = root.GetComponentInChildren<Terrain>();
                    if (terrain != null) break;
                }

                if (terrain != null && terrain.terrainData != null)
                {
                    BakeTerrainTile(terrain.terrainData, chunk.gridCoord);
                    baked++;
                }
                else
                {
                    WriteFlatTile(chunk.gridCoord);
                    skipped++;
                }

                EditorSceneManager.CloseScene(scene, true);
                done++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (savedSetup != null && savedSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(savedSetup);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Map Tile Baker",
            $"Baked {baked} terrain tile(s)\nFlat tiles: {skipped}\nOutput: {outputFolder}",
            "OK");
    }

    private void BakeTerrainTile(TerrainData data, Vector2Int coord)
    {
        int res = tilePixelSize;
        var pixels = new Color[res * res];

        // Read the raw heightmap directly. Returns a [0,1] array sized [hres, hres]
        // where each value is normalized against data.size.y.
        int hres = data.heightmapResolution;
        float[,] raw = data.GetHeights(0, 0, hres, hres);

        // First pass: find per-tile min/max for auto-contrast.
        float minH = float.MaxValue;
        float maxH = float.MinValue;
        for (int z = 0; z < hres; z++)
        {
            for (int x = 0; x < hres; x++)
            {
                float h = raw[z, x];
                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
        }

        float worldRange = (maxH - minH) * data.size.y;
        Debug.Log($"[MapTileBaker] Tile {coord.x},{coord.y}: " +
                  $"hres={hres} normMin={minH:F4} normMax={maxH:F4} worldRange={worldRange:F2}m");

        float range = Mathf.Max(0.00001f, maxH - minH);
        // Treat as flat only if the world-unit range is tiny.
        bool flat = worldRange < 0.5f;

        // Resample raw heightmap to tile pixel grid via bilinear interpolation,
        // remapping per-tile min/max to [0,1] for visible contrast.
        for (int y = 0; y < res; y++)
        {
            float fz = (float)y / (res - 1) * (hres - 1);
            int z0 = Mathf.FloorToInt(fz);
            int z1 = Mathf.Min(z0 + 1, hres - 1);
            float zT = fz - z0;

            for (int x = 0; x < res; x++)
            {
                float fx = (float)x / (res - 1) * (hres - 1);
                int x0 = Mathf.FloorToInt(fx);
                int x1 = Mathf.Min(x0 + 1, hres - 1);
                float xT = fx - x0;

                float h00 = raw[z0, x0];
                float h10 = raw[z0, x1];
                float h01 = raw[z1, x0];
                float h11 = raw[z1, x1];
                float h = Mathf.Lerp(Mathf.Lerp(h00, h10, xT), Mathf.Lerp(h01, h11, xT), zT);

                h = Mathf.Clamp01((h - minH) / range);

                float lineMask = 0f;
                if (!flat)
                {
                    float band = h * contourBands;
                    float distToLine = Mathf.Abs(band - Mathf.Round(band)) / contourBands;
                    lineMask = 1f - Mathf.SmoothStep(0f, contourThickness, distToLine);
                }

                Color fill = Color.Lerp(baseColor, highColor, h * 0.35f);
                Color c = Color.Lerp(fill, lineColor, lineMask);
                pixels[y * res + x] = c;
            }
        }

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, false);
        tex.SetPixels(pixels);
        tex.Apply();

        WritePng(tex, coord);
        Object.DestroyImmediate(tex);
    }

    private void WriteFlatTile(Vector2Int coord)
    {
        var tex = new Texture2D(tilePixelSize, tilePixelSize, TextureFormat.RGBA32, false, false);
        var pixels = new Color[tilePixelSize * tilePixelSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = baseColor;
        tex.SetPixels(pixels);
        tex.Apply();
        WritePng(tex, coord);
        Object.DestroyImmediate(tex);
    }

    private void WritePng(Texture2D tex, Vector2Int coord)
    {
        string path = $"{outputFolder}/Tile_{coord.x}_{coord.y}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void CreateFolderRecursive(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path);
        string folder = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            CreateFolderRecursive(parent);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
            AssetDatabase.CreateFolder(parent, folder);
    }
}
