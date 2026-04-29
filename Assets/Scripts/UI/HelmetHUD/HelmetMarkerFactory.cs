using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedurally builds a single AR marker view hierarchy at runtime so the
/// system requires no prefab setup. Each marker has:
///   - root rect (positioned by HelmetNavMarkers)
///   - ring image (visible while target is on-screen)
///   - inner pip image (always-on indicator dot)
///   - arrow image (visible when target is off-screen, rotated to point inward)
///   - label TMP (target label)
///   - distance TMP (distance readout)
///
/// All images use the holographic UI material when available so they get the
/// scanlines + chromatic aberration + flicker treatment.
/// </summary>
public static class HelmetMarkerFactory
{
    private static Texture2D ringTex;
    private static Texture2D pipTex;
    private static Texture2D arrowTex;
    private static Material sharedHoloMaterial;

    public static HelmetNavMarkers.MarkerView Build(Transform parent, float size, bool useDefaultMaterial = false)
    {
        EnsureTextures();
        if (!useDefaultMaterial) EnsureMaterial();
        Material mat = useDefaultMaterial ? null : sharedHoloMaterial;

        var rootGO = new GameObject("NavMarker", typeof(RectTransform));
        var root = (RectTransform)rootGO.transform;
        root.SetParent(parent, false);
        root.sizeDelta = new Vector2(size, size);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);

        var view = new HelmetNavMarkers.MarkerView
        {
            root = root,
            ring = MakeImage(root, "Ring", ringTex, size, mat),
            arrow = MakeImage(root, "Arrow", arrowTex, size, mat),
            label = MakeText(root, "Label", new Vector2(0f, size * 0.7f), 14, TextAlignmentOptions.Bottom, size * 4f),
            distance = MakeText(root, "Distance", new Vector2(0f, -size * 0.7f), 12, TextAlignmentOptions.Top, size * 4f),
        };

        // Inner pip — always visible when on-screen mode is on
        MakeImage(root, "Pip", pipTex, size * 0.25f, mat);

        return view;
    }

    private static Image MakeImage(RectTransform parent, string name, Texture2D tex, float size, Material mat)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var img = go.GetComponent<Image>();
        if (tex != null)
        {
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            img.sprite = sprite;
        }
        if (mat != null) img.material = mat;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI MakeText(RectTransform parent, string name, Vector2 anchored, int fontSize, TextAlignmentOptions align, float width)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, fontSize * 1.6f);
        rt.anchoredPosition = anchored;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.text = "";
        return tmp;
    }

    private static void EnsureTextures()
    {
        if (ringTex == null) ringTex = MakeRing(128, 0.42f, 0.50f);
        if (pipTex == null)  pipTex  = MakeDisc(64, 0.45f);
        if (arrowTex == null) arrowTex = MakeArrow(128);
    }

    private static void EnsureMaterial()
    {
        if (sharedHoloMaterial != null) return;
        var shader = Shader.Find("UI/HelmetHUDHolographic");
        if (shader == null) return;
        sharedHoloMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
    }

    private static Texture2D MakeRing(int size, float innerR, float outerR)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / maxR;
                float ringMask = SmoothBand(r, innerR, outerR, 0.04f);
                // Add 4 small ticks at cardinals
                float tick = TickMask(x, y, size);
                float a = Mathf.Max(ringMask, tick);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    private static float SmoothBand(float r, float inner, float outer, float feather)
    {
        float a = Mathf.SmoothStep(inner - feather, inner, r);
        float b = 1f - Mathf.SmoothStep(outer, outer + feather, r);
        return Mathf.Clamp01(Mathf.Min(a, b));
    }

    private static float TickMask(int x, int y, int size)
    {
        float cx = size * 0.5f - 0.5f;
        float cy = size * 0.5f - 0.5f;
        float dx = Mathf.Abs(x - cx);
        float dy = Mathf.Abs(y - cy);
        float maxR = size * 0.5f;
        // 4 cardinal ticks: thin lines outside outer ring
        float tickThickness = size * 0.025f;
        float tickInner = maxR * 0.55f;
        float tickOuter = maxR * 0.72f;
        bool horiz = dy < tickThickness && dx > tickInner && dx < tickOuter;
        bool vert  = dx < tickThickness && dy > tickInner && dy < tickOuter;
        return (horiz || vert) ? 1f : 0f;
    }

    private static Texture2D MakeDisc(int size, float radius01)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / maxR;
                float a = 1f - Mathf.SmoothStep(radius01 - 0.05f, radius01 + 0.02f, r);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D MakeArrow(int size)
    {
        // Pointing up (+y); rotated by HelmetNavMarkers
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];
        // Triangle: apex at (0.5, 0.92), base from (0.18, 0.18) to (0.82, 0.18)
        Vector2 a = new Vector2(0.5f, 0.92f);
        Vector2 b = new Vector2(0.18f, 0.18f);
        Vector2 c = new Vector2(0.82f, 0.18f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float t = SignedTriDistance(p, a, b, c);
                // t < 0 inside; use feather to make edge smooth
                float alpha = Mathf.Clamp01(-t * size * 0.5f);
                // Inner notch — add a hollow stroke effect
                float stroke = Mathf.Clamp01(0.06f - Mathf.Abs(t) * 4f);
                float final = Mathf.Max(alpha * 0.55f, stroke);
                px[y * size + x] = new Color(1, 1, 1, final);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    private static float SignedTriDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // Approximate "inside-ness" by signed distance to the three edges
        float d1 = SignedEdge(p, a, b);
        float d2 = SignedEdge(p, b, c);
        float d3 = SignedEdge(p, c, a);
        return Mathf.Max(Mathf.Max(d1, d2), d3);
    }

    private static float SignedEdge(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = b - a;
        Vector2 n = new Vector2(e.y, -e.x).normalized;
        return Vector2.Dot(p - a, n);
    }
}
