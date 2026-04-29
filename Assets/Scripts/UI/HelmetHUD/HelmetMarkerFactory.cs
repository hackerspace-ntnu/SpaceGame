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
        // Pointing up (+y); rotated by HelmetNavMarkers.
        // Visual:
        //   - Bold filled chevron (V-shape) with bright outline
        //   - Small gap, then a thin tail tick (cardinal-line look)
        //   - A bright apex dot at the very tip for readability at HUD edges
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];

        float invSize = 1f / size;
        // Edge feather in UV space — a couple of pixels worth.
        float feather = 2f * invSize;

        // Chevron geometry (UV space). Apex up.
        Vector2 apex     = new Vector2(0.50f, 0.94f);
        Vector2 leftOut  = new Vector2(0.10f, 0.46f);
        Vector2 rightOut = new Vector2(0.90f, 0.46f);
        Vector2 leftIn   = new Vector2(0.50f, 0.78f); // notch on the inside making it a V
        Vector2 innerTip = new Vector2(0.50f, 0.50f);
        Vector2 rightIn  = new Vector2(0.50f, 0.78f);

        // The chevron is the union of two triangles: (apex, leftOut, innerTip) and (apex, rightOut, innerTip).
        // Outline thickness in UV space.
        float outline = 0.045f;

        // Tail tick — a thin rectangle below the chevron.
        float tickHalfW   = 0.035f;
        float tickBottom  = 0.10f;
        float tickTop     = 0.30f;

        // Apex glow dot
        Vector2 dot = apex;
        float dotR = 0.06f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * invSize, (y + 0.5f) * invSize);

                // --- Chevron: signed distance to two triangles, take the min ---
                float dTriL = TriSDF(p, apex, leftOut, innerTip);
                float dTriR = TriSDF(p, apex, rightOut, innerTip);
                float dChev = Mathf.Min(dTriL, dTriR);

                // Filled body (soft inside)
                float bodyA = SmoothEdge(dChev, 0f, feather) * 0.55f;
                // Bright outline ring around the chevron edge
                float outlineA = Band(dChev, -outline, 0f, feather);

                // --- Tail tick (thin vertical bar) ---
                float tickX = Mathf.Abs(p.x - 0.5f) - tickHalfW;
                float tickY = Mathf.Max(p.y - tickTop, tickBottom - p.y);
                float dTick = Mathf.Max(tickX, tickY);
                float tickA = SmoothEdge(dTick, 0f, feather);

                // --- Apex glow dot ---
                float dDot = (p - dot).magnitude - dotR;
                float dotCore  = SmoothEdge(dDot, 0f, feather) * 0.85f;
                float dotGlow  = SmoothEdge(dDot, dotR * 0.8f, dotR * 1.6f) * 0.35f;

                float a = outlineA;
                if (bodyA > a) a = bodyA;
                if (tickA  > a) a = tickA;
                if (dotCore > a) a = dotCore;
                a = Mathf.Min(1f, a + dotGlow * 0.6f);

                px[y * size + x] = new Color(1, 1, 1, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    // Smooth 0->1 step where val<edge becomes 1, val>edge+f becomes 0.
    private static float SmoothEdge(float val, float edge, float f)
    {
        return 1f - Mathf.SmoothStep(edge, edge + f, val);
    }

    // Smooth band: 1 between [lo, hi], feathered by f on each side.
    private static float Band(float val, float lo, float hi, float f)
    {
        float a = Mathf.SmoothStep(lo - f, lo, val);
        float b = 1f - Mathf.SmoothStep(hi, hi + f, val);
        return Mathf.Clamp01(Mathf.Min(a, b));
    }

    // Signed distance to a triangle (negative inside).
    private static float TriSDF(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = SignedEdge(p, a, b);
        float d2 = SignedEdge(p, b, c);
        float d3 = SignedEdge(p, c, a);
        // Ensure consistent sign regardless of vertex winding.
        float maxD = Mathf.Max(Mathf.Max(d1, d2), d3);
        float minD = Mathf.Min(Mathf.Min(d1, d2), d3);
        // Inside if all same sign; SDF magnitude is the closest edge.
        return (maxD < 0f) ? maxD : (minD > 0f ? minD : maxD);
    }

    private static float SignedEdge(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = b - a;
        Vector2 n = new Vector2(e.y, -e.x).normalized;
        return Vector2.Dot(p - a, n);
    }
}
