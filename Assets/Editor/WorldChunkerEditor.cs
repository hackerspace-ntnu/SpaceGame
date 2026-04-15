using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor window that generates chunk scenes from a master world scene.
/// The master scene is never modified — objects are copied into chunk scenes.
/// Terrain is automatically split into tiles matching the chunk grid.
/// Grid dimensions are auto-calculated from world bounds and chunk size.
/// Open via Tools > World Streaming > Chunk World.
/// </summary>
public class WorldChunkerEditor : EditorWindow
{
    private Vector2 chunkSize = new Vector2(256f, 256f);
    private int loadRadius = 1;
    private float unloadGracePeriod = 10f;
    private string outputFolder = "Assets/Scenes/Chunks";
    private string terrainDataFolder = "Assets/Terrain/ChunkData";
    private string configOutputPath = "Assets/Settings/WorldStreamingConfig.asset";
    private bool skipNetworkObjects = true;

    // Auto-calculated from scanning the scene
    private Vector3 detectedOrigin;
    private Vector3 detectedMax;
    private Vector2Int calculatedGridDimensions;
    private bool boundsDetected;

    // Collected terrains from the scene
    private List<Terrain> sceneTerrains = new List<Terrain>();

    // Selective update
    private bool selectiveUpdateExpanded;
    private readonly HashSet<Vector2Int> selectedChunks = new HashSet<Vector2Int>();

    private Vector2 scrollPos;

    private static readonly Color ColorUnselected = new Color(0.6f, 0.6f, 0.6f, 0.25f);
    private static readonly Color ColorSelected    = new Color(0.3f, 0.6f, 1f,  0.45f);
    private static readonly Color ColorOutline     = new Color(1f,   1f,   1f,  0.5f);
    private static readonly Color ColorLabel       = new Color(1f,   1f,   1f,  0.9f);

    private GUIStyle chunkLabelStyle;

    [MenuItem("Tools/World Streaming/Chunk World")]
    public static void ShowWindow()
    {
        GetWindow<WorldChunkerEditor>("World Chunker");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!boundsDetected || !selectiveUpdateExpanded) return;

