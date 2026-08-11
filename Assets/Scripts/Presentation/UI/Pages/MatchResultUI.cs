using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Win/loss screen shown when MatchManager reports the match is over.
//
// Builds its own overlay at runtime when its fields are not Inspector-wired.
// MinigameArena is additively loaded on every peer and carries no result-screen
// GameObject, so a scene-authored panel would have to be duplicated per arena;
// MatchManager calls Ensure() instead and this puts up the same overlay on host
// and clients alike. Fields left serialized so a designed panel can be dropped
// into a scene later and take precedence over the generated one.
public class MatchResultUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private Button returnToMenuButton;
    [SerializeField] private SceneReference mainMenuScene;

    // Shared with MinigameConfigUI so the two minigame screens read as one set.
    private static readonly Color Backdrop = new(0.02f, 0.03f, 0.06f, 0.88f);
    private static readonly Color Accent = new(0.239f, 0.549f, 0.949f, 1f);
    private static readonly Color Muted = new(0.62f, 0.70f, 0.82f, 1f);

    private const string FallbackMainMenuScene = "MainMenu";

    private MatchManager subscribedTo;

    // Called by MatchManager on every peer as the match starts.
    public static MatchResultUI Ensure()
    {
        var existing = FindFirstObjectByType<MatchResultUI>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var go = new GameObject("MatchResultUI");
        return go.AddComponent<MatchResultUI>();
    }

    private void Awake()
    {
        if (panel == null)
            BuildOverlay();

        if (panel != null)
            panel.SetActive(false);

        if (returnToMenuButton != null)
            returnToMenuButton.onClick.AddListener(ReturnToMenu);
    }

    private void Start()
    {
        var matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager == null) return;

        subscribedTo = matchManager;
        subscribedTo.OnMatchEnded += ShowResult;
    }

    private void OnDestroy()
    {
        if (subscribedTo != null)
            subscribedTo.OnMatchEnded -= ShowResult;
    }

    private void ShowResult(int winningTeam)
    {
        if (panel != null)
            panel.SetActive(true);

        if (resultText != null)
            resultText.text = Headline(winningTeam);

        if (subtitleText != null)
            subtitleText.text = MatchSettings.Describe();

        // Final standings stay up without holding Tab. The leaderboard renders on its own canvas
        // below this one, in the gap the top/bottom-anchored layout leaves free.
        MatchLeaderboardUI.Ensure().SetPinned(true);

        ReleaseLocalPlayer();
    }

    // MatchManager assigns the local peer a team on spawn; in the solo modes that
    // team is this player alone, so the same comparison covers all three
    // gamemodes. A peer that never got a player object (-1) just sees the match
    // is over.
    private static string Headline(int winningTeam)
    {
        if (winningTeam < 0) return "Draw";
        if (MatchManager.LocalTeamIndex < 0) return "Match Over";
        return winningTeam == MatchManager.LocalTeamIndex ? "Victory!" : "Defeat";
    }

    // PlayerLook re-locks the cursor every LateUpdate while it is enabled, so the
    // result screen's button is unclickable until the local player is handed over.
    // EnterCutsceneMode is the existing primitive for exactly this: it disables
    // input/look/movement while leaving the camera live so there's still a view
    // behind the panel.
    private void ReleaseLocalPlayer()
    {
        NetworkObject localPlayer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject()
            : null;

        if (localPlayer != null)
        {
            var controller = localPlayer.GetComponent<PlayerController>();
            if (controller != null)
                controller.EnterCutsceneMode();
        }

        // An eliminated player is already free-flying. Stop the spectator reading
        // the mouse so it doesn't drag the view around while the result screen is
        // being clicked, but leave its Camera rendering so there's still a view.
        var spectator = FindFirstObjectByType<SpectatorCamera>();
        if (spectator != null)
            spectator.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReturnToMenu()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        string sceneName = mainMenuScene != null && !string.IsNullOrEmpty(mainMenuScene.SceneName)
            ? mainMenuScene.SceneName
            : FallbackMainMenuScene;

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void BuildOverlay()
    {
        EnsureEventSystem();

        var canvasGo = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above the gameplay HUD

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        panel = CreateChild("Panel", canvasGo.transform, out RectTransform panelRect);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = Backdrop;

        // Headline top-anchored and button bottom-anchored, leaving the middle of the screen for
        // the leaderboard. Anchoring to the edges rather than offsetting from the centre keeps
        // the two screens from colliding regardless of how many rows the table ends up with.
        var textGo = CreateChild("ResultText", panel.transform, out RectTransform textRect);
        textRect.anchorMin = new Vector2(0.5f, 1f);
        textRect.anchorMax = new Vector2(0.5f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0f, -80f);
        textRect.sizeDelta = new Vector2(1200f, 140f);
        resultText = NewLabel(textGo, "", 100, Color.white);

        var subtitleGo = CreateChild("SubtitleText", panel.transform, out RectTransform subtitleRect);
        subtitleRect.anchorMin = new Vector2(0.5f, 1f);
        subtitleRect.anchorMax = new Vector2(0.5f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 1f);
        subtitleRect.anchoredPosition = new Vector2(0f, -216f);
        subtitleRect.sizeDelta = new Vector2(1400f, 56f);
        subtitleText = NewLabel(subtitleGo, "", 28, Muted);

        var buttonGo = CreateChild("ReturnToMenuButton", panel.transform, out RectTransform buttonRect);
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 70f);
        buttonRect.sizeDelta = new Vector2(420f, 88f);

        var buttonImage = buttonGo.AddComponent<Image>();
        returnToMenuButton = buttonGo.AddComponent<Button>();
        returnToMenuButton.targetGraphic = buttonImage;

        var colors = returnToMenuButton.colors;
        colors.normalColor = Accent;
        colors.selectedColor = Accent;
        colors.highlightedColor = Color.Lerp(Accent, Color.white, 0.22f);
        colors.pressedColor = Color.Lerp(Accent, Color.black, 0.25f);
        colors.colorMultiplier = 1f;
        returnToMenuButton.colors = colors;

        var labelGo = CreateChild("Label", buttonGo.transform, out RectTransform labelRect);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        NewLabel(labelGo, "Return to Menu", 34, Color.white);
    }

    // Same treatment as MinigameConfigUI: TextMeshPro on the project's default
    // SDF font, bold, so the result screen matches the menu instead of rendering
    // blurry legacy uGUI text at whatever size the CanvasScaler lands on.
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

    // A click on the result button needs an EventSystem. The arena scene has none
    // of its own, and the project is on the new Input System, so the module has to
    // be InputSystemUIInputModule — StandaloneInputModule is inert here.
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        // Deliberately not DontDestroyOnLoad: it must die with the arena so it
        // can't collide with the main menu's own EventSystem on the way back.
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
