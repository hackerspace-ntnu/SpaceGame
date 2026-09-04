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

        // Renders the backdrop colour while no gameplay camera exists. Without it Unity draws its
        // own "No cameras rendering" notice during the gap between the old scene unloading and the
        // new scene's camera spawning — the overlay canvas is ScreenSpaceOverlay, so it does not
        // count as something rendering.
        [SerializeField] private Camera fallbackCamera;

        // Shared with the other minigame screens so the whole flow reads as one set.
        private static readonly Color Backdrop = new(0.02f, 0.03f, 0.06f, 1f);
        private static readonly Color Accent = new(0.239f, 0.549f, 0.949f, 1f);
        private static readonly Color Muted = new(0.62f, 0.70f, 0.82f, 1f);

        // How long a step may stall before it is reported. The overlay does NOT come down when this
        // elapses — dropping a player into a half-built world is worse than a long load — it only
        // logs, so a hang is diagnosable from the console instead of being silently shipped to the
        // player as a stutter.
        private const float StallWarningSeconds = 30f;

        // Frames to let render after everything reports ready, so the first-frame shader compile
        // hitch happens behind the overlay instead of in front of the player.
        private const int WarmupFrames = 4;

        // Grace period for a WorldStreamer to appear before the terrain wait is skipped. The
        // streamer spawns with the scene, so it is null for the first frames after the load and
        // looking once would skip the wait that matters most.
        private const float StreamerAppearSeconds = 5f;

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
        /// active, the local player exists, world streaming reports its initial chunks are in, a
        /// camera is rendering, and a few frames have gone by. Pass null for the scene name to skip
        /// the scene wait.
        ///
        /// Deliberately takes no caption: the screen says "Loading" for every route into the game,
        /// singleplayer or multiplayer, so loading never reads as a mode the player did not pick.
        /// </summary>
        public static void ShowUntilReady(string sceneToWaitFor)
        {
            LoadingScreenUI screen = Ensure();
            screen.Begin(sceneToWaitFor);
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

        private void Begin(string sceneToWaitFor)
        {
            if (watching != null)
                StopCoroutine(watching);

            SetVisible(true);
            SetStage();
            watching = StartCoroutine(WaitForReady(sceneToWaitFor));
        }

        // Every step below holds the overlay up until it genuinely passes. Nothing here dismisses
        // early on a timer: the screen coming down is the promise that the game is ready, so a slow
        // machine waits longer rather than being shown a world still assembling itself.
        private IEnumerator WaitForReady(string sceneToWaitFor)
        {
            // 1. The scene itself.
            if (!string.IsNullOrEmpty(sceneToWaitFor))
            {
                yield return WaitUntil(() => IsSceneReady(sceneToWaitFor), "scene load");
            }

            // 2. The local player object. Skipped entirely offline, where nothing spawns through
            //    Netcode and waiting would hang on something that is never coming.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                yield return WaitUntil(
                    () => NetworkManager.Singleton == null ||
                          !NetworkManager.Singleton.IsListening ||
                          (NetworkManager.Singleton.SpawnManager != null &&
                           NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() != null),
                    "player spawn");
            }

            // 3. Terrain chunks and the NavMesh bake. This is the step that actually removes the
            //    stutter; the ones above only get us to a point where it can be measured.
            //
            //    The streamer spawns with the scene, so it is briefly null here — look for a few
            //    seconds before concluding this world has no streaming at all.
            WorldStreamer streamer = null;
            float appearDeadline = Time.realtimeSinceStartup + StreamerAppearSeconds;
            while (streamer == null && Time.realtimeSinceStartup < appearDeadline)
            {
                streamer = FindFirstObjectByType<WorldStreamer>();
                if (streamer == null) yield return null;
            }

            if (streamer != null)
            {
                yield return WaitUntil(() => streamer == null || streamer.InitialChunksLoaded,
                                       "terrain streaming");
            }

            // 4. A camera actually rendering. Until one exists Unity paints its own "no cameras
            //    rendering" screen, and handing control over at that point shows it to the player.
            yield return WaitUntil(HasGameplayCamera, "camera");

            // 5. Let a few frames render behind the overlay to absorb shader compilation.
            for (int i = 0; i < WarmupFrames; i++)
                yield return new WaitForEndOfFrame();

            Hide();
        }

        /// <summary>
        /// Blocks until <paramref name="condition"/> holds, logging once if it takes longer than
        /// <see cref="StallWarningSeconds"/> so a hang is visible in the console. Never gives up —
        /// see the note on WaitForReady.
        /// </summary>
        private IEnumerator WaitUntil(System.Func<bool> condition, string what)
        {
            stage = what;
            float warnAt = Time.realtimeSinceStartup + StallWarningSeconds;
            bool warned = false;

            while (!condition())
            {
                if (!warned && Time.realtimeSinceStartup >= warnAt)
                {
                    warned = true;
                    Debug.LogWarning($"[LoadingScreen] Still waiting on {what} after " +
                                     $"{StallWarningSeconds:0}s. Holding the loading screen up — the " +
                                     "game is not ready yet.");
                }

                yield return null;
            }
        }

        // Any enabled camera rendering to the screen counts. The overlay's own fallback is excluded:
        // it exists precisely to cover the gap, so treating it as "a camera is up" would let the
        // screen come down while the real one is still missing.
        private bool HasGameplayCamera()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == fallbackCamera) continue;
                if (cam.isActiveAndEnabled && cam.targetTexture == null)
                    return true;
            }

            return false;
        }

        private static bool IsSceneReady(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        // The status line is a fixed caption, not a running commentary: the player is told the game
        // is loading, and nothing about which mode, scene or step it happens to be on.
        private void SetStage()
        {
            stage = "loading";
            if (statusText != null)
                statusText.text = "Please wait…";
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

            // Tied to the overlay: it must cover the camera gap while loading, and must not linger
            // afterwards where it would render on top of the real scene camera.
            if (fallbackCamera != null && fallbackCamera.gameObject.activeSelf != visible)
                fallbackCamera.gameObject.SetActive(visible);
        }

        // ──────────────────────────────────────────────
        // Generated overlay
        // ──────────────────────────────────────────────

        private void BuildOverlay()
        {
            BuildFallbackCamera();

            var canvasGo = new GameObject("LoadingCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above every other overlay in the project — this one exists to hide what's behind it.
            canvas.sortingOrder = 5000;

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

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
            // Left at the built-in caption on purpose — see ShowUntilReady.

            var statusGo = CreateChild("Status", panel.transform, out RectTransform statusRect);
            statusRect.anchorMin = new Vector2(0.5f, 0.5f);
            statusRect.anchorMax = new Vector2(0.5f, 0.5f);
            statusRect.anchoredPosition = new Vector2(0f, -50f);
            statusRect.sizeDelta = new Vector2(1200f, 60f);
            // A fixed caption rather than a progress bar: none of these steps report real progress,
            // and a bar that jumps 0 → 100 is worse than saying nothing.
            statusText = NewLabel(statusGo, "Please wait…", 30, Muted);

            var ruleGo = CreateChild("Rule", panel.transform, out RectTransform ruleRect);
            ruleRect.anchorMin = new Vector2(0.5f, 0.5f);
            ruleRect.anchorMax = new Vector2(0.5f, 0.5f);
            ruleRect.anchoredPosition = new Vector2(0f, -10f);
            ruleRect.sizeDelta = new Vector2(360f, 3f);
            ruleGo.AddComponent<Image>().color = Accent;
        }

        // Renders nothing but the backdrop colour. Its whole job is to exist, so that Unity never
        // finds itself with zero enabled cameras and draws its own no-camera notice over the load.
        private void BuildFallbackCamera()
        {
            var camGo = new GameObject("LoadingFallbackCamera");
            camGo.transform.SetParent(transform, false);

            fallbackCamera = camGo.AddComponent<Camera>();
            fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
            fallbackCamera.backgroundColor = Backdrop;
            // Nothing in the world should reach it — the overlay canvas draws on top regardless.
            fallbackCamera.cullingMask = 0;
            // Below any gameplay camera, so the real one takes over the moment it appears.
            fallbackCamera.depth = -100f;
            // A plain camera on an object that also has no AudioListener; adding one would fight the
            // scene's listener and log a duplicate-listener warning during every load.
            fallbackCamera.orthographic = true;
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
