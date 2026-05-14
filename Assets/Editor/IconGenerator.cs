using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEngine.Rendering.Universal;

public class IconGenerator : EditorWindow
{
    private Camera renderCamera;
    private GameObject prefab;

    private int resolution = 256;
    private string savePath = "Assets/Sprites/";

    private RenderTexture previewRT;
    private GameObject previewInstance;

    private Vector2 previewRotation;
    private float previewRoll;
    private float previewZoom = 1f;

    private Vector2 cameraOffset = Vector2.zero;

    private Vector2 scrollPos;

    [MenuItem("Tools/Icon Generator")]
    public static void Open()
    {
        GetWindow<IconGenerator>("Icon Generator");
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        renderCamera = (Camera)EditorGUILayout.ObjectField(
            "Render Camera", renderCamera, typeof(Camera), true);

        GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab", prefab, typeof(GameObject), false);

        if (newPrefab != prefab)
        {
            prefab = newPrefab;
            RecreatePreviewInstance();
        }

        resolution = EditorGUILayout.IntField("Resolution", resolution);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        GUILayout.Space(10);
        GUILayout.Label("Preview Controls", EditorStyles.boldLabel);

        previewRotation.x = EditorGUILayout.Slider("Pitch", previewRotation.x, -180f, 180f);
        previewRotation.y = EditorGUILayout.Slider("Yaw", previewRotation.y, -180f, 180f);
        previewRoll = EditorGUILayout.Slider("Roll", previewRoll, -180f, 180f);

        previewZoom = EditorGUILayout.Slider("Zoom", previewZoom, 0.05f, 5f);

        cameraOffset = EditorGUILayout.Vector2Field("Camera Offset (X/Y)", cameraOffset);

        GUILayout.Space(10);

        DrawPreview();

        GUILayout.Space(10);

        GUI.enabled = renderCamera != null && prefab != null;

        if (GUILayout.Button("Generate Icon", GUILayout.Height(40)))
        {
            GenerateIcon();
        }

        GUI.enabled = true;

        EditorGUILayout.EndScrollView();

        UpdatePreviewTransform();
    }
    
    private void UpdatePreviewTransform()
    {
        if (!renderCamera) return;

        renderCamera.orthographic = true;
        renderCamera.orthographicSize = previewZoom;

        if (previewInstance)
        {
            previewInstance.transform.rotation =
                Quaternion.Euler(previewRotation.x, previewRotation.y, previewRoll);

            ApplyCameraTransform();
        }
    }
    
    private void ApplyCameraTransform()
    {
        if (!renderCamera || !previewInstance) return;

        Vector3 root = previewInstance.transform.position;

        // Move camera using screen-space axes
        Vector3 right = renderCamera.transform.right;
        Vector3 up = renderCamera.transform.up;

        Vector3 offset = right * cameraOffset.x + up * cameraOffset.y;

        renderCamera.transform.position = root + Vector3.back * 1f + offset;

        renderCamera.transform.rotation = Quaternion.identity;
    }
    
    private void DrawPreview()
    {
        if (!renderCamera)
        {
            EditorGUILayout.HelpBox("Assign a render camera.", MessageType.Info);
            return;
        }

        if (!previewRT)
            previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);

        SetupPreviewCamera();

        ApplyCameraTransform();

        renderCamera.targetTexture = previewRT;
        renderCamera.Render();

        Rect rect = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(false));
        EditorGUI.DrawPreviewTexture(rect, previewRT);
    }
    
    private void SetupPreviewCamera()
    {
        renderCamera.clearFlags = CameraClearFlags.SolidColor;

        // Transparent background (correct for preview)
        renderCamera.backgroundColor = new Color(0, 0, 0, 0);

        renderCamera.allowHDR = false;

        var data = renderCamera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
    }
    
    private void RecreatePreviewInstance()
    {
        if (previewInstance)
            DestroyImmediate(previewInstance);

        if (!prefab || !renderCamera)
            return;

        previewInstance = Instantiate(prefab);
        previewInstance.hideFlags = HideFlags.HideAndDontSave;

        ApplyCameraTransform();
    }


    private void GenerateIcon()
    {
        SetupPreviewCamera();
        ApplyCameraTransform();

        RenderTexture rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        rt.Create();

        renderCamera.targetTexture = rt;
        renderCamera.Render();

        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        Color[] pixels = tex.GetPixels();

        int w = tex.width;
        int h = tex.height;
        
        Color[] result = new Color[pixels.Length];
        pixels.CopyTo(result, 0);

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;

                if (pixels[i].a > 0.01f)
                    continue;

                Color avg = Color.black;
                int count = 0;

                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        Color n = pixels[(y + oy) * w + (x + ox)];

                        if (n.a > 0.01f)
                        {
                            avg += n;
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    avg /= count;
                    avg.a = 0f;
                    result[i] = avg;
                }
            }
        }

        pixels = result;

        tex.SetPixels(pixels);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();

        if (!Directory.Exists(savePath))
            Directory.CreateDirectory(savePath);

        string file = Path.Combine(savePath, prefab.name + ".png");
        File.WriteAllBytes(file, png);

        AssetDatabase.Refresh();

        string assetPath = file.Replace(Application.dataPath, "Assets");
        ImportAsSprite(assetPath);

        RenderTexture.active = null;
        renderCamera.targetTexture = null;

        rt.Release();
        DestroyImmediate(rt);
        DestroyImmediate(tex);

        Debug.Log("Icon saved: " + file);
    }
    
    private void ImportAsSprite(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (!importer) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;

        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        importer.SaveAndReimport();
    }

    private void OnDisable()
    {
        if (previewInstance)
            DestroyImmediate(previewInstance);

        if (previewRT)
            DestroyImmediate(previewRT);
    }
}
