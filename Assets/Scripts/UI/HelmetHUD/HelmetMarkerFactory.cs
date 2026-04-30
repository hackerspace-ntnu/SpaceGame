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
    private static Material sharedSolidHologramMaterial;

    public static HelmetNavMarkers.MarkerView Build(Transform parent, float size, bool useDefaultMaterial = false)
    {
        EnsureTextures();
        if (!useDefaultMaterial) EnsureMaterial();
        Material mat = useDefaultMaterial ? null : sharedHoloMaterial;
        // Arrow uses a clean additive HDR material so it reads like the map hologram.
        Material arrowMat = useDefaultMaterial ? null : EnsureSolidHologramMaterial();

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
            arrow = MakeImage(root, "Arrow", arrowTex, size, arrowMat),
            pip = MakeImage(root, "Pip", pipTex, size * 0.25f, mat),
            label = MakeText(root, "Label", new Vector2(0f, size * 0.7f), 14, TextAlignmentOptions.Bottom, size * 4f),
            distance = MakeText(root, "Distance", new Vector2(0f, -size * 0.7f), 12, TextAlignmentOptions.Top, size * 4f),
        };

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

    private static bool warnedShaderMissing;
    private static void EnsureMaterial()
    {
        if (sharedHoloMaterial != null) return;
        var shader = Shader.Find("UI/HelmetHUDHolographic");
        if (shader == null)
        {
            if (!warnedShaderMissing)
            {
                Debug.LogWarning("[HelmetMarkerFactory] Shader 'UI/HelmetHUDHolographic' not found. Markers will use the default UI shader. Add the shader to Project Settings → Graphics → Always Included Shaders, or create a dummy Material referencing it so Unity tracks it.");
                warnedShaderMissing = true;
            }
            // Fall back to the default UI shader so markers are still visible.
            var fallback = Shader.Find("UI/Default");
            if (fallback != null) sharedHoloMaterial = new Material(fallback) { hideFlags = HideFlags.DontSave };
            return;
        }
        sharedHoloMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
    }

    private static bool warnedSolidShaderMissing;
    private static Material EnsureSolidHologramMaterial()
    {
        if (sharedSolidHologramMaterial != null) return sharedSolidHologramMaterial;
        var shader = Shader.Find("UI/HelmetHUDHologramSolid");
        if (shader == null)
        {
            if (!warnedSolidShaderMissing)
            {
                Debug.LogWarning("[HelmetMarkerFactory] Shader 'UI/HelmetHUDHologramSolid' not found. Arrow will fall back to UI/Default. Add it to Always Included Shaders or reference it from a Material.");
                warnedSolidShaderMissing = true;
            }
            var fallback = Shader.Find("UI/Default");
            if (fallback != null) sharedSolidHologramMaterial = new Material(fallback) { hideFlags = HideFlags.DontSave };
            return sharedSolidHologramMaterial;
        }
        sharedSolidHologramMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        return sharedSolidHologramMaterial;
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
        // Pointing up (+y); rotated by HelmetNavMarkers. Clean filled triangle —
        // the hologram look comes from the additive HDR material (UI/HelmetHUDHologramSolid).
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];

        Vector2 apex      = new Vector2(0.50f, 0.95f);
        Vector2 baseLeft  = new Vector2(0.10f, 0.15f);
        Vector2 baseRight = new Vector2(0.90f, 0.15f);

        float invSize = 1f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * invSize, (y + 0.5f) * invSize);
                float d = PointTriDistance(p, apex, baseLeft, baseRight);
                float dPx = d * size;
                float a = Mathf.Clamp01(0.5f - dPx / 1.2f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    // Signed distance from p to triangle (a,b,c). Negative inside, positive outside.
    private static float PointTriDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // Inside test via sign of cross products (winding-independent: inside iff all three same sign).
        float s1 = Cross(b - a, p - a);
        float s2 = Cross(c - b, p - b);
        float s3 = Cross(a - c, p - c);
        bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
        bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
        bool inside = !(hasNeg && hasPos);

        // Unsigned distance to the closest edge segment.
        float dEdge = Mathf.Sqrt(Mathf.Min(Mathf.Min(SegDistSq(p, a, b), SegDistSq(p, b, c)), SegDistSq(p, c, a)));
        return inside ? -dEdge : dEdge;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    private static float SegDistSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.x * ab.x + ab.y * ab.y;
        if (len2 < 1e-8f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        Vector2 q = a + ab * t;
        return (p - q).sqrMagnitude;
    }

}
