using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Singleton UI overlay with two black cinematic bars (top + bottom) and a separate
// full-screen black fade. Used by CutsceneDirector for cinematic framing and by
// InteriorManager for hiding the hard-cut between exterior and interior scenes.
//
// Self-instantiates on first access — Instance() builds it from code if no instance
// exists in the scene. Survives scene loads via DontDestroyOnLoad.
//
// All animations use unscaledDeltaTime so they keep running if Time.timeScale is 0.
public class LetterboxOverlay : MonoBehaviour
{
    private static LetterboxOverlay s_instance;

    public static LetterboxOverlay Instance
    {
        get
        {
            if (s_instance != null) return s_instance;

            // A LetterboxOverlay may already exist in a loaded scene whose Awake
            // hasn't run yet (during scene activation). Grab it without forcing a
            // new instance.
            var found = FindFirstObjectByType<LetterboxOverlay>();
            if (found != null)
            {
                s_instance = found;
                return s_instance;
            }

            // Build() calls AddComponent which runs Awake synchronously. Awake
            // assigns s_instance, so by the time Build() returns it's already set.
            // We still re-check rather than reassigning blindly so a race between
            // two callers can't produce two GameObjects.
            Build();
            return s_instance;
        }
    }

    public bool BarsVisible { get; private set; }
    public bool FadeOpaque { get; private set; }

    private RectTransform topBar;
    private RectTransform bottomBar;
    private CanvasGroup barsGroup;
    private Image fadeImage;
    private CanvasGroup fadeGroup;

    private Coroutine barsRoutine;
    private Coroutine fadeRoutine;

    [Tooltip("Fraction of screen height each bar covers when fully shown.")]
    [SerializeField] private float barHeightFraction = 0.12f;

    private void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureBuilt();
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // ──────────── Public API ────────────

    /// <summary>Slide letterbox bars in. Returns when fully shown.</summary>
    public Coroutine ShowBarsAsync(float duration = 0.4f)
    {
        if (barsRoutine != null) StopCoroutine(barsRoutine);
        barsRoutine = StartCoroutine(AnimateBars(true, duration));
        BarsVisible = true;
        return barsRoutine;
    }

    /// <summary>Slide letterbox bars out. Returns when fully hidden.</summary>
    public Coroutine HideBarsAsync(float duration = 0.4f)
    {
        if (barsRoutine != null) StopCoroutine(barsRoutine);
        barsRoutine = StartCoroutine(AnimateBars(false, duration));
        BarsVisible = false;
        return barsRoutine;
    }

