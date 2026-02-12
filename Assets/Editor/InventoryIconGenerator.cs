using UnityEditor;
using UnityEngine;
using System.IO;

public class InventoryIconGenerator : EditorWindow
{
    private Camera renderCamera;
    private GameObject prefab;
    private InventoryItem itemAsset;

    private int resolution = 256;
    private string savePath = "Assets/Sprites/Items";

    private RenderTexture previewRT;
    private GameObject previewInstance;

    private Color previewBorderColor = new Color(1f, 1f, 1f, 0.6f);
    private float previewBorderWidth = 2f;
    private Vector2 previewRotation;
    private float previewZoom = 1f;

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

        GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab", prefab, typeof(GameObject), false);

        if (newPrefab != prefab)
        {
            prefab = newPrefab;
            RecreatePreviewInstance();
        }

        itemAsset = (InventoryItem)EditorGUILayout.ObjectField(
            "Inventory Item (Optional)",
            itemAsset,
            typeof(InventoryItem),
            false
        );

        resolution = EditorGUILayout.IntField("Resolution", resolution);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        GUILayout.Space(10);
        GUILayout.Label("Preview Controls", EditorStyles.boldLabel);

        previewRotation.x = EditorGUILayout.Slider("Pitch", previewRotation.x, -180f, 180f);
        previewRotation.y = EditorGUILayout.Slider("Yaw", previewRotation.y, -180f, 180f);
        previewZoom = EditorGUILayout.Slider("Zoom", previewZoom, 0.05f, 5f);

        if (previewInstance)
        {
            previewInstance.transform.rotation =
                Quaternion.Euler(previewRotation.x, previewRotation.y, 0);

            if (renderCamera)
            {
                renderCamera.orthographic = true;
                renderCamera.orthographicSize = previewZoom;
            }
        }

        GUILayout.Space(10);
        DrawPreview();

        GUILayout.Space(10);

        GUI.enabled = (renderCamera && prefab);

        if (GUILayout.Button("Generate Icon", GUILayout.Height(40)))
        {
            GenerateIcon();
        }

        GUI.enabled = true;
    }

    private void DrawPreview()
    {
        if (!renderCamera)
        {
            EditorGUILayout.HelpBox("Assign a render camera to see preview.", MessageType.Info);
            return;
        }

        if (!previewRT)
            previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);

        renderCamera.targetTexture = previewRT;
        renderCamera.Render();

        Rect rect = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(false));

        // Draw the preview image
        EditorGUI.DrawPreviewTexture(rect, previewRT);

        // Draw border
        Handles.BeginGUI();
        Handles.color = previewBorderColor;

        Vector3 p1 = new Vector3(rect.xMin, rect.yMin);
        Vector3 p2 = new Vector3(rect.xMax, rect.yMin);
        Vector3 p3 = new Vector3(rect.xMax, rect.yMax);
        Vector3 p4 = new Vector3(rect.xMin, rect.yMax);

        Handles.DrawAAPolyLine(previewBorderWidth, p1, p2, p3, p4, p1);
        Handles.EndGUI();
    }

    private void RecreatePreviewInstance()
    {
        if (previewInstance)
            DestroyImmediate(previewInstance);

        if (!prefab || !renderCamera)
            return;

        previewInstance = Instantiate(prefab);
        previewInstance.hideFlags = HideFlags.HideAndDontSave;

        Bounds b = GetBounds(previewInstance);

        renderCamera.transform.position =
            b.center + Vector3.back * b.size.magnitude;

        renderCamera.transform.LookAt(b.center);
    }

    private void GenerateIcon()
    {
        if (!previewInstance)
        {
            Debug.LogError("No preview instance to capture.");
            return;
        }

        RenderTexture rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        renderCamera.targetTexture = rt;
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

        if (itemAsset && sprite)
        {
            itemAsset.icon = sprite;
            EditorUtility.SetDirty(itemAsset);
            AssetDatabase.SaveAssets();
        }

        RenderTexture.active = null;
        renderCamera.targetTexture = null;

        DestroyImmediate(rt);
        DestroyImmediate(tex);

        Debug.Log("Icon saved to: " + file);
    }

    private Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;

        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        return bounds;
    }

    private Sprite ImportAsSprite(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (!importer)
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

    private void OnDisable()
    {
        if (previewInstance != null)
            DestroyImmediate(previewInstance);

        if (previewRT != null)
            DestroyImmediate(previewRT);
    }
}
