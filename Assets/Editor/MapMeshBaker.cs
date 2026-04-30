using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes one low-poly Mesh asset per chunk by sampling each chunk scene's
/// TerrainData heightmap. The runtime hologram (MapHologramTerrain) loads
/// these meshes from Resources and assembles a real 3D terrain hologram.
///
/// Output: Assets/Resources/MapMeshes/MapMesh_{cx}_{cy}.asset
/// </summary>
public class MapMeshBaker : EditorWindow
{
    private WorldStreamingConfig config;
    [Tooltip("Mesh resolution per chunk (verts per side). 65 = 64x64 quads = 8192 tris/chunk.")]
    private int meshResolution = 65;
    private string outputFolder = "Assets/Resources/MapMeshes";

    [MenuItem("Tools/World Streaming/Bake Map Meshes")]
    public static void ShowWindow() => GetWindow<MapMeshBaker>("Map Mesh Baker");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Map Mesh Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bakes a low-poly 3D mesh per chunk from its TerrainData heightmap.\n" +
            "MapHologramTerrain loads these at runtime to build the 3D hologram.",
            MessageType.Info);

        config = (WorldStreamingConfig)EditorGUILayout.ObjectField(
            "World Streaming Config", config, typeof(WorldStreamingConfig), false);

        meshResolution = EditorGUILayout.IntSlider("Mesh Resolution", meshResolution, 9, 257);
        outputFolder   = EditorGUILayout.TextField("Output Folder", outputFolder);

        EditorGUILayout.Space(10);
        GUI.enabled = config != null && config.chunks != null && config.chunks.Length > 0;
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Bake All Chunk Meshes", GUILayout.Height(36)))
            BakeAll();
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
    }

    private void BakeAll()
    {
        CreateFolderRecursive(outputFolder);
        var savedSetup = EditorSceneManager.GetSceneManagerSetup();

        int total = config.chunks.Length;
        int done = 0, baked = 0, skipped = 0;

        try
        {
            foreach (var chunk in config.chunks)
            {
                EditorUtility.DisplayProgressBar("Baking Map Meshes",
                    $"Chunk {chunk.gridCoord.x},{chunk.gridCoord.y}", (float)done / total);

                if (!chunk.hasTerrain || string.IsNullOrEmpty(chunk.scenePath))
                {
                    skipped++; done++; continue;
                }

                var scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive);
                Terrain terrain = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    terrain = root.GetComponentInChildren<Terrain>();
                    if (terrain != null) break;
                }

                if (terrain != null && terrain.terrainData != null)
                {
                    BakeChunkMesh(terrain.terrainData, chunk.gridCoord, config.chunkSize);
                    baked++;
                }
                else skipped++;

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
        EditorUtility.DisplayDialog("Map Mesh Baker",
            $"Baked {baked} mesh(es). Skipped {skipped}.\nOutput: {outputFolder}", "OK");
    }

    private void BakeChunkMesh(TerrainData data, Vector2Int coord, Vector2 chunkSize)
    {
        int res = meshResolution;
        int hres = data.heightmapResolution;
        float[,] raw = data.GetHeights(0, 0, hres, hres);
        float worldHeight = data.size.y;

        var verts  = new Vector3[res * res];
        var uvs    = new Vector2[res * res];
        var tris   = new int[(res - 1) * (res - 1) * 6];

        for (int z = 0; z < res; z++)
        {
            float vT = (float)z / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float uT = (float)x / (res - 1);

                // Bilinear sample of the heightmap.
                float fx = uT * (hres - 1);
                float fz = vT * (hres - 1);
                int x0 = Mathf.FloorToInt(fx), x1 = Mathf.Min(x0 + 1, hres - 1);
                int z0 = Mathf.FloorToInt(fz), z1 = Mathf.Min(z0 + 1, hres - 1);
                float xT = fx - x0, zT = fz - z0;

                float h00 = raw[z0, x0], h10 = raw[z0, x1];
                float h01 = raw[z1, x0], h11 = raw[z1, x1];
                float h = Mathf.Lerp(Mathf.Lerp(h00, h10, xT), Mathf.Lerp(h01, h11, xT), zT);

                int idx = z * res + x;
                verts[idx] = new Vector3(uT * chunkSize.x, h * worldHeight, vT * chunkSize.y);
                uvs[idx]   = new Vector2(uT, vT);
            }
        }

        int t = 0;
        for (int z = 0; z < res - 1; z++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                int i = z * res + x;
                tris[t++] = i;
                tris[t++] = i + res;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + res;
                tris[t++] = i + res + 1;
            }
        }

        var mesh = new Mesh
        {
            name = $"MapMesh_{coord.x}_{coord.y}",
            indexFormat = (verts.Length > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16,
        };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string path = $"{outputFolder}/MapMesh_{coord.x}_{coord.y}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
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
