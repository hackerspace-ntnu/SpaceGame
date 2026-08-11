// Full-screen loading overlay that stays up until the world is genuinely playable.
//
// Covering only the scene load would not help: NetworkGameManager already waits for
// WorldStreamer.IsReady and preloads chunks around the spawn before spawning the player, so by
// the time the scene "finishes loading" the expensive part — the NavMesh bake and the first-frame
// shader warmup — has not happened yet. That is the stutter players actually feel. This waits for
// the whole chain instead.
//
// Built at runtime and marked DontDestroyOnLoad, because it has to survive the scene load it is
// covering.
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Presentation
{
    public class LoadingScreenUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI statusText;

        // Shared with the other minigame screens so the whole flow reads as one set.
        private static readonly Color Backdrop = new(0.02f, 0.03f, 0.06f, 1f);
        private static readonly Color Accent = new(0.239f, 0.549f, 0.949f, 1f);
        private static readonly Color Muted = new(0.62f, 0.70f, 0.82f, 1f);

        // Hard ceiling on the whole wait. A streaming failure should drop the player into a rough
        // first few seconds, not strand them on a loading screen with no way out.
        private const float DefaultTimeoutSeconds = 30f;

        // Separate, shorter budget for the terrain/NavMesh wait — see step 3 in WaitForReady.
        private const float StreamingBudgetSeconds = 15f;

        // Frames to let render after everything reports ready, so the first-frame shader compile
        // hitch happens behind the overlay instead of in front of the player.
        private const int WarmupFrames = 4;

        private static LoadingScreenUI instance;
        private Coroutine watching;
        private string stage = "Loading";

        public static LoadingScreenUI Ensure()
        {
            if (instance != null) return instance;

            var go = new GameObject("LoadingScreenUI");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LoadingScreenUI>();
            return instance;
        }

        /// <summary>
        /// Puts the overlay up and holds it until <paramref name="sceneToWaitFor"/> is loaded and
        /// active, the local player exists, world streaming reports its initial chunks are in, and a
        /// few frames have rendered. Pass null for the scene name to skip the scene wait.
        /// </summary>
        public static void ShowUntilReady(string sceneToWaitFor, string title = "Loading",
                                          float timeoutSeconds = DefaultTimeoutSeconds)
        {
            LoadingScreenUI screen = Ensure();
            screen.Begin(sceneToWaitFor, title, timeoutSeconds);
        }

        // Escape hatch for callers that decide the wait is no longer wanted (a failed host start, a
        // cancelled load). Safe to call when nothing is showing.
        public static void Dismiss()
        {
            if (instance != null)
                instance.Hide();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            if (panel == null)
                BuildOverlay();

            SetVisible(false);
        }

        private void Begin(string sceneToWaitFor, string title, float timeoutSeconds)
        {
            if (watching != null)
                StopCoroutine(watching);

            SetVisible(true);
            if (titleText != null)
                titleText.text = title;

            SetStage("Loading world");
            watching = StartCoroutine(WaitForReady(sceneToWaitFor, timeoutSeconds));
        }

        private IEnumerator WaitForReady(string sceneToWaitFor, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);

            // 1. The scene itself.
            if (!string.IsNullOrEmpty(sceneToWaitFor))
            {
                SetStage("Loading world");
                while (!IsSceneReady(sceneToWaitFor))
                {
                    if (TimedOut(deadline)) yield break;
                    yield return null;
                }
            }

            // 2. The local player object. Skipped entirely offline, where nothing spawns through
            //    Netcode and waiting would burn the whole timeout for nothing.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                SetStage("Joining match");
                while (NetworkManager.Singleton.SpawnManager == null ||
                       NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() == null)
                {
                    if (TimedOut(deadline)) yield break;
                    yield return null;
                }
            }

            // 3. Terrain chunks and the NavMesh bake. This is the step that actually removes the
            //    stutter; the ones above only get us to a point where it can be measured.
            //
            //    Given its own shorter budget, and giving up here means "carry on", not "abort":
            //    steps 1 and 2 already passed, so the game is playable. A WorldStreamer that never
            //    reports its initial chunks should cost a rough first few seconds, not the full
            //    timeout spent staring at a black screen.
            var streamer = FindFirstObjectByType<WorldStreamer>();
            if (streamer != null && !streamer.InitialChunksLoaded)
            {
                SetStage("Generating terrain");
                float streamingDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + StreamingBudgetSeconds);

                while (!streamer.InitialChunksLoaded && Time.realtimeSinceStartup < streamingDeadline)
                    yield return null;

                if (!streamer.InitialChunksLoaded)
                    Debug.LogWarning($"[LoadingScreen] World streaming did not report its initial chunks " +
                                     $"within {StreamingBudgetSeconds:0}s. Continuing anyway — expect a " +
                                     "stutter while terrain and the NavMesh finish in the background.");
            }

            // 4. Let a few frames render behind the overlay to absorb shader compilation.
            SetStage("Warming up");
            for (int i = 0; i < WarmupFrames; i++)
                yield return new WaitForEndOfFrame();

            Hide();
        }

        private bool TimedOut(float deadline)
        {
            if (Time.realtimeSinceStartup < deadline)
                return false;

            Debug.LogWarning($"[LoadingScreen] Timed out waiting on '{stage}'. Dismissing so the game " +
                             "stays playable — expect a stutter, and check whether world streaming " +
                             "finished at all.");
            Hide();
            return true;
        }

        private static bool IsSceneReady(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private void SetStage(string value)
        {
            stage = value;
            if (statusText != null)
                statusText.text = value + "…";
        }

        private void Hide()
        {
            if (watching != null)
            {
                StopCoroutine(watching);
                watching = null;
            }

            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
                panel.SetActive(visible);
        }

        // ──────────────────────────────────────────────
        // Generated overlay
        // ──────────────────────────────────────────────

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("LoadingCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above every other overlay in the project — this one exists to hide what's behind it.
            canvas.sortingOrder = 5000;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            panel = CreateChild("Panel", canvasGo.transform, out RectTransform panelRect);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            // Fully opaque: a translucent backdrop would show a half-built world behind it, which
            // looks worse than a clean screen.
            panel.AddComponent<Image>().color = Backdrop;

            var titleGo = CreateChild("Title", panel.transform, out RectTransform titleRect);
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 40f);
            titleRect.sizeDelta = new Vector2(1200f, 140f);
            titleText = NewLabel(titleGo, "Loading", 96, Color.white);

            var statusGo = CreateChild("Status", panel.transform, out RectTransform statusRect);
            statusRect.anchorMin = new Vector2(0.5f, 0.5f);
            statusRect.anchorMax = new Vector2(0.5f, 0.5f);
            statusRect.anchoredPosition = new Vector2(0f, -50f);
            statusRect.sizeDelta = new Vector2(1200f, 60f);
            // The stage name rather than a progress bar: none of these steps report real progress,
            // and a bar that jumps 0 → 100 is worse than an honest label.
            statusText = NewLabel(statusGo, "", 30, Muted);

            var ruleGo = CreateChild("Rule", panel.transform, out RectTransform ruleRect);
            ruleRect.anchorMin = new Vector2(0.5f, 0.5f);
            ruleRect.anchorMax = new Vector2(0.5f, 0.5f);
            ruleRect.anchoredPosition = new Vector2(0f, -10f);
            ruleRect.sizeDelta = new Vector2(360f, 3f);
            ruleGo.AddComponent<Image>().color = Accent;
        }

        private static TextMeshProUGUI NewLabel(GameObject host, string text, int size, Color color)
        {
            var label = host.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return label;
        }

        private static GameObject CreateChild(string name, Transform parent, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            rect = go.GetComponent<RectTransform>();
            return go;
        }
    }
}
