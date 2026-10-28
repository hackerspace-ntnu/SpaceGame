using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Frame rate and frame time in the top-right corner while developer mode is on.
    /// <para>
    /// Averaged over half a second so the numbers can be read, with the worst frame of that
    /// half second beside them, because a hitch hides inside an average. CPU and GPU time come
    /// from <see cref="FrameTimingManager"/> and say which side to optimise: the frame takes as
    /// long as the slower of the two (GDC-L1-PERF-0004). Where the platform reports no GPU time
    /// that half reads "—". The active quality tier is shown too, since every number depends on it.
    /// </para>
    /// <para>
    /// Local to this machine: nothing is networked or saved beyond the developer-mode switch.
    /// </para>
    /// </summary>
    public class PerformanceOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const int SortingOrder = 2200; // over the pause menu and dev browser, so it stays readable
        private const float Margin = 28f;
        private const float PanelWidth = 236f;
        private const float PanelHeight = 118f;
        private const float Padding = 14f;
        private const int FpsSize = 26;
        private const int DetailSize = 17;
        private const float FpsRowHeight = 32f;
        private const float DetailRowHeight = 22f;

        private static PerformanceOverlay instance;

        private readonly FrameTiming[] timing = new FrameTiming[1];

        private GameObject canvasObject;
        private TextMeshProUGUI fpsLabel;
        private TextMeshProUGUI frameLabel;
        private TextMeshProUGUI splitLabel;
        private TextMeshProUGUI tierLabel;

        private float elapsed;
        private float worst;
        private int frames;
        private double cpuTotal;
        private double gpuTotal;
        private int timedFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("PerformanceOverlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerformanceOverlay>();
        }

        private void OnEnable()
        {
            GameSettings.Changed += ApplyDevMode;
            ApplyDevMode();
        }

        private void OnDisable() => GameSettings.Changed -= ApplyDevMode;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void ApplyDevMode()
        {
            bool show = GameSettings.DevMode;
            if (show && canvasObject == null) Build();
            if (canvasObject == null || canvasObject.activeSelf == show) return;

            canvasObject.SetActive(show);
            ResetWindow();
        }

        private void Update()
        {
            if (canvasObject == null || !canvasObject.activeSelf) return;

            float delta = Time.unscaledDeltaTime;
            elapsed += delta;
            worst = Mathf.Max(worst, delta);
            frames++;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) == 1)
            {
                cpuTotal += timing[0].cpuFrameTime;
                gpuTotal += timing[0].gpuFrameTime;
                timedFrames++;
            }

            if (elapsed >= RefreshSeconds) Publish();
        }

        private void Publish()
        {
            fpsLabel.SetText("{0:0} FPS", frames / elapsed);
            frameLabel.SetText("{0:1} ms   worst {1:1}", elapsed / frames * 1000f, worst * 1000f);

            float cpu = timedFrames > 0 ? (float)(cpuTotal / timedFrames) : 0f;
            float gpu = timedFrames > 0 ? (float)(gpuTotal / timedFrames) : 0f;
            if (cpu <= 0f) splitLabel.SetText("CPU —   GPU —");
            else if (gpu <= 0f) splitLabel.SetText("CPU {0:1}   GPU —", cpu);
            else splitLabel.SetText("CPU {0:1}   GPU {1:1}", cpu, gpu);

            string[] tiers = QualitySettings.names;
            tierLabel.text = tiers.Length > 0 ? "Quality " + tiers[QualitySettings.GetQualityLevel()] : string.Empty;

            ResetWindow();
        }

        private void ResetWindow()
        {
            elapsed = 0f;
            worst = 0f;
            frames = 0;
            cpuTotal = 0d;
            gpuTotal = 0d;
            timedFrames = 0;
        }

        private void Build()
        {
            canvasObject = new GameObject("PerformanceCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // UIScale is the only thing in the project that may configure a CanvasScaler.
            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            RectTransform panel = UIBuilder.Rect("Panel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-Margin, -Margin);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Fill", panel)), UITheme.ChipSprite, UITheme.Panel);

            RectTransform rows = UIBuilder.Fill(UIBuilder.Rect("Rows", panel), Padding, Padding, Padding, Padding);
            UIBuilder.Column(rows, 0f);

            fpsLabel = Row(rows, "Fps", FpsSize, FpsRowHeight, UITheme.Bright, FontStyles.Bold);
            frameLabel = Row(rows, "FrameTime", DetailSize, DetailRowHeight, UITheme.Bright, FontStyles.Normal);
            splitLabel = Row(rows, "CpuGpu", DetailSize, DetailRowHeight, UITheme.Muted, FontStyles.Normal);
            tierLabel = Row(rows, "Tier", DetailSize, DetailRowHeight, UITheme.Muted, FontStyles.Normal);
        }

        private static TextMeshProUGUI Row(RectTransform rows, string name, int size, float height, Color color,
                                           FontStyles style)
        {
            RectTransform row = UIBuilder.Rect(name, rows);
            UIBuilder.FixedHeight(row, height);
            return UIBuilder.Label(row, string.Empty, size, color, TextAlignmentOptions.Left, style);
        }
    }
}