        if (chunkLabelStyle == null)
        {
            chunkLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = ColorLabel },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
        }

        var currentEvent = Event.current;
        bool clicked = currentEvent.type == EventType.MouseDown
                       && currentEvent.button == 0
                       && !currentEvent.alt;

        for (int cx = 0; cx < calculatedGridDimensions.x; cx++)
        {
            for (int cy = 0; cy < calculatedGridDimensions.y; cy++)
            {
                var coord = new Vector2Int(cx, cy);
                bool isSelected = selectedChunks.Contains(coord);

                float worldX = detectedOrigin.x + cx * chunkSize.x;
                float worldZ = detectedOrigin.z + cy * chunkSize.y;
                float cx2 = worldX + chunkSize.x;
                float cz2 = worldZ + chunkSize.y;

                // Flat quad at y=0 — four corners
                var c00 = new Vector3(worldX, 0, worldZ);
                var c10 = new Vector3(cx2,   0, worldZ);
                var c11 = new Vector3(cx2,   0, cz2);
                var c01 = new Vector3(worldX, 0, cz2);
                var center = new Vector3(worldX + chunkSize.x * 0.5f, 0, worldZ + chunkSize.y * 0.5f);

                // Fill
                Handles.color = isSelected ? ColorSelected : ColorUnselected;
                Handles.DrawSolidRectangleWithOutline(
                    new Vector3[] { c00, c10, c11, c01 },
                    isSelected ? ColorSelected : ColorUnselected,
                    ColorOutline
                );

                Handles.Label(center + Vector3.up * 2f, $"{cx},{cy}", chunkLabelStyle);

                // Click detection — use a transparent button handle
                if (clicked)
                {
                    // Project mouse ray onto y=0 plane
                    Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                    if (Mathf.Abs(ray.direction.y) > 0.0001f)
                    {
                        float t = -ray.origin.y / ray.direction.y;
                        if (t > 0f)
                        {
                            var hit = ray.origin + ray.direction * t;
                            if (hit.x >= worldX && hit.x < cx2 && hit.z >= worldZ && hit.z < cz2)
                            {
                                if (currentEvent.shift && lastClickedCoord.HasValue)
                                {
                                    int minX = Mathf.Min(lastClickedCoord.Value.x, cx);
                                    int maxX = Mathf.Max(lastClickedCoord.Value.x, cx);
                                    int minY = Mathf.Min(lastClickedCoord.Value.y, cy);
                                    int maxY = Mathf.Max(lastClickedCoord.Value.y, cy);
                                    for (int rx = minX; rx <= maxX; rx++)
                                        for (int ry = minY; ry <= maxY; ry++)
                                            selectedChunks.Add(new Vector2Int(rx, ry));
                                }
                                else
                                {
                                    if (isSelected) selectedChunks.Remove(coord);
                                    else            selectedChunks.Add(coord);
                                    lastClickedCoord = coord;
                                }

                                currentEvent.Use();
                                Repaint();
                            }
                        }
                    }
                }
            }
        }

    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("World Chunker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Generates chunk scenes from the currently open master scene.\n\n" +
            "- Master scene is NOT modified (objects are copied)\n" +
            "- Terrain is automatically split into tiles per chunk\n" +
            "- Grid size is auto-calculated from world bounds\n\n" +
            "1. Open your master world scene\n" +
            "2. Set the chunk size\n" +
            "3. Click 'Scan World' then 'Generate Chunks'",
            MessageType.Info);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Chunk Size", EditorStyles.boldLabel);
        chunkSize = EditorGUILayout.Vector2Field("Chunk Size (world units)", chunkSize);

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Scan World"))
        {
            ScanWorldBounds();
        }

        if (boundsDetected)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Detected Bounds", EditorStyles.boldLabel);

            float worldWidth = detectedMax.x - detectedOrigin.x;
            float worldDepth = detectedMax.z - detectedOrigin.z;

            EditorGUILayout.LabelField($"World Origin: ({detectedOrigin.x:F1}, {detectedOrigin.z:F1})");
            EditorGUILayout.LabelField($"World Size: {worldWidth:F1} x {worldDepth:F1} units");
            EditorGUILayout.LabelField($"Grid: {calculatedGridDimensions.x} x {calculatedGridDimensions.y} = {calculatedGridDimensions.x * calculatedGridDimensions.y} chunks");

            if (sceneTerrains.Count > 0)
            {
                EditorGUILayout.LabelField($"Terrains found: {sceneTerrains.Count} (will be split into tiles)");
            }
        }

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Streaming Settings", EditorStyles.boldLabel);
        loadRadius = EditorGUILayout.IntSlider("Load Radius", loadRadius, 1, 4);
        unloadGracePeriod = EditorGUILayout.FloatField("Unload Grace Period (s)", unloadGracePeriod);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        outputFolder = EditorGUILayout.TextField("Chunk Scenes Folder", outputFolder);
        terrainDataFolder = EditorGUILayout.TextField("Terrain Data Folder", terrainDataFolder);
        configOutputPath = EditorGUILayout.TextField("Config Asset Path", configOutputPath);
        skipNetworkObjects = EditorGUILayout.Toggle("Skip NetworkObjects", skipNetworkObjects);

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Preview (count objects per chunk)"))
        {
            if (!boundsDetected) ScanWorldBounds();
            if (boundsDetected) PreviewChunks();
        }

        EditorGUILayout.Space(5);

        GUI.enabled = boundsDetected;
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Generate Chunks", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("Generate Chunks",
                $"This will create {calculatedGridDimensions.x * calculatedGridDimensions.y} chunk scenes in '{outputFolder}'.\n\n" +
                (sceneTerrains.Count > 0 ? $"Terrain will be split into {calculatedGridDimensions.x * calculatedGridDimensions.y} tiles.\n\n" : "") +
                "Objects will be COPIED — the master scene will NOT be modified.\n\n" +
                "Continue?", "Generate", "Cancel"))
            {
                GenerateChunks(null);
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        EditorGUILayout.Space(10);

        if (boundsDetected)
        {
            selectiveUpdateExpanded = EditorGUILayout.Foldout(selectiveUpdateExpanded, "Selective Update", true);
            if (!selectiveUpdateExpanded) goto endScrollView;

            EditorGUILayout.HelpBox(
                "Click chunks in the scene view or grid below to select them, then use 'Update Selected Chunks' to regenerate only those.\n" +
                "Shift-click to select a range. Right-click to deselect.",
                MessageType.None);

            DrawSelectableGrid();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                selectedChunks.Clear();
                for (int x = 0; x < calculatedGridDimensions.x; x++)
                    for (int y = 0; y < calculatedGridDimensions.y; y++)
                        selectedChunks.Add(new Vector2Int(x, y));
            }
            if (GUILayout.Button("Clear Selection"))
                selectedChunks.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            GUI.enabled = selectedChunks.Count > 0;
            GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
            if (GUILayout.Button($"Update Selected Chunks ({selectedChunks.Count})", GUILayout.Height(32)))
            {
                if (EditorUtility.DisplayDialog("Update Selected Chunks",
                    $"Regenerate {selectedChunks.Count} selected chunk(s)?\n\nExisting scenes for those chunks will be overwritten.",
                    "Update", "Cancel"))
                {
                    GenerateChunks(selectedChunks);
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        endScrollView:
        EditorGUILayout.EndScrollView();
    }

    private void ScanWorldBounds()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();

        if (roots.Length == 0)
        {
            EditorUtility.DisplayDialog("Empty Scene", "No objects found in the active scene.", "OK");
            boundsDetected = false;
            return;
        }

        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        bool foundAnything = false;
        sceneTerrains.Clear();

        foreach (var root in roots)
        {
            if (skipNetworkObjects && HasNetworkObject(root))
                continue;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var b = renderer.bounds;
                minX = Mathf.Min(minX, b.min.x);
                minZ = Mathf.Min(minZ, b.min.z);
                maxX = Mathf.Max(maxX, b.max.x);
                maxZ = Mathf.Max(maxZ, b.max.z);
                foundAnything = true;
            }

            foreach (var terrain in root.GetComponentsInChildren<Terrain>())
            {
                sceneTerrains.Add(terrain);
                var tPos = terrain.transform.position;
                var tSize = terrain.terrainData.size;
                minX = Mathf.Min(minX, tPos.x);
                minZ = Mathf.Min(minZ, tPos.z);
                maxX = Mathf.Max(maxX, tPos.x + tSize.x);
                maxZ = Mathf.Max(maxZ, tPos.z + tSize.z);
                foundAnything = true;
            }

            if (root.GetComponentsInChildren<Renderer>().Length == 0
                && root.GetComponentsInChildren<Terrain>().Length == 0)
            {
                var pos = root.transform.position;
                minX = Mathf.Min(minX, pos.x);
                minZ = Mathf.Min(minZ, pos.z);
                maxX = Mathf.Max(maxX, pos.x);
                maxZ = Mathf.Max(maxZ, pos.z);
                foundAnything = true;
            }
        }

        if (!foundAnything)
        {
            EditorUtility.DisplayDialog("No Objects", "No valid objects found to chunk.", "OK");
            boundsDetected = false;
            return;
        }

        detectedOrigin = new Vector3(
            Mathf.Floor(minX / chunkSize.x) * chunkSize.x,
            0f,
            Mathf.Floor(minZ / chunkSize.y) * chunkSize.y
        );
        detectedMax = new Vector3(maxX, 0f, maxZ);

        float worldWidth = detectedMax.x - detectedOrigin.x;
        float worldDepth = detectedMax.z - detectedOrigin.z;

        calculatedGridDimensions = new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(worldWidth / chunkSize.x)),
            Mathf.Max(1, Mathf.CeilToInt(worldDepth / chunkSize.y))
        );

        boundsDetected = true;

        Debug.Log($"[WorldChunker] Scanned: origin={detectedOrigin}, " +
                  $"size={worldWidth:F1}x{worldDepth:F1}, " +
                  $"grid={calculatedGridDimensions.x}x{calculatedGridDimensions.y}, " +
                  $"terrains={sceneTerrains.Count}");
    }

    private void PreviewChunks()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var buckets = new Dictionary<Vector2Int, int>();
        int skippedNetwork = 0;

        foreach (var root in roots)
        {
            if (skipNetworkObjects && HasNetworkObject(root))
            {
                skippedNetwork++;
                continue;
            }

            // Skip terrain objects in preview count (they get split, not bucketed)
            if (root.GetComponentInChildren<Terrain>() != null)
                continue;

            var coord = ClampCoord(WorldPosToChunkCoord(GetChunkAnchorPosition(root)));
            buckets[coord] = buckets.GetValueOrDefault(coord, 0) + 1;
        }

        string preview = $"World Origin: {detectedOrigin}\n" +
                         $"Grid: {calculatedGridDimensions.x}x{calculatedGridDimensions.y} " +
                         $"chunks of {chunkSize.x}x{chunkSize.y} units\n";

        if (sceneTerrains.Count > 0)
            preview += $"Terrains: {sceneTerrains.Count} (will be split across all chunks)\n";

        preview += "\nObjects per chunk (excluding terrain):\n";

        for (int y = calculatedGridDimensions.y - 1; y >= 0; y--)
        {
            for (int x = 0; x < calculatedGridDimensions.x; x++)
            {
                var coord = new Vector2Int(x, y);
                int count = buckets.GetValueOrDefault(coord, 0);
                preview += $"[{x},{y}: {count}] ";
            }
            preview += "\n";
        }

        preview += $"\nSkipped NetworkObjects: {skippedNetwork}";

        Debug.Log("[WorldChunker] Preview:\n" + preview);
        EditorUtility.DisplayDialog("Chunk Preview", preview, "OK");
    }

    private void GenerateChunks(HashSet<Vector2Int> onlyCoords)
    {
        if (!boundsDetected)
        {
            ScanWorldBounds();
            if (!boundsDetected) return;
        }

        var masterScene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(masterScene.path))
        {
            EditorUtility.DisplayDialog("Error", "Please save the master scene first.", "OK");
            return;
        }

        EditorSceneManager.SaveScene(masterScene);

        CreateFolderRecursive(outputFolder);
        CreateFolderRecursive(terrainDataFolder);
        CreateFolderRecursive(Path.GetDirectoryName(configOutputPath));

        var roots = masterScene.GetRootGameObjects();

        // Separate terrain objects from regular objects
        var terrainObjects = new List<Terrain>();
        var buckets = new Dictionary<Vector2Int, List<GameObject>>();
        int skippedNetwork = 0;

        foreach (var root in roots)
        {
            if (skipNetworkObjects && HasNetworkObject(root))
            {
                skippedNetwork++;
                continue;
            }

            // Skip cameras and audio listeners — these belong in the persistent scene
            if (root.GetComponentInChildren<Camera>() != null)
                continue;

            // Collect terrains separately — they get split, not copied whole
            var terrainsInRoot = root.GetComponentsInChildren<Terrain>(true);
            if (terrainsInRoot.Length > 0)
            {
                foreach (var terrain in terrainsInRoot)
                {
                    if (!terrainObjects.Contains(terrain))
                        terrainObjects.Add(terrain);
                }
                continue;
            }

            var coord = ClampCoord(WorldPosToChunkCoord(GetChunkAnchorPosition(root)));
            if (!buckets.ContainsKey(coord))
                buckets[coord] = new List<GameObject>();
            buckets[coord].Add(root);
        }

        bool isSelective = onlyCoords != null && onlyCoords.Count > 0;

        // When doing a selective update, load the existing config so we can
        // preserve entries for chunks we are NOT regenerating.
        var existingConfig = AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>(configOutputPath);
        var existingChunkInfos = (isSelective && existingConfig != null && existingConfig.chunks != null)
            ? existingConfig.chunks.ToDictionary(c => c.gridCoord)
            : new Dictionary<Vector2Int, ChunkInfo>();

        // Generate chunk scenes
        var chunkInfos = new List<ChunkInfo>();
        var scenePaths = new List<string>();
        int totalChunks = isSelective ? onlyCoords.Count : calculatedGridDimensions.x * calculatedGridDimensions.y;
        int created = 0;

        for (int cx = 0; cx < calculatedGridDimensions.x; cx++)
        {
            for (int cy = 0; cy < calculatedGridDimensions.y; cy++)
            {
                var coord = new Vector2Int(cx, cy);

                // If selective, skip chunks not in the selection — but preserve their info.
                if (isSelective && !onlyCoords.Contains(coord))
                {
                    if (existingChunkInfos.TryGetValue(coord, out var kept))
                        chunkInfos.Add(kept);
                    continue;
                }
                string sceneName = $"Chunk_{cx}_{cy}";
                string scenePath = $"{outputFolder}/{sceneName}.unity";

                EditorUtility.DisplayProgressBar("Generating Chunks",
                    $"Creating {sceneName}...", (float)created / totalChunks);

                var chunkScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                chunkScene.name = sceneName;

                bool hasTerrain = false;

                // --- Split terrain into this chunk's tile ---
                Vector3 chunkWorldMin = new Vector3(
                    detectedOrigin.x + cx * chunkSize.x,
                    0f,
                    detectedOrigin.z + cy * chunkSize.y
                );

                var tileData = CreateCombinedTerrainTileData(terrainObjects, chunkWorldMin, chunkSize, cx, cy, out var sourceTerrain);
                if (tileData != null && sourceTerrain != null)
                {
                    hasTerrain = true;

                    var terrainGO = new GameObject($"Terrain_{cx}_{cy}");
                    terrainGO.transform.position = chunkWorldMin;

                    var newTerrain = terrainGO.AddComponent<Terrain>();
                    newTerrain.terrainData = tileData;
                    newTerrain.materialTemplate = sourceTerrain.materialTemplate;
                    newTerrain.drawHeightmap = sourceTerrain.drawHeightmap;
                    newTerrain.drawTreesAndFoliage = sourceTerrain.drawTreesAndFoliage;
                    newTerrain.reflectionProbeUsage = sourceTerrain.reflectionProbeUsage;
                    newTerrain.shadowCastingMode = sourceTerrain.shadowCastingMode;
                    newTerrain.heightmapPixelError = sourceTerrain.heightmapPixelError;
                    newTerrain.basemapDistance = sourceTerrain.basemapDistance;
                    newTerrain.drawInstanced = sourceTerrain.drawInstanced;

                    var collider = terrainGO.AddComponent<TerrainCollider>();
                    collider.terrainData = tileData;

                    terrainGO.isStatic = true;

                    SceneManager.MoveGameObjectToScene(terrainGO, chunkScene);
                }

                // --- Copy non-terrain objects ---
                if (buckets.ContainsKey(coord))
                {
                    foreach (var original in buckets[coord])
                    {
                        var copy = UnityEngine.Object.Instantiate(original);
                        copy.name = original.name;
                        copy.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                        copy.transform.localScale = original.transform.localScale;
                        SceneManager.MoveGameObjectToScene(copy, chunkScene);
                    }
                }

                EditorSceneManager.SaveScene(chunkScene, scenePath);
                EditorSceneManager.CloseScene(chunkScene, true);

                var boundsCenter = new Vector3(
                    detectedOrigin.x + (cx + 0.5f) * chunkSize.x,
                    0f,
                    detectedOrigin.z + (cy + 0.5f) * chunkSize.y
                );
                var boundsSize = new Vector3(chunkSize.x, 1000f, chunkSize.y);

                chunkInfos.Add(new ChunkInfo
                {
                    gridCoord = coord,
                    sceneName = sceneName,
                    worldBounds = new Bounds(boundsCenter, boundsSize),
                    hasTerrain = hasTerrain
                });

                scenePaths.Add(scenePath);
                created++;
            }
        }

        EditorUtility.ClearProgressBar();

        // Save config
        var config = AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>(configOutputPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<WorldStreamingConfig>();
            AssetDatabase.CreateAsset(config, configOutputPath);
        }

        config.chunkSize = chunkSize;
        config.gridDimensions = calculatedGridDimensions;
        config.worldOrigin = detectedOrigin;
        config.loadRadius = loadRadius;
        config.unloadGracePeriod = unloadGracePeriod;
        config.chunks = chunkInfos.ToArray();
        EditorUtility.SetDirty(config);

        AddScenesToBuildSettings(scenePaths);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int copiedCount = buckets
            .Where(kvp => !isSelective || onlyCoords.Contains(kvp.Key))
            .Sum(kvp => kvp.Value.Count);
        int terrainTiles = chunkInfos.Count(c => c.hasTerrain && (!isSelective || onlyCoords.Contains(c.gridCoord)));
        string summary = (isSelective ? $"Selective update complete! ({created} chunks)\n\n" : "Chunking complete!\n\n") +
                         $"Created/updated {created} chunk scene(s) in '{outputFolder}'\n" +
                         $"Copied {copiedCount} root objects into chunks\n" +
                         $"Split terrain into {terrainTiles} tile(s)\n" +
                         $"Skipped {skippedNetwork} NetworkObjects\n" +
                         $"Master scene is UNCHANGED\n" +
                         $"Config saved to '{configOutputPath}'";

        Debug.Log("[WorldChunker] " + summary);
        EditorUtility.DisplayDialog("World Chunker", summary, "OK");
    }

    // ─────────────────────────────────────────────
    //  Terrain Splitting
    // ─────────────────────────────────────────────

    private struct TerrainOverlap
    {
        public Terrain Terrain;
        public float Area;
    }

    private TerrainData CreateCombinedTerrainTileData(List<Terrain> srcTerrains, Vector3 chunkWorldMin, Vector2 tileSize, int cx, int cy, out Terrain sourceTerrain)
    {
        sourceTerrain = null;

        var overlaps = GetOverlappingTerrains(srcTerrains, chunkWorldMin, tileSize);
        if (overlaps.Count == 0)
            return null;

        sourceTerrain = overlaps[0].Terrain;
        var templateData = sourceTerrain.terrainData;

        float maxTerrainHeight = overlaps.Max(o => o.Terrain.terrainData.size.y);
        int maxSourceHeightRes = overlaps.Max(o => o.Terrain.terrainData.heightmapResolution);
        float maxSamplesPerUnit = overlaps.Max(o =>
        {
            var data = o.Terrain.terrainData;
            return (data.heightmapResolution - 1) / Mathf.Max(data.size.x, 1f);
        });

        int targetSamples = Mathf.CeilToInt(maxSamplesPerUnit * Mathf.Max(tileSize.x, tileSize.y));
        int tileHeightRes = Mathf.NextPowerOfTwo(Mathf.Max(targetSamples, 32)) + 1;
        tileHeightRes = Mathf.Clamp(tileHeightRes, 33, maxSourceHeightRes);

        var tileData = new TerrainData
        {
            heightmapResolution = tileHeightRes,
            size = new Vector3(tileSize.x, maxTerrainHeight, tileSize.y),
            terrainLayers = templateData.terrainLayers
        };

        var tileHeights = new float[tileHeightRes, tileHeightRes];
        float chunkMaxX = chunkWorldMin.x + tileSize.x;
        float chunkMaxZ = chunkWorldMin.z + tileSize.y;

        for (int tz = 0; tz < tileHeightRes; tz++)
        {
            float zT = (float)tz / (tileHeightRes - 1);
            float worldZ = Mathf.Lerp(chunkWorldMin.z, chunkMaxZ, zT);

            for (int tx = 0; tx < tileHeightRes; tx++)
            {
                float xT = (float)tx / (tileHeightRes - 1);
                float worldX = Mathf.Lerp(chunkWorldMin.x, chunkMaxX, xT);

                var terrain = FindTerrainForWorldPosition(overlaps, worldX, worldZ) ?? sourceTerrain;
                float sampledHeight = SampleTerrainHeight(terrain, worldX, worldZ);
                tileHeights[tz, tx] = Mathf.Clamp01(sampledHeight / Mathf.Max(maxTerrainHeight, 0.0001f));
            }
        }

        tileData.SetHeights(0, 0, tileHeights);

        string assetPath = $"{terrainDataFolder}/TerrainData_{cx}_{cy}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(assetPath);

        AssetDatabase.CreateAsset(tileData, assetPath);

        return tileData;
    }

    /// <summary>
    /// Creates a new TerrainData asset for the chunk at (cx, cy) by sampling
    /// the region of the source terrain that overlaps with this chunk.
    /// Returns null if there is no overlap.
    /// </summary>
    private TerrainData CreateTerrainTileData(Terrain srcTerrain, Vector3 chunkWorldMin, Vector2 tileSize, int cx, int cy)
    {
        var srcData = srcTerrain.terrainData;
        var srcPos = srcTerrain.transform.position;
        var srcSize = srcData.size;

        // Compute overlap between this chunk and the source terrain in world space
        float overlapMinX = Mathf.Max(chunkWorldMin.x, srcPos.x);
        float overlapMinZ = Mathf.Max(chunkWorldMin.z, srcPos.z);
        float overlapMaxX = Mathf.Min(chunkWorldMin.x + tileSize.x, srcPos.x + srcSize.x);
        float overlapMaxZ = Mathf.Min(chunkWorldMin.z + tileSize.y, srcPos.z + srcSize.z);

        if (overlapMaxX <= overlapMinX || overlapMaxZ <= overlapMinZ)
            return null; // No overlap

        // Normalized positions within the source terrain [0, 1]
        float srcNormMinX = (overlapMinX - srcPos.x) / srcSize.x;
        float srcNormMinZ = (overlapMinZ - srcPos.z) / srcSize.z;
        float srcNormMaxX = (overlapMaxX - srcPos.x) / srcSize.x;
        float srcNormMaxZ = (overlapMaxZ - srcPos.z) / srcSize.z;

        // Create new TerrainData
        var tileData = new TerrainData();

        // --- Heightmap ---
        int srcHeightRes = srcData.heightmapResolution; // power-of-2 + 1
        // Compute tile heightmap resolution proportional to coverage
        float coverageFractionX = srcNormMaxX - srcNormMinX;
        float coverageFractionZ = srcNormMaxZ - srcNormMinZ;
        int tileHeightRes = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(srcHeightRes * Mathf.Max(coverageFractionX, coverageFractionZ))) + 1;
        tileHeightRes = Mathf.Clamp(tileHeightRes, 33, srcHeightRes);
        tileData.heightmapResolution = tileHeightRes;

        // Size: tile covers chunkSize in XZ, same height as source
        tileData.size = new Vector3(tileSize.x, srcSize.y, tileSize.y);

        // Sample heightmap from source.
        // Unity terrain APIs use X first and Z second, while the returned array is indexed [z, x].
        int srcStartX = Mathf.Clamp(Mathf.FloorToInt(srcNormMinX * (srcHeightRes - 1)), 0, srcHeightRes - 1);
        int srcStartZ = Mathf.Clamp(Mathf.FloorToInt(srcNormMinZ * (srcHeightRes - 1)), 0, srcHeightRes - 1);
        int srcEndX = Mathf.Clamp(Mathf.CeilToInt(srcNormMaxX * (srcHeightRes - 1)), 0, srcHeightRes - 1);
        int srcEndZ = Mathf.Clamp(Mathf.CeilToInt(srcNormMaxZ * (srcHeightRes - 1)), 0, srcHeightRes - 1);
        int srcSampleW = Mathf.Max(srcEndX - srcStartX + 1, 2);
        int srcSampleH = Mathf.Max(srcEndZ - srcStartZ + 1, 2);
        srcSampleW = Mathf.Min(srcSampleW, srcHeightRes - srcStartX);
        srcSampleH = Mathf.Min(srcSampleH, srcHeightRes - srcStartZ);

        var srcHeights = srcData.GetHeights(srcStartX, srcStartZ, srcSampleW, srcSampleH);

        // Resample to tile resolution using bilinear interpolation
        var tileHeights = new float[tileHeightRes, tileHeightRes];
        for (int tz = 0; tz < tileHeightRes; tz++)
        {
            for (int tx = 0; tx < tileHeightRes; tx++)
            {
                // Map tile pixel to source sample region, clamped to valid range
                float srcFracX = Mathf.Clamp((float)tx / Mathf.Max(tileHeightRes - 1, 1) * (srcSampleW - 1), 0f, srcSampleW - 1f);
                float srcFracZ = Mathf.Clamp((float)tz / Mathf.Max(tileHeightRes - 1, 1) * (srcSampleH - 1), 0f, srcSampleH - 1f);
                tileHeights[tz, tx] = SampleBilinear(srcHeights, srcFracZ, srcFracX);
            }
        }
        tileData.SetHeights(0, 0, tileHeights);

        // --- Terrain layers ---
        tileData.terrainLayers = srcData.terrainLayers;

        // --- Alphamaps (splatmaps) ---
        int srcAlphaRes = srcData.alphamapResolution;
        int numLayers = srcData.alphamapLayers;

        if (numLayers > 0)
        {
            int tileAlphaRes = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(srcAlphaRes * Mathf.Max(coverageFractionX, coverageFractionZ)));
            tileAlphaRes = Mathf.Clamp(tileAlphaRes, 16, srcAlphaRes);
            tileData.alphamapResolution = tileAlphaRes;

            int alphaStartX = Mathf.FloorToInt(srcNormMinX * srcAlphaRes);
            int alphaStartZ = Mathf.FloorToInt(srcNormMinZ * srcAlphaRes);
            int alphaSampleW = Mathf.CeilToInt(coverageFractionX * srcAlphaRes);
            int alphaSampleH = Mathf.CeilToInt(coverageFractionZ * srcAlphaRes);
            alphaSampleW = Mathf.Min(alphaSampleW, srcAlphaRes - alphaStartX);
            alphaSampleH = Mathf.Min(alphaSampleH, srcAlphaRes - alphaStartZ);

            if (alphaSampleW > 0 && alphaSampleH > 0)
            {
                var srcAlphas = srcData.GetAlphamaps(alphaStartX, alphaStartZ, alphaSampleW, alphaSampleH);
                var tileAlphas = new float[tileAlphaRes, tileAlphaRes, numLayers];

                for (int tz = 0; tz < tileAlphaRes; tz++)
                {
                    for (int tx = 0; tx < tileAlphaRes; tx++)
                    {
                        float srcFX = (float)tx / tileAlphaRes * (alphaSampleW - 1);
                        float srcFZ = (float)tz / tileAlphaRes * (alphaSampleH - 1);
                        int sx = Mathf.Clamp(Mathf.RoundToInt(srcFX), 0, alphaSampleW - 1);
                        int sz = Mathf.Clamp(Mathf.RoundToInt(srcFZ), 0, alphaSampleH - 1);

                        for (int layer = 0; layer < numLayers; layer++)
                            tileAlphas[tz, tx, layer] = srcAlphas[sz, sx, layer];
                    }
                }
                tileData.SetAlphamaps(0, 0, tileAlphas);
            }
        }

        // --- Detail layers (grass, etc.) ---
        int srcDetailRes = srcData.detailResolution;
        if (srcDetailRes > 0 && srcData.detailPrototypes.Length > 0)
        {
            int tileDetailRes = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(srcDetailRes * Mathf.Max(coverageFractionX, coverageFractionZ)));
            tileDetailRes = Mathf.Clamp(tileDetailRes, 8, srcDetailRes);

            tileData.SetDetailResolution(tileDetailRes, srcData.detailPatchCount > 0 ? srcData.detailPatchCount : 8);
            tileData.detailPrototypes = srcData.detailPrototypes;

            int detStartX = Mathf.FloorToInt(srcNormMinX * srcDetailRes);
            int detStartZ = Mathf.FloorToInt(srcNormMinZ * srcDetailRes);
            int detSampleW = Mathf.CeilToInt(coverageFractionX * srcDetailRes);
            int detSampleH = Mathf.CeilToInt(coverageFractionZ * srcDetailRes);
            detSampleW = Mathf.Min(detSampleW, srcDetailRes - detStartX);
            detSampleH = Mathf.Min(detSampleH, srcDetailRes - detStartZ);

            for (int layer = 0; layer < srcData.detailPrototypes.Length; layer++)
            {
                if (detSampleW <= 0 || detSampleH <= 0) continue;
                var srcDetail = srcData.GetDetailLayer(detStartX, detStartZ, detSampleW, detSampleH, layer);
                var tileDetail = new int[tileDetailRes, tileDetailRes];

                for (int tz = 0; tz < tileDetailRes; tz++)
                {
                    for (int tx = 0; tx < tileDetailRes; tx++)
                    {
                        int sx = Mathf.Clamp(Mathf.RoundToInt((float)tx / tileDetailRes * (detSampleW - 1)), 0, detSampleW - 1);
                        int sz = Mathf.Clamp(Mathf.RoundToInt((float)tz / tileDetailRes * (detSampleH - 1)), 0, detSampleH - 1);
                        tileDetail[tz, tx] = srcDetail[sz, sx];
                    }
                }
                tileData.SetDetailLayer(0, 0, layer, tileDetail);
            }
        }

        // --- Trees ---
        tileData.treePrototypes = srcData.treePrototypes;
        var tileTreeInstances = new List<TreeInstance>();

        foreach (var tree in srcData.treeInstances)
        {
            // Tree positions are normalized [0,1] within the source terrain
            float treeWorldX = srcPos.x + tree.position.x * srcSize.x;
            float treeWorldZ = srcPos.z + tree.position.z * srcSize.z;

            // Check if the tree falls within this chunk
            if (treeWorldX >= chunkWorldMin.x && treeWorldX < chunkWorldMin.x + tileSize.x
                && treeWorldZ >= chunkWorldMin.z && treeWorldZ < chunkWorldMin.z + tileSize.y)
            {
                var newTree = tree;
                // Re-normalize position to the tile's local space
                newTree.position = new Vector3(
                    (treeWorldX - chunkWorldMin.x) / tileSize.x,
                    tree.position.y,
                    (treeWorldZ - chunkWorldMin.z) / tileSize.y
                );
                tileTreeInstances.Add(newTree);
            }
        }
        tileData.treeInstances = tileTreeInstances.ToArray();

        // Save the TerrainData as an asset
        string assetPath = $"{terrainDataFolder}/TerrainData_{cx}_{cy}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(assetPath);

        AssetDatabase.CreateAsset(tileData, assetPath);

        return tileData;
    }

    private float SampleBilinear(float[,] data, float row, float col)
    {
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        row = Mathf.Clamp(row, 0f, rows - 1f);
        col = Mathf.Clamp(col, 0f, cols - 1f);

        int r0 = Mathf.Min(Mathf.FloorToInt(row), rows - 1);
        int r1 = Mathf.Min(r0 + 1, rows - 1);
        int c0 = Mathf.Min(Mathf.FloorToInt(col), cols - 1);
        int c1 = Mathf.Min(c0 + 1, cols - 1);

        float fr = row - r0;
        float fc = col - c0;

        return Mathf.Lerp(
            Mathf.Lerp(data[r0, c0], data[r0, c1], fc),
            Mathf.Lerp(data[r1, c0], data[r1, c1], fc),
            fr
        );
    }

    private List<TerrainOverlap> GetOverlappingTerrains(List<Terrain> srcTerrains, Vector3 chunkWorldMin, Vector2 tileSize)
    {
        var overlaps = new List<TerrainOverlap>();

        foreach (var srcTerrain in srcTerrains)
        {
            var srcPos = srcTerrain.transform.position;
            var srcSize = srcTerrain.terrainData.size;

            float overlapMinX = Mathf.Max(chunkWorldMin.x, srcPos.x);
            float overlapMinZ = Mathf.Max(chunkWorldMin.z, srcPos.z);
            float overlapMaxX = Mathf.Min(chunkWorldMin.x + tileSize.x, srcPos.x + srcSize.x);
            float overlapMaxZ = Mathf.Min(chunkWorldMin.z + tileSize.y, srcPos.z + srcSize.z);
            float overlapWidth = overlapMaxX - overlapMinX;
            float overlapDepth = overlapMaxZ - overlapMinZ;

            if (overlapWidth <= 0f || overlapDepth <= 0f)
                continue;

            overlaps.Add(new TerrainOverlap
            {
                Terrain = srcTerrain,
                Area = overlapWidth * overlapDepth
            });
        }

        overlaps.Sort((a, b) => b.Area.CompareTo(a.Area));
        return overlaps;
    }

    private Terrain FindTerrainForWorldPosition(List<TerrainOverlap> overlaps, float worldX, float worldZ)
    {
        const float epsilon = 0.001f;

        foreach (var overlap in overlaps)
        {
            var terrain = overlap.Terrain;
            var pos = terrain.transform.position;
            var size = terrain.terrainData.size;

            if (worldX >= pos.x - epsilon && worldX <= pos.x + size.x + epsilon
                && worldZ >= pos.z - epsilon && worldZ <= pos.z + size.z + epsilon)
            {
                return terrain;
            }
        }

        return null;
    }

    private float SampleTerrainHeight(Terrain terrain, float worldX, float worldZ)
    {
        if (terrain == null)
            return 0f;

        var pos = terrain.transform.position;
        var size = terrain.terrainData.size;

        float normX = Mathf.InverseLerp(pos.x, pos.x + size.x, worldX);
        float normZ = Mathf.InverseLerp(pos.z, pos.z + size.z, worldZ);
        float localHeight = terrain.terrainData.GetInterpolatedHeight(normX, normZ);
        return pos.y + localHeight;
    }

    // ─────────────────────────────────────────────
    //  Utilities
    // ─────────────────────────────────────────────

    private bool HasNetworkObject(GameObject obj)
    {
        return obj.GetComponent<Unity.Netcode.NetworkObject>() != null
            || obj.GetComponentInChildren<Unity.Netcode.NetworkObject>() != null;
    }

    private Vector3 GetChunkAnchorPosition(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (var renderer in renderers)
        {
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        foreach (var collider in root.GetComponentsInChildren<Collider>())
        {
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
            return bounds.center;

        return root.transform.position;
    }

    private Vector2Int WorldPosToChunkCoord(Vector3 worldPos)
    {
        float relX = worldPos.x - detectedOrigin.x;
        float relZ = worldPos.z - detectedOrigin.z;
        return new Vector2Int(
            Mathf.FloorToInt(relX / chunkSize.x),
            Mathf.FloorToInt(relZ / chunkSize.y)
        );
    }

    private Vector2Int ClampCoord(Vector2Int coord)
    {
        return new Vector2Int(
            Mathf.Clamp(coord.x, 0, calculatedGridDimensions.x - 1),
            Mathf.Clamp(coord.y, 0, calculatedGridDimensions.y - 1)
        );
    }

    private void AddScenesToBuildSettings(List<string> scenePaths)
    {
        var existingScenes = EditorBuildSettings.scenes
            .Where(s => !s.path.StartsWith(outputFolder + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var existingPaths = new HashSet<string>(existingScenes.Select(s => s.path));

        foreach (var path in scenePaths)
        {
            if (!existingPaths.Contains(path))
            {
                existingScenes.Add(new EditorBuildSettingsScene(path, true));
                existingPaths.Add(path);
            }
        }

        EditorBuildSettings.scenes = existingScenes.ToArray();
    }

    private void CreateFolderRecursive(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path);
        string folderName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            CreateFolderRecursive(parent);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
            AssetDatabase.CreateFolder(parent, folderName);
    }

    private Vector2Int? lastClickedCoord;

    private void DrawSelectableGrid()
    {
        float cellSize = 40f;
        float totalWidth = calculatedGridDimensions.x * cellSize;
        float totalHeight = calculatedGridDimensions.y * cellSize;
        var rect = GUILayoutUtility.GetRect(totalWidth + 40, totalHeight + 40);

        var currentEvent = Event.current;

        for (int y = calculatedGridDimensions.y - 1; y >= 0; y--)
        {
            for (int x = 0; x < calculatedGridDimensions.x; x++)
            {
                int drawY = calculatedGridDimensions.y - 1 - y;
                var cellRect = new Rect(
                    rect.x + 20 + x * cellSize,
                    rect.y + 10 + drawY * cellSize,
                    cellSize - 2, cellSize - 2
                );

                var coord = new Vector2Int(x, y);
                bool isSelected = selectedChunks.Contains(coord);

                Color cellColor = isSelected ? new Color(0.3f, 0.55f, 1f) : new Color(0.3f, 0.3f, 0.3f);
                EditorGUI.DrawRect(cellRect, cellColor);
                GUI.Label(cellRect, $"{x},{y}", EditorStyles.miniLabel);

                if (currentEvent.type == EventType.MouseDown && cellRect.Contains(currentEvent.mousePosition))
                {
                    bool rightClick = currentEvent.button == 1;

                    if (rightClick)
                    {
                        selectedChunks.Remove(coord);
                        lastClickedCoord = null;
                    }
                    else if (currentEvent.shift && lastClickedCoord.HasValue)
                    {
                        // Range select between lastClickedCoord and this coord
                        int minX = Mathf.Min(lastClickedCoord.Value.x, x);
                        int maxX = Mathf.Max(lastClickedCoord.Value.x, x);
                        int minY = Mathf.Min(lastClickedCoord.Value.y, y);
                        int maxY = Mathf.Max(lastClickedCoord.Value.y, y);

                        for (int rx = minX; rx <= maxX; rx++)
                            for (int ry = minY; ry <= maxY; ry++)
                                selectedChunks.Add(new Vector2Int(rx, ry));
                    }
                    else
                    {
                        if (isSelected)
                            selectedChunks.Remove(coord);
                        else
                            selectedChunks.Add(coord);
                        lastClickedCoord = coord;
                    }

                    currentEvent.Use();
                    Repaint();
                }
            }
        }
    }

}