    /// <summary>Fade screen to opaque black. Returns when fully black.</summary>
    public Coroutine FadeToBlackAsync(float duration = 0.3f)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(AnimateFade(1f, duration));
        FadeOpaque = true;
        return fadeRoutine;
    }

    /// <summary>Fade screen back from black. Returns when fully transparent.</summary>
    public Coroutine FadeFromBlackAsync(float duration = 0.3f)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(AnimateFade(0f, duration));
        FadeOpaque = false;
        return fadeRoutine;
    }

    /// <summary>
    /// Fade to black, run <paramref name="duringBlack"/>, hold, fade back. The whole
    /// sequence is owned by this (DontDestroyOnLoad) overlay — safe to call from a
    /// MonoBehaviour that won't survive the action (e.g. an InteriorExit on a door
    /// whose scene is about to unload).
    /// </summary>
    public Coroutine FadeOutInAround(System.Action duringBlack,
                                     float fadeOutDur = 0.25f,
                                     float holdDur = 0.4f,
                                     float fadeInDur = 0.35f)
    {
        return StartCoroutine(FadeOutInRoutine(duringBlack, fadeOutDur, holdDur, fadeInDur));
    }

    private IEnumerator FadeOutInRoutine(System.Action duringBlack, float outDur, float hold, float inDur)
    {
        yield return AnimateFade(1f, outDur);
        FadeOpaque = true;
        try { duringBlack?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e); }
        yield return new WaitForSecondsRealtime(hold);
        yield return AnimateFade(0f, inDur);
        FadeOpaque = false;
    }

    /// <summary>Snap bars + fade to a known clean state. Use on hard reset (e.g. respawn).</summary>
    public void SnapClear()
    {
        if (barsRoutine != null) StopCoroutine(barsRoutine);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        SetBarsHeightFraction(0f);
        SetFadeAlpha(0f);
        BarsVisible = false;
        FadeOpaque = false;
    }

    // ──────────── Animation ────────────

    private IEnumerator AnimateBars(bool show, float duration)
    {
        // Read live anchor state so we keep going from wherever a prior animation
        // happened to be when we started — supports being called mid-animation.
        float startFrac = 1f - topBar.anchorMin.y;
        float endFrac = show ? barHeightFraction : 0f;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, duration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            SetBarsHeightFraction(Mathf.Lerp(startFrac, endFrac, k));
            yield return null;
        }
        SetBarsHeightFraction(endFrac);
        barsRoutine = null;
    }

    private IEnumerator AnimateFade(float targetAlpha, float duration)
    {
        float startAlpha = fadeGroup.alpha;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, duration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            SetFadeAlpha(Mathf.Lerp(startAlpha, targetAlpha, k));
            yield return null;
        }
        SetFadeAlpha(targetAlpha);
        fadeRoutine = null;
    }

    private void SetBarsHeightFraction(float frac)
    {
        // Top bar: anchored to the top of the screen, covers `frac` of height.
        topBar.anchorMin = new Vector2(0f, 1f - frac);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.offsetMin = Vector2.zero;
        topBar.offsetMax = Vector2.zero;

        // Bottom bar: mirror.
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, frac);
        bottomBar.offsetMin = Vector2.zero;
        bottomBar.offsetMax = Vector2.zero;

        bool visible = frac > 0.001f;
        if (barsGroup.alpha != (visible ? 1f : 0f))
            barsGroup.alpha = visible ? 1f : 0f;
    }

    private void SetFadeAlpha(float a)
    {
        fadeGroup.alpha = a;
        // Block raycasts only when actually opaque enough to matter.
        fadeGroup.blocksRaycasts = a > 0.5f;
    }

    // ──────────── Build ────────────

    private void EnsureBuilt()
    {
        if (topBar != null) return;

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            var canvasGO = new GameObject("Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000; // above everything
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // Bars container with its own CanvasGroup so we can fade them as a unit.
        var barsContainerGO = new GameObject("Bars", typeof(RectTransform));
        barsContainerGO.transform.SetParent(canvas.transform, false);
        var barsRT = (RectTransform)barsContainerGO.transform;
        barsRT.anchorMin = Vector2.zero; barsRT.anchorMax = Vector2.one;
        barsRT.offsetMin = Vector2.zero; barsRT.offsetMax = Vector2.zero;
        barsGroup = barsContainerGO.AddComponent<CanvasGroup>();
        barsGroup.blocksRaycasts = false;
        barsGroup.interactable = false;
        barsGroup.alpha = 0f;

        topBar    = MakeBar(barsContainerGO.transform, "TopBar");
        bottomBar = MakeBar(barsContainerGO.transform, "BottomBar");
        SetBarsHeightFraction(0f);

        // Fade overlay — separate from bars so we can show one without the other.
        var fadeGO = new GameObject("Fade", typeof(RectTransform));
        fadeGO.transform.SetParent(canvas.transform, false);
        var fadeRT = (RectTransform)fadeGO.transform;
        fadeRT.anchorMin = Vector2.zero; fadeRT.anchorMax = Vector2.one;
        fadeRT.offsetMin = Vector2.zero; fadeRT.offsetMax = Vector2.zero;
        fadeImage = fadeGO.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeGroup = fadeGO.AddComponent<CanvasGroup>();
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;
        fadeGroup.alpha = 0f;
    }

    private static RectTransform MakeBar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    // Synchronous: AddComponent immediately runs Awake, which sets s_instance.
    private static void Build()
    {
        var go = new GameObject("LetterboxOverlay");
        go.AddComponent<LetterboxOverlay>();
    }
}
