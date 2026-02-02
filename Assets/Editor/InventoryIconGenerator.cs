using UnityEditor;
using UnityEngine;
using System.IO;

public class InventoryIconGenerator : EditorWindow
{
    private Camera renderCamera;
    private GameObject prefab;
    private  InventoryItem itemAsset;
    private int resolution = 256;
    private string savePath = "Assets/Sprites/Items";

    [MenuItem("Tools/Inventory/Icon Generator")]
    public static void Open()
    {
        GetWindow<InventoryIconGenerator>("Icon Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("3D → 2D Icon Generator", EditorStyles.boldLabel);

        renderCamera = (Camera)EditorGUILayout.ObjectField(
            "Render Camera", renderCamera, typeof(Camera), true);

        prefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab", prefab, typeof(GameObject), false);
        
        itemAsset = (InventoryItem)EditorGUILayout.ObjectField(
            "Inventory Item (Optional)",
            itemAsset,
            typeof(InventoryItem),
            false
        );

        resolution = EditorGUILayout.IntField("Resolution", resolution);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        GUILayout.Space(10);

        if (GUILayout.Button("Generate Icon"))
        {
            if (renderCamera == null || prefab == null)
            {
                Debug.LogError("Camera or prefab missing!");
                return;
            }

            GenerateIcon();
        }
    }

    private void GenerateIcon()
    {
        GameObject instance = Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;

        Bounds bounds = GetBounds(instance);

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        renderCamera.targetTexture = rt;

        renderCamera.transform.position =
            bounds.center + Vector3.back * bounds.size.magnitude;
        renderCamera.transform.LookAt(bounds.center);
        
        renderCamera.orthographic = true;
        renderCamera.orthographicSize = bounds.extents.magnitude;
        instance.transform.rotation = Quaternion.Euler(30, 45, 0);

        renderCamera.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();

        if (!Directory.Exists(savePath))
            Directory.CreateDirectory(savePath);

        string file = Path.Combine(savePath, prefab.name + ".png");
        File.WriteAllBytes(file, png);

        AssetDatabase.Refresh();

        string assetPath = file.Replace(Application.dataPath, "Assets");
        Sprite sprite = ImportAsSprite(assetPath);
        
        if (itemAsset != null && sprite != null)
        {
            itemAsset.icon = sprite;
            EditorUtility.SetDirty(itemAsset);
            AssetDatabase.SaveAssets();
        }
        
        DestroyImmediate(instance);
        DestroyImmediate(rt);
        DestroyImmediate(tex);

        renderCamera.targetTexture = null;
        RenderTexture.active = null;

        Debug.Log("Icon saved to: " + file);
    }

    private Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;

        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        return bounds;
    }
    
    private Sprite ImportAsSprite(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError("Failed to get TextureImporter for " + assetPath);
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;

        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }
}
